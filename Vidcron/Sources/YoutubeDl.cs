using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Vidcron.Errors;

namespace Vidcron.Sources
{
    public class YoutubeDl : ISource
    {
        #region Consts

        private const string UNIQUE_ID_PREFIX = "youtubedl";
        private const string YOUTUBE_DL_BINARY_NAME = "yt-dlp";

        private static readonly DefaultContractResolver JsonContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };

        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = JsonContractResolver,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private static readonly Lazy<bool> DoesYoutubeDlExist = new Lazy<bool>(
            () => Utilities.IsApplicationInPath(YOUTUBE_DL_BINARY_NAME, SourceLogger)
        );

        // @TODO: how get global config for constructing this? Rejigger to have a factory class?
        private static readonly Logger SourceLogger = new Logger(nameof(YoutubeDl), LogLevel.Information);

        #endregion

        private readonly Logger _logger;
        private readonly YoutubeSourceConfig _sourceConfig;

        public YoutubeDl(SourceConfig sourceConfig, GlobalConfig globalConfig)
        {
            if (!DoesYoutubeDlExist.Value)
            {
                throw new InvalidOperationException($"Cannot process youtube-dl source if {YOUTUBE_DL_BINARY_NAME} is not in PATH");
            }

            _logger = new Logger($"{nameof(YoutubeDl)}:{sourceConfig.Name}", globalConfig.LogLevel);
            _sourceConfig = new YoutubeSourceConfig(sourceConfig);
        }

        public async Task<IEnumerable<DownloadJob>> GetAllDownloads()
        {
            // The plan here is to run youtube-dl in simulate mode and extract all the videos
            // that were in the source collection
            await _logger.Info("Retrieving all videos in source collection");
            string[] getVideosArguments = {"-j", "--flat-playlist", _sourceConfig.Url};
            IReadOnlyCollection<string> videoJsonObjects = await Utilities.GetCommandOutput(YOUTUBE_DL_BINARY_NAME, getVideosArguments, _logger);

            // Each line should be a JSON blob we can use to extract some data
            HashSet<DownloadJob> downloads = new HashSet<DownloadJob>(new DownloadJob.DownloadComparer());
            foreach (string jsonBlob in videoJsonObjects)
            {
                try
                {
                    // Deserialize the json and store it if it's unique
                    VideoDetails videoDetails = JsonConvert.DeserializeObject<VideoDetails>(jsonBlob, JsonSerializerSettings);
                    if (videoDetails == null)
                    {
                        // WARN
                        await _logger.Warn("youtube-dl json output was null");
                        continue;
                    }

                    // Apply filtering logic
                    // @TODO: Make this configurable
                    if (videoDetails.Url.Contains("/shorts/"))
                    {
                        await _logger.Debug($"Skipping {videoDetails.Title} {videoDetails.Url}");
                        continue;
                    }

                    downloads.Add(GenerateDownloadFromVideoDetails(videoDetails));
                }
                catch (JsonException jsonException)
                {
                    // WARN
                    await _logger.Warn($"Failed to deserialize youtube-dl json output: {jsonException}");
                }
            }

            // @TODO: Output how many jobs found
            return downloads;
        }

        private DownloadJob GenerateDownloadFromVideoDetails(VideoDetails videoDetails)
        {
            string uniqueId = $"{UNIQUE_ID_PREFIX}:{videoDetails.Extractor}:{videoDetails.Id}";
            string displayName = $"{uniqueId}: {videoDetails.Title}";

            return new DownloadJob
            {
                RunJob = () => DownloadVideo(videoDetails.Url),
                DisplayName = displayName,
                Logger = _logger,
                UniqueId = uniqueId,
            };
        }

        private async Task<DownloadResult> DownloadVideo(string videoId)
        {
            DateTime startTime = DateTime.Now;
            try
            {
                // Step 1: Download the video
                // Fire up youtube-dl to download the video by ID
                string[] downloadVideoArguments =
                {
                    "--print-json",
                    "--user-agent \"Mozilla/5.0 (compatible; YandexImages/3.0; +http://yandex.com/bots)\"",
                    videoId
                };
                IReadOnlyList<string> downloadOutput = await Utilities.GetCommandOutput(
                    YOUTUBE_DL_BINARY_NAME,
                    downloadVideoArguments,
                    _logger
                );
                if (downloadOutput.Count == 0)
                {
                    throw new ApplicationException("Did not receive any output from youtube-dl!");
                }

                await _logger.Info("Video downloaded successfully");

                // Step 2: Move the file to the destination folder, if provided
                if (!string.IsNullOrWhiteSpace(_sourceConfig.DestinationFolder))
                {
                    await MoveDownloadedFile(downloadOutput[0]);
                }

                return new DownloadResult
                {
                    Error = null,
                    Status = DownloadStatus.Completed,
                    StartTime = startTime,
                    EndTime = DateTime.Now
                };
            }
            catch (Exception e)
            {
                await _logger.Error($"Exception while downloading video: {e.Message}");
                return new DownloadResult
                {
                    Error = e,
                    Status = DownloadStatus.Failed,
                    StartTime = startTime
                };
            }
        }

        private async Task MoveDownloadedFile(string downloadDetailStr)
        {
            // Deserialize the output to get the downloaded file name
            DownloadDetails downloadDetails = JsonConvert.DeserializeObject<DownloadDetails>(downloadDetailStr, JsonSerializerSettings);
            if (downloadDetails == null)
            {
                throw new ApplicationException("Download details deserialized to null");
            }

            try
            {
                // Due to a 5 year old https://github.com/ytdl-org/youtube-dl/issues/5710,
                // we have to do this silly workaround
                string wildFilename = Path.GetFileNameWithoutExtension(downloadDetails.Filename) + ".*";
                string[] videoFileNames = Directory.GetFiles(".", wildFilename);

                if (videoFileNames.Length > 1)
                {
                    await _logger.Warn("Multiple files were downloaded? Only the first one will be moved");
                }

                // Move the file
                string videoFileName = Path.GetFileName(videoFileNames[0]);
                string destinationFilePath = Path.Combine(_sourceConfig.DestinationFolder, videoFileName);
                await _logger.Debug($"Moving file {videoFileName} to {destinationFilePath}");
                await Task.Run(() => File.Move(videoFileName, destinationFilePath, true));
                await _logger.Debug("File moved successfully");
            }
            catch (Exception)
            {
                // Cleanup the file if it failed to be moved
                await _logger.Warn($"Failed moving file, file will be cleaned up");
                File.Delete(downloadDetails.Filename);
                throw;
            }
        }

        private class VideoDetails
        {
            public string Extractor { get; set; }

            public string Id { get; set; }

            public string Title { get; set; }

            public string Url { get; set; }
        }

        private class DownloadDetails
        {
            [JsonProperty("_filename")]
            public string Filename { get; set; }
        }
    }

    public class YoutubeSourceConfig
    {
        private readonly SourceConfig _config;

        public YoutubeSourceConfig(SourceConfig config)
        {
            _config = config;

            if (!_config.Properties.ContainsKey("Url"))
            {
                throw new InvalidConfigurationException($"Property for Youtube source \"{config.Name}\" is missing required property Url");
            }
        }

        public string DestinationFolder => _config.DestinationFolder;

        public string Name => _config.Name;

        public string Url => _config.Properties["Url"];
    }
}
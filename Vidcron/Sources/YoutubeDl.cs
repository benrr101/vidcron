using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Vidcron.Config;
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

        private readonly GlobalConfig _globalConfig;
        private static Logger _logger;
        private readonly YoutubeSourceConfig _sourceConfig;

        public YoutubeDl(SourceConfig sourceConfig, GlobalConfig globalConfig)
        {
            if (!DoesYoutubeDlExist.Value)
            {
                throw new InvalidOperationException($"Cannot process youtube-dl source if {YOUTUBE_DL_BINARY_NAME} is not in PATH");
            }

            _globalConfig = globalConfig;
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
                    PlaylistVideoDetails playlistVideoDetails = JsonConvert.DeserializeObject<PlaylistVideoDetails>(jsonBlob, JsonSerializerSettings);
                    if (playlistVideoDetails == null)
                    {
                        await _logger.Warn("youtube-dl json output was null");
                        continue;
                    }

                    // Apply filtering logic
                    // @TODO: Make this configurable
                    if (!playlistVideoDetails.DurationSeconds.HasValue || playlistVideoDetails.Url.Contains("/shorts/"))
                    {
                        await _logger.Debug($"Skipping {playlistVideoDetails.Title} {playlistVideoDetails.Url}");
                        continue;
                    }

                    downloads.Add(GenerateDownloadFromVideoDetails(playlistVideoDetails));
                }
                catch (JsonException jsonException)
                {
                    // WARN
                    await _logger.Warn($"Failed to deserialize youtube-dl json output: {jsonException}");
                }
            }

            return downloads;
        }

        private DownloadJob GenerateDownloadFromVideoDetails(PlaylistVideoDetails playlistVideoDetails)
        {
            string uniqueId = $"{UNIQUE_ID_PREFIX}:{playlistVideoDetails.Extractor}:{playlistVideoDetails.Id}";
            var durationString = playlistVideoDetails.DurationSeconds.HasValue
                ? TimeSpan.FromSeconds(playlistVideoDetails.DurationSeconds.Value).ToString("g")
                : "??:??";
            string displayName = $"{playlistVideoDetails.Title} ({durationString})";

            return new DownloadJob
            {
                RunJob = () => DownloadVideo(playlistVideoDetails.Url),
                DisplayName = displayName,
                Logger = new Logger(uniqueId, _globalConfig.LogLevel),
                SourceName = _sourceConfig.Name,
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
                    "--no-simulate",
                    "--print \"%()j\"",
                    "--user-agent \"Mozilla/5.0 (compatible; YandexImages/3.0; +http://yandex.com/bots)\"",
                    "--sponsorblock-remove sponsor",
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

            // Due to a 5 year old https://github.com/ytdl-org/youtube-dl/issues/5710,
            // we have to do this silly workaround
            string wildFilename = Path.GetFileNameWithoutExtension(downloadDetails.Filename) + ".*";
            IEnumerable<string> videoFilePaths = Directory.GetFiles(".", wildFilename)
                .Select(Path.GetFileName)
                .ToList();

            try
            {
                foreach (var videoFilePath in videoFilePaths)
                {
                    // Copy the file
                    string destinationFilePath = Path.Combine(_sourceConfig.DestinationFolder, videoFilePath);

                    await _logger.Debug($"Copying file {videoFilePath} to {destinationFilePath}");
                    await Task.Run(() => File.Copy(videoFilePath, destinationFilePath));
                }

                await _logger.Debug("File moved successfully");
            }
            catch (Exception e)
            {
                await _logger.Warn($"Failed moving file, file will be cleaned up: {e.Message}");
                throw;
            }
            finally
            {
                // Cleanup the file in original folder
                foreach (var videoFilePath in videoFilePaths)
                {
                    // Delete the source file
                    await Task.Run(() => File.Delete(videoFilePath));
                }
            }
        }

        private class PlaylistVideoDetails
        {
            [JsonProperty("duration")]
            public double? DurationSeconds { get; set; }
            
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
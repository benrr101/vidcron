using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Vidcron.Errors;

namespace Vidcron.Sources
{
    public class YoutubeDl : ISource
    {
        #region Consts

        private const string UNIQUE_ID_PREFIX = "youtubedl";
        private const string YOUTUBE_DL_BINARY_NAME = "youtube-dl";

        private static readonly DefaultContractResolver JsonContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new SnakeCaseNamingStrategy()
        };

        private static readonly JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings
        {
            ContractResolver = JsonContractResolver,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        private static readonly Lazy<bool> DoesYoutubeDlExist = new Lazy<bool>(() => Utilities.IsApplicationInPath("youtube-dl", SourceLogger));

        private static readonly Logger SourceLogger = new Logger(nameof(YoutubeDl));

        #endregion

        private readonly Logger _logger;
        private readonly YoutubeSourceConfig _sourceConfig;

        public YoutubeDl(SourceConfig sourceConfig)
        {
            if (!DoesYoutubeDlExist.Value)
            {
                throw new InvalidOperationException($"Cannot process youtube-dl source if {YOUTUBE_DL_BINARY_NAME} is not in PATH");
            }

            _logger = new Logger($"{nameof(YoutubeDl)}:{sourceConfig.Name}");
            _sourceConfig = new YoutubeSourceConfig(sourceConfig);
        }

        public async Task<IEnumerable<DownloadJob>> GetAllDownloads()
        {
            // The plan here is to run youtube-dl in simulate mode and extract all the videos
            // that were in the source collection
            _logger.Info("Retrieving all videos in source collection");
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
                        _logger.Warn("youtube-dl json output was null");
                        continue;
                    }

                    downloads.Add(GenerateDownloadFromVideoDetails(videoDetails));
                }
                catch (JsonException jsonException)
                {
                    // WARN
                    _logger.Warn($"Failed to deserialize youtube-dl json output: {jsonException}");
                }
            }

            return downloads;
        }

        private DownloadJob GenerateDownloadFromVideoDetails(VideoDetails videoDetails)
        {
            string uniqueId = $"{UNIQUE_ID_PREFIX}:{videoDetails.Url}";
            string displayName = $"{uniqueId} ({videoDetails.Title})";

            // HACK: This is a work around for youtube-dl being dumb and returning JUST THE ID of
            //     the video as the URL. If the ID starts with a "-" (occasionally happens on YT)
            //     youtube-dl will blowup. This does not appear to be an issue with other sites
            //     like dailymotion.
            if (_sourceConfig.Url.Contains("youtu"))
            {
                videoDetails.Url = $"https://youtu.be/{videoDetails.Url}";
            }

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

                _logger.Info("Video downloaded successfully");

                // Step 2: Move the file to the destination folder, if provided
                if (!string.IsNullOrWhiteSpace(_sourceConfig.DestinationFolder))
                {
                    MoveDownloadedFile(downloadOutput[0]);
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
                return new DownloadResult
                {
                    Error = e,
                    Status = DownloadStatus.Failed,
                    StartTime = startTime
                };
            }
        }

        private void MoveDownloadedFile(string downloadDetailStr)
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
                    _logger.Warn("Multiple files were downloaded? Only the first one will be moved");
                }

                // Move the file
                string destinationFileName = Path.Combine(_sourceConfig.DestinationFolder, videoFileNames[0]);
                _logger.Info($"Moving file {videoFileNames[0]} to {destinationFileName}");
                File.Move(videoFileNames[0], destinationFileName);
                _logger.Info("File moved successfully");
            }
            catch (Exception)
            {
                // Cleanup the file if it failed to be moved
                _logger.Warn($"Failed moving file, file will be cleaned up");
                File.Delete(downloadDetails.Filename);
                throw;
            }
        }

        private class VideoDetails
        {
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
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

        private static readonly Lazy<bool> DoesYoutubeDlExist = new Lazy<bool>(() => Utilities.IsApplicationInPath("youtube-dl"));

        #endregion

        private readonly YoutubeSourceConfig _sourceConfig;

        public YoutubeDl(SourceConfig sourceConfig)
        {
            if (!DoesYoutubeDlExist.Value)
            {
                throw new InvalidOperationException($"Cannot process youtube-dl source if {YOUTUBE_DL_BINARY_NAME} is not in PATH");
            }

            _sourceConfig = new YoutubeSourceConfig(sourceConfig);
        }

        public async Task<IEnumerable<DownloadJob>> GetAllDownloads()
        {
            // The plan here is to run youtube-dl in simulate mode and extract all the videos
            // that were in the source collection
            Console.WriteLine($"[Youtube-dl:{_sourceConfig.Name}] Retrieving all videos in source collection");
            string[] getVideosArguments = {"-j", "--flat-playlist", _sourceConfig.Url};
            IReadOnlyCollection<string> videoJsonObjects = await Utilities.GetCommandOutput(YOUTUBE_DL_BINARY_NAME, getVideosArguments);

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
                        Console.Error.WriteLine($"[Youtube-dl]:{_sourceConfig.Name}] youtube-dl json output was null");
                        continue;
                    }

                    downloads.Add(GenerateDownloadFromVideoDetails(videoDetails));
                }
                catch (JsonException jsonException)
                {
                    // WARN
                    Console.Error.WriteLine($"[Youtube-dl:{_sourceConfig.Name}] Failed to deserialize youtube-dl json output: {jsonException}");
                }
            }

            return downloads;
        }

        private DownloadJob GenerateDownloadFromVideoDetails(VideoDetails videoDetails)
        {
            string uniqueId = $"{UNIQUE_ID_PREFIX}:{videoDetails.Id}";
            string displayName = $"{uniqueId} ({videoDetails.Title})";

            return new DownloadJob
            {
                RunJob = () => DownloadVideo(videoDetails.Id),
                DisplayName = displayName,
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
                    downloadVideoArguments
                );
                if (downloadOutput.Count == 0)
                {
                    throw new ApplicationException("Did not receive any output from youtube-dl!");
                }

                // Step 2: Move the file to the destination folder, if provided
                if (!string.IsNullOrWhiteSpace(_sourceConfig.DestinationFolder))
                {
                    // Deserialize the output to get the downloaded file name
                    DownloadDetails downloadDetails = JsonConvert.DeserializeObject<DownloadDetails>(
                        downloadOutput[0],
                        JsonSerializerSettings
                    );
                    if (downloadDetails == null)
                    {
                        throw new ApplicationException("Download details deserialized to null");
                    }
                    
                    try
                    {
                        // Move the file
                        string destinationFileName = Path.Combine(_sourceConfig.DestinationFolder, downloadDetails.Filename);
                        File.Move(downloadDetails.Filename, destinationFileName);
                    }
                    catch (Exception)
                    {
                        // Cleanup the file if it failed to be moved
                        File.Delete(downloadDetails.Filename);
                        throw;
                    }
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

        private class VideoDetails
        {
            public string Title { get; set; }

            public string Id { get; set; }
        }

        private class DownloadDetails
        {
            [JsonPropertyName("_filename")]
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
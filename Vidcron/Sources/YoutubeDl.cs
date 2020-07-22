using System;
using System.Collections.Generic;
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

        public IEnumerable<Download> AllDownloads
        {
            get
            {
                // The plan here is to run youtube-dl in simulate mode and extract all the videos
                // that were in the source collection
                Console.WriteLine($"[Youtube-dl:{_sourceConfig.Name}] Retrieving all videos in source collection");
                string[] getVideosArguments = {"-j --flat-playlist", _sourceConfig.Url};
                string[] videoJsonObjects = Utilities.GetCommandOutput(YOUTUBE_DL_BINARY_NAME, getVideosArguments);

                // Each line should be a JSON blob we can use to extract some data
                HashSet<Download> downloads = new HashSet<Download>(new Download.DownloadComparer());
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
        }

        private static Download GenerateDownloadFromVideoDetails(VideoDetails videoDetails)
        {
            string uniqueId = $"{UNIQUE_ID_PREFIX}:{videoDetails.Id}";
            string displayName = $"{uniqueId} ({videoDetails.Title})";

            return new Download
            {
                ActionToPerform = () => DownloadVideo(videoDetails.Id),
                DisplayName = displayName,
                UniqueId = uniqueId,
            };
        }

        private static async Task<string> DownloadVideo(string videoId)
        {
            return videoId;
        }

        private class VideoDetails
        {
            public string Title { get; set; }

            public string Id { get; set; }
        }
    }

    public class YoutubeSourceConfig
    {
        private readonly string _url;

        public YoutubeSourceConfig(SourceConfig config)
        {
            if (!config.Properties.TryGetValue("Url", out _url))
            {
                throw new InvalidConfigurationException($"Property for Youtube source \"{config.Name}\" is missing required property Url");
            }
        }

        public string Name;

        public string Url => _url;
    }
}
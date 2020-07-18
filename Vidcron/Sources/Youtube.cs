using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.VisualBasic;
using Newtonsoft.Json;

namespace Vidchron.Sources
{
    public class YoutubePlaylist : ISource
    {
        #region Regex

        private static readonly Regex ChannelRegex = new Regex(
            @"https?://(?:youtu\.be|(?:\w+\.)?youtube(?:-nocookie)?\.com|(?:www\.)?invidio\.us)/channel/(?<id>[0-9A-Za-z_-]+)",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        private static readonly Regex LoadMoreRegex = new Regex(
            @"data-uix-load-more-href=""/?(?<more>[^""]+)",
            RegexOptions.Compiled
        );

        private static readonly Regex PlaylistIsJustVideoRegex = new Regex(
            @"(?:(?:^|//)youtu\.be/|youtube\.com/embed/(?!videoseries))([0-9A-Za-z_-]{11})",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        private static readonly Regex PlaylistRegex = new Regex(
    @"
              (?x)(?:
                  (?:https?://)?
                  (?:\w+\.)?
                  (?:
                      (?:youtube\.com|invidio\.us)
                      /
                      (?:
                          (?:course|view_play_list|my_playlists|artist|playlist|watch|embed/(?:videoseries|[0-9A-Za-z_-]{11}))\?(?:.*?[&;])*?(?:p|a|list)=|
                          p/
                      )|
                      youtu\.be/[0-9A-Za-z_-]{11}\?.*?\blist=
                  )
                  (
                      (?:PL|LL|EC|UU|FL|RD|UL|TL|OLAK5uy_)?[0-9A-Za-z-_]{10,}|
                      (?:MC)[\w\.]*
                  )
                  .*|(%(playlist_id)s)
              )
             ",
    RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        private static readonly Regex PlaylistVideoRegex = new Regex(
            @"href=""\s*/watch\?v=(?<id>[0-9A-Za-z_-]{11})&amp;[^""]*?index=(?<index>\d+)(?:[^>]+>(?<title>[^<]+))?",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        private static readonly Regex YoutubeAlertDivRegex = new Regex(
            @"<div class=""yt-alert-message""[^>]*>([^<]+)</div>",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        private static readonly Regex YoutubeAlertMessageRegex = new Regex(
            @"[^<]*(?:The|This) playlist (?<reason>does not exist|is private)[^<]*",
            RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace
        );

        #endregion

        public IEnumerable<Download> GetAllDownloads(YoutubeSourceConfig config)
        {
            Match playlistMatch = PlaylistRegex.Match(config.Url);
            if (playlistMatch.Success)
            {

            }

            Match channelMatch = ChannelRegex.Match(config.Url);
            if (channelMatch.Success)
            {

            }

            throw new InvalidConfigurationException($"Invalid URL: {config.Url}");
        }

        private IEnumerable<Download> GetAllDownloadsForPlaylist(Match playlistMatch)
        {
            // Extract playlist ID
            string playlistId = playlistMatch.Groups[1].Value;

            // Determine if the playlist is only for a video
            Uri videoUri = new Uri(playlistMatch.Value);
            NameValueCollection videoUriQuery = HttpUtility.ParseQueryString(videoUri.Query);
            if (videoUriQuery["v"] != null || PlaylistIsJustVideoRegex.IsMatch(playlistMatch.Value))
            {
                throw new InvalidConfigurationException($"Playlist URL is a single video: {videoUri.Query}");
            }

            // Determine if the playlist is a "mix"
            if (playlistId.StartsWith("RD") || playlistId.StartsWith("UL") || playlistId.StartsWith("PU"))
            {
                throw new NotSupportedException($"Mix playlists aren't supported.");
            }

            using (WebClient client = new WebClient())
            {
                // Download the playlist page
                // DEBUG
                Console.WriteLine($"Youtube-{playlistId}: Downloading playlist ");
                string playlistPageContents = client.DownloadString(playlistMatch.Value);

                // The yt-alert-message now has tabindex attribute (see
                // https://github.com/ytdl-org/youtube-dl/issues/11604)
                foreach (string match in YoutubeAlertDivRegex.Matches(playlistPageContents).Select(m => m.Groups[1].Value))
                {
                    // Check if the playlist exists or is private
                    string trimmedMatch = match.Trim();
                    Match alertMessageMatch = YoutubeAlertMessageRegex.Match(trimmedMatch);
                    if (alertMessageMatch.Success)
                    {
                        string reason = alertMessageMatch.Groups["reason"].Value;
                        string message = $"This playlist {reason}";
                        throw new InvalidOperationException(message);
                    }
                    if (trimmedMatch.Contains("Invalid parameters"))
                    {
                        throw new InvalidConfigurationException("Invalid parameters, URL may be incorrect");
                    }
                    if (trimmedMatch.Contains("Choose your language"))
                    {
                        continue;
                    }
                }

                return GetEntriesFromVideoList(client, playlistPageContents, playlistId, PlaylistVideoRegex)
                    .Select()
            }
        }

        private IEnumerable<string> GetEntriesFromVideoList(WebClient webClient, string pageContent, string id, Regex videoRegex)
        {
            string moreWidgetHtml = pageContent;
            string contentHtml = pageContent;
            int pageNumber = 0;
            while (true)
            {
                // DEBUG
                Console.WriteLine($"Youtube-{id}: Finding videos in page {pageNumber}");
                foreach (string videoId in ExtractVideosFromPage(contentHtml, videoRegex))
                {
                    yield return videoId;
                }

                Match loadMoreMatch = LoadMoreRegex.Match(moreWidgetHtml);
                if (!loadMoreMatch.Success)
                {
                    break;
                }

                // DEBUG
                Console.WriteLine($"Youtube-{id}: Loading next page");
                string moreJson = webClient.DownloadString($"https://youtube.com/{loadMoreMatch.Groups["more"]}");
                Dictionary<string, string> moreObject = JsonConvert.DeserializeObject<Dictionary<string, string>>(moreJson);
                if (!moreObject.TryGetValue("content_html", out contentHtml) || string.IsNullOrWhiteSpace(contentHtml))
                {
                    // Some pages have a "load more" but don't actually have more videos
                    break;
                }

                moreWidgetHtml = moreObject["load_more_widget_html"];
                pageNumber++;
            }
        }

        private HashSet<string> ExtractVideosFromPage(string pageContent, Regex videoRegex)
        {
            HashSet<string> idsInPage = new HashSet<string>();
            foreach (Match match in videoRegex.Matches(pageContent))
            {
                // The link with index 0 is not the first video of the playlist (not sure if still actual)
                if (match.Groups["index"].Success && match.Groups["id"].Value == "0")
                {
                    continue;
                }

                idsInPage.Add(match.Groups["id"].Value);
            }

            return idsInPage;
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

        public string Url => _url;
    }
}
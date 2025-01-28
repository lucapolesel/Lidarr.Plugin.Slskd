using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins.Slskd;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdParser : IParseIndexerResponse
    {
        public ISlskdProxy Proxy { get; set; }
        public SlskdIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }
        public IArtistService ArtistService { get; set; }
        public IAlbumService AlbumService { get; set; }
        public ITrackService TrackService { get; set; }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse response)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var request = response.Request;

            // NOTE: This is the simplest method to pass data I could think of (Slskd doesn't care about extra headers anyway)
            var artistNameHeader = request.HttpRequest.Headers["SLSKD-ARTIST"];
            var albumNameHeader = request.HttpRequest.Headers["SLSKD-ALBUM"];

            var artistName = HttpUtility.UrlDecode(artistNameHeader, Encoding.UTF8);
            var albumTitle = HttpUtility.UrlDecode(albumNameHeader, Encoding.UTF8);

            // Retrieve the artist by id
            var artist = ArtistService.FindByName(artistName);
            var artistAliases = new List<string>();

            Album album = null;
            List<Track> tracks = null;

            // In case we are performing the RSS request
            if (artist != null)
            {
                var artistMetadata = artist.Metadata.Value;

                artistAliases = new List<string>
                {
                    artist.Name
                };

                // Append aliases cleaning their title
                artistAliases.AddRange(artistMetadata.Aliases);

                album = AlbumService.FindByTitle(artistMetadata.Id, albumTitle);

                // TODO: Make sure album isn't null
                tracks = TrackService.GetTracksByAlbum(album.Id);
            }

            var releaseTitle = $"{artistName} - {albumTitle}";

            var searchResponse = Json.Deserialize<SlskdSearchEntry>(response.Content);

            // Wait for the search to complete
            var searchEntry = Proxy.WaitSearchToComplete(Settings, searchResponse.Id)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            // Delete if failed
            if (searchEntry.State > SlskdStates.CompletedTimedOut)
            {
                _ = Proxy.DeleteSearchAsync(Settings, searchEntry.Id);

                return torrentInfos;
            }

            // Parse each response
            foreach (var r in searchEntry.Responses)
            {
                // Skip those entries that got no files
                if (r.FileCount == 0)
                {
                    continue;
                }

                /*
                 * Group each file by their directory since a user might have different releases and
                 * try to group those are within a single release like multi-disc releases
                 * Note: We can't download those that won't share a unique album directory like: Album Title [Disc 01]
                 *       because we have to provide a single output path to Lidarr, and we would have to move the files manually.
                 */
                var directories = r.Files
                    .GroupBy(f => SlskdUtils.GroupPath(f.Filename, false))
                    .ToList();

                foreach (var dir in directories)
                {
                    // Just to be sure
                    if (string.IsNullOrEmpty(dir.Key))
                    {
                        // TODO: Throw an exception
                        continue;
                    }

                    // We want it to detect the artist name and album at least
                    if (album != null && !MatchRelease(dir.Key, artistAliases.ToArray(), album.Title))
                    {
                        continue;
                    }

                    // Generate a MD5 hash for this directory so we can later use it to identify the files we need
                    var releaseHash = Md5StringConverter.ComputeMd5(dir.Key);

                    // TODO: It shouldn't happen but check the file paths to make sure that they are from the same directory
                    var rInfo = new ReleaseInfo
                    {
                        Guid = $"Slskd-{releaseHash}",
                        Artist = artistName,
                        Album = albumTitle,
                        Title = releaseTitle,
                        InfoUrl = searchEntry.Id,
                        DownloadUrl = r.Username,
                        DownloadProtocol = nameof(SlskdDownloadProtocol),
                    };

                    // Parse the media files by guessing their quality with the info we have
                    var validMediaFiles = SlskdUtils.GetValidMediaFiles(dir);

                    // We weren't able to find any valid media file
                    if (validMediaFiles.Empty())
                    {
                        continue;
                    }

                    // TODO: Check if this is actually worth doing or if it isn't viable in SoulSeek (or add an option to disable it)
                    var distinctQualities = validMediaFiles
                        .DistinctBy(q => new { q.QualityInfo.Codec, q.QualityInfo.Quality })
                        .ToList();

                    // Make sure that all the audio files have the same qualities
                    if (distinctQualities.Count > 1)
                    {
                        continue;
                    }

                    // TODO: Validate the file names based off the album tracks
                    if (tracks != null)
                    {
                        // Make sure that we have enough songs
                        if (validMediaFiles.Length < tracks.Count)
                        {
                            // TODO: Check if we can actually do this in every case (some tracks are TBA, etc.)
                            //continue;
                        }
                    }

                    var mediaQuality = distinctQualities.First().QualityInfo;

                    rInfo.Codec = Enum.GetName(mediaQuality.Codec);
                    rInfo.Container = mediaQuality.Quality.Name;

                    rInfo.Size = validMediaFiles.Sum(f => f.ResponseFile.Size);
                    rInfo.Title += $" [{rInfo.Container}] [WEB]";

                    torrentInfos.Add(rInfo);
                }
            }

            // Delete the search entry if no results
            if (torrentInfos.Count == 0)
            {
                _ = Proxy.DeleteSearchAsync(Settings, searchEntry.Id);
            }

            // Order by size
            return
                torrentInfos
                    .OrderByDescending(o => o.Size)
                    .ToArray();
        }

        private static bool MatchRelease(string path, string[] artistAliases, string albumName)
        {
            var normalizedPath = Normalize(path);

            var aliasPattern = string.Join("|", artistAliases.Select(Normalize));

            // Check for alias followed by the album name
            var closeInfoRegex = $@"\b({aliasPattern})\b.*\b({Regex.Escape(Normalize(albumName))})\b";

            return Regex.IsMatch(normalizedPath, closeInfoRegex, RegexOptions.IgnoreCase);
        }

        private static string Normalize(string input)
        {
            var normalizedString = input.ToLowerInvariant();

            // Preserve words only
            normalizedString = Regex.Replace(normalizedString, @"[^\p{L}\p{N}\s]", " ");

            // Normalize whitespaces
            normalizedString = Regex.Replace(normalizedString, @"\s+", " ");

            return normalizedString.Trim();
        }
    }
}

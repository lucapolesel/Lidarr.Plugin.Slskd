using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Http;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class FileQualityInfo
    {
        public readonly Codec Codec;
        public readonly Quality Quality;

        public FileQualityInfo(Codec codec, Quality quality)
        {
            Codec = codec;
            Quality = quality;
        }
    }

    public class SlskdParser : IParseIndexerResponse
    {
        public ISlskdProxy Proxy { get; set; }
        public SlskdIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }

        public IList<ReleaseInfo> ParseResponse(IndexerResponse response)
        {
            var torrentInfos = new List<ReleaseInfo>();

            var request = response.Request;

            var jsonResponse = new HttpResponse<SlskdSearchEntry>(response.HttpResponse);

            // TODO: Check to make sure about its status
            var entry = jsonResponse.Resource;

            // NOTE: This is the simplest method to pass data I could think of (Slskd doesn't care about extra headers anyway)
            var artistName = request.HttpRequest.Headers["SLSKD-ARTIST"];
            var albumName = request.HttpRequest.Headers["SLSKD-ALBUM"];

            foreach (var r in entry.Responses)
            {
                // Skip those entries that got no files
                if (r.FileCount == 0)
                {
                    continue;
                }

                var title = artistName;

                if (!string.IsNullOrEmpty(albumName))
                {
                    title += $" - {albumName}";
                }

                // Group each file by their directory since a user might have different releases
                var directories = r.Files
                    .GroupBy(f => Path.GetDirectoryName(f.Filename))
                    .ToList();

                foreach (var dir in directories)
                {
                    // Generate a MD5 hash for this directory so we can later use it to identify the files we need
                    var releaseHash = Md5StringConverter.ComputeMd5(dir.Key);

                    // TODO: It shouldn't happen but check the file paths to make sure that they are from the same directory
                    var rInfo = new ReleaseInfo
                    {
                        Guid = $"Slskd-{releaseHash}-{r.Username}",
                        Artist = artistName,
                        Album = albumName,
                        Title = title,
                        InfoUrl = $"/api/v0/searches/{entry.Id}/responses",
                        DownloadUrl = $"/api/v0/transfers/downloads/{r.Username}",
                        DownloadProtocol = nameof(SlskdDownloadProtocol),
                    };

                    // Get all the files that actually have either a BitRate or BitDepth set
                    // TODO: Handle those files with no properties at all (or just trash them?)
                    var files = dir
                        .Where(f => (f.BitRate != null || f.BitDepth != null) && f.Length != null)
                        .ToList();

                    // We weren't able to find any file with the required params
                    if (files.Count == 0)
                    {
                        continue;
                    }

                    // Try to guess the Codec and Quality of each file based off their extension and properties
                    // also ignore any file with no guessed codec
                    var fileQualities = files
                        .Select(GuessFileQuality)
                        .Where(f => f.Codec != Codec.Unknown)
                        .ToList();

                    // If there are no files left then that means we didn't have any valid audio file at all (or not supported by the previous function)
                    if (!fileQualities.Any())
                    {
                        continue;
                    }

                    // TODO: Check if this is actually worth doing or if it isn't viable in SoulSeek (or add an option to disable it)
                    var distinctQualities = fileQualities.DistinctBy(q => new { q.Codec, q.Quality }).ToList();

                    // Make sure that all the audio files have the same qualities
                    if (distinctQualities.Count > 1)
                    {
                        continue;
                    }

                    var mediaQuality = distinctQualities.First();

                    rInfo.Codec = Enum.GetName(mediaQuality.Codec);
                    rInfo.Container = mediaQuality.Quality.Name;

                    rInfo.Size = files.Sum(f => f.Size);
                    rInfo.Title += $" [{rInfo.Container}] [WEB]";

                    torrentInfos.Add(rInfo);
                }
            }

            // order by date
            return
                torrentInfos
                    .OrderByDescending(o => o.Size)
                    .ToArray();
        }

        private static FileQualityInfo GuessFileQuality(SlskdResponseFile file)
        {
            var extension = Path.GetExtension(file.Filename)?.TrimStart('.').ToUpper();

            // NOTE: I wanted to use QualityParser.FindQuality but it's private
            // TODO: Implement more types
            switch (extension)
            {
                case "MP1":
                    return new FileQualityInfo(Codec.MP1, Quality.Unknown);
                case "MP2":
                    return new FileQualityInfo(Codec.MP2, Quality.Unknown);
                case "MP3":
                    if (file.BitRate == null)
                    {
                        return new FileQualityInfo(Codec.MP3VBR, Quality.Unknown);
                    }

                    // NOTE: I'm sorry - I don't like doing this at all so we will change it one day
                    return file.BitRate switch
                    {
                        8 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_008),
                        16 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_016),
                        24 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_024),
                        32 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_032),
                        40 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_040),
                        48 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_048),
                        56 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_056),
                        64 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_064),
                        80 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_080),
                        96 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_096),
                        112 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_112),
                        128 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_128),
                        160 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_160),
                        192 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_192),
                        224 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_224),
                        256 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_256),
                        320 => new FileQualityInfo(Codec.MP3CBR, Quality.MP3_320),
                        _ => new FileQualityInfo(Codec.MP3VBR, Quality.Unknown)
                    };
                case "FLAC":
                    if (file.BitDepth == null)
                    {
                        return new FileQualityInfo(Codec.FLAC, Quality.Unknown);
                    }

                    return file.BitDepth switch
                    {
                        16 => new FileQualityInfo(Codec.FLAC, Quality.FLAC),
                        24 => new FileQualityInfo(Codec.FLAC, Quality.FLAC_24),
                        _ => new FileQualityInfo(Codec.FLAC, Quality.Unknown)
                    };
                case "APE":
                    return new FileQualityInfo(Codec.APE, Quality.APE);
                case "WMA":
                    return new FileQualityInfo(Codec.WMA, Quality.WMA);
                case "WAV":
                    return new FileQualityInfo(Codec.WAV, Quality.WAV);
                case "OGG":
                    if (file.BitRate == null)
                    {
                        return new FileQualityInfo(Codec.OGG, Quality.Unknown);
                    }

                    return file.BitRate switch
                    {
                        160 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q5),
                        192 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q6),
                        224 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q7),
                        256 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q8),
                        320 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q9),
                        500 => new FileQualityInfo(Codec.OGG, Quality.VORBIS_Q10),
                        _ => new FileQualityInfo(Codec.OGG, Quality.Unknown)
                    };
                case "OPUS":
                    if (file.BitRate == null)
                    {
                        return new FileQualityInfo(Codec.OPUS, Quality.Unknown);
                    }

                    return file.BitRate switch
                    {
                        < 130 => new FileQualityInfo(Codec.OPUS, Quality.Unknown),
                        < 180 => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q5),
                        < 205 => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q6),
                        < 240 => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q7),
                        < 290 => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q8),
                        < 410 => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q9),
                        _ => new FileQualityInfo(Codec.OPUS, Quality.VORBIS_Q10)
                    };
                default:
                    return new FileQualityInfo(Codec.Unknown, Quality.Unknown);
            }
        }
    }
}
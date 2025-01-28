using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Parser;
using NzbDrone.Core.Qualities;

namespace NzbDrone.Core.Plugins.Slskd;

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

public class SlskdMediaFile
{
    public readonly SlskdResponseFile ResponseFile;
    public readonly FileQualityInfo QualityInfo;

    public SlskdMediaFile(SlskdResponseFile responseFile, FileQualityInfo qualityInfo)
    {
        ResponseFile = responseFile;
        QualityInfo = qualityInfo;
    }
}

public static class SlskdUtils
{
    public static string GetWindowsFullPath(this OsPath osPath)
    {
        return new OsPath(osPath.FullPath, OsPathKind.Windows).FullPath;
    }

    public static string GroupPath(string path, bool isDirectory)
    {
        // Use OsPath to fix the slashes and use ready-to-use functions
        var filePath = new OsPath(path, OsPathKind.Unix);

        // There may be files with no extension, but we know for sure when we compare which
        var fileDirectory = isDirectory
            ? filePath
            : filePath.Directory;

        // Check if it's part of a multi release
        // TODO: Improve the Regex
        if (Regex.IsMatch(fileDirectory.FileName, @"^(?:Disc|CD|Vinyl)\s*(\d+)$", RegexOptions.IgnoreCase))
        {
            // If so then return the base directory for the grouping
            return fileDirectory.Directory.GetWindowsFullPath();
        }

        // Note: Slskd is using Windows styled paths that's why we are converting them back
        return fileDirectory.GetWindowsFullPath();
    }

    public static SlskdMediaFile[] GetValidMediaFiles(IEnumerable<SlskdResponseFile> responseFiles)
    {
        var validMediaFiles = new List<SlskdMediaFile>();

        foreach (var response in responseFiles)
        {
            // Get all the files that actually have either a BitRate or BitDepth set
            if ((response.BitRate == null || response.BitDepth == null) && response.Length == null)
            {
                continue;
            }

            // Try to guess the Codec and Quality of each file based off their extension and properties
            var quality = GuessFileQuality(response);

            // Ignore any file with no guessed codec
            if (quality.Codec == Codec.Unknown)
            {
                continue;
            }

            validMediaFiles.Add(new SlskdMediaFile(response, quality));
        }

        return validMediaFiles.ToArray();
    }

    private static FileQualityInfo GuessFileQuality(SlskdResponseFile file)
    {
        var extension = Path.GetExtension(file.Filename)?.TrimStart('.').ToUpper(CultureInfo.InvariantCulture);

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

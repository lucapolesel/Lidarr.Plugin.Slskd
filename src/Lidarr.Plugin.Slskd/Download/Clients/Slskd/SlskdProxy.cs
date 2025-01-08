using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Parser.Model;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public interface ISlskdProxy
    {
        SlskdOptions GetOptions(SlskdSettings settings);
        SlskdSearchEntry Search(SlskdIndexerSettings settings, SlskdSearchRequest requestData);
        JsonRequestBuilder BuildSearchEntryRequest(SlskdIndexerSettings settings, string searchId);
        SlskdSearchEntry GetSearchEntry(SlskdIndexerSettings settings, string searchId);
        List<SlskdSearchEntry> GetSearches(SlskdIndexerSettings settings);
        void DeleteSearch(SlskdIndexerSettings settings, string searchId);
        void DeleteAllSearches(SlskdIndexerSettings settings);
        List<SlskdDownloadEntry> GetAllDownloads(SlskdSettings settings);
        List<DownloadClientItem> GetQueue(SlskdSettings settings);
        string Download(SlskdSettings settings, ReleaseInfo release);
        void CancelDownload(SlskdSettings settings, string username, string downloadId, bool remove = false);
        void RemoveFromQueue(SlskdSettings settings, DownloadClientItem downloadItem);
        public void Authenticate(SlskdSettings settings);
        public void Authenticate(SlskdIndexerSettings settings);
    }

    public class SlskdProxy : ISlskdProxy
    {
        private readonly IHttpClient _httpClient;
        private readonly Logger _logger;
        private readonly IHistoryService _historyService;

        public SlskdProxy(ICacheManager cacheManager,
            IHttpClient httpClient,
            Logger logger,
            IHistoryService historyService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _historyService = historyService;
        }

        public SlskdOptions GetOptions(SlskdSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v0/options");
            return ProcessRequest<SlskdOptions>(request);
        }

        public SlskdSearchEntry Search(SlskdIndexerSettings settings, SlskdSearchRequest requestData)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/searches")
                .Post();

            var json = JsonConvert.SerializeObject(requestData);

            request.SetJsonData(json);

            return ProcessRequest<SlskdSearchEntry>(request);
        }

        public JsonRequestBuilder BuildSearchEntryRequest(SlskdIndexerSettings settings, string searchId)
        {
            return BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}")
                .AddQueryParam("includeResponses", true);
        }

        public SlskdSearchEntry GetSearchEntry(SlskdIndexerSettings settings, string searchId)
        {
            var request = BuildSearchEntryRequest(settings, searchId);
            return ProcessRequest<SlskdSearchEntry>(request);
        }

        public List<SlskdSearchEntry> GetSearches(SlskdIndexerSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/searches");
            return ProcessRequest<List<SlskdSearchEntry>>(request);
        }

        public void DeleteSearch(SlskdIndexerSettings settings, string searchId)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}");

            request.Method = HttpMethod.Delete;

            ProcessRequest(request);
        }

        public void DeleteAllSearches(SlskdIndexerSettings settings)
        {
            foreach (var sEntry in GetSearches(settings))
            {
                DeleteSearch(settings, sEntry.Id);
            }
        }

        public List<SlskdDownloadEntry> GetAllDownloads(SlskdSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/transfers/downloads/");
            return ProcessRequest<List<SlskdDownloadEntry>>(request);
        }

        public List<DownloadClientItem> GetQueue(SlskdSettings settings)
        {
            var options = GetOptions(settings);

            var allDownloads = GetAllDownloads(settings);

            var downloadDirectories = allDownloads
                .Where(d => d != null)
                .SelectMany(user => user.Directories)
                .Select(d => ToDownloadClientItem(d, options))
                .Where(i => i != null)
                .ToList();

            return downloadDirectories;
        }

        private static DownloadItemStatus GetFileDownloadStatus(SlskdFile file)
        {
            if (file.State < SlskdFileStatus.InProgress)
            {
                return DownloadItemStatus.Queued;
            }

            if (file.State == SlskdFileStatus.InProgress)
            {
                return DownloadItemStatus.Downloading;
            }

            if (file.State == SlskdFileStatus.CompletedSucceeded)
            {
                return DownloadItemStatus.Completed;
            }

            return DownloadItemStatus.Failed;
        }

        private DownloadClientItem ToDownloadClientItem(SlskdDirectory directory, SlskdOptions options)
        {
            var averageSpeed = directory.Files
                .Select(file => file.AverageSpeed)
                .Where(s => s > 0)
                .DefaultIfEmpty(0)
                .Average();

            var totalSize = directory.Files.Sum(file => file.Size);
            var bytesRemaining = directory.Files.Sum(file => file.BytesRemaining);

            TimeSpan? remainingTime = null;

            if (averageSpeed > 0)
            {
                var tmpRemainingTime = TimeSpan.FromSeconds(bytesRemaining / averageSpeed);

                // Just to make sure we don't go past a certain threshold
                if (tmpRemainingTime.TotalDays < 2)
                {
                    remainingTime = tmpRemainingTime;
                }
            }

            // TODO: This is wrong so fix it later (I forgot why it's wrong)
            var statuses = directory.Files
                .Select(GetFileDownloadStatus)
                .ToArray();

            DownloadItemStatus status;

            if (statuses.All(s => s == DownloadItemStatus.Queued))
            {
                status = DownloadItemStatus.Queued;
            }
            else if (statuses.Any(s => s == DownloadItemStatus.Failed))
            {
                status = DownloadItemStatus.Failed;
            }
            else if (statuses.All(s => s == DownloadItemStatus.Completed))
            {
                status = DownloadItemStatus.Completed;
            }
            else
            {
                status = DownloadItemStatus.Downloading;
            }

            // Compute a DownloadId based off the directory
            // NOTE: It ain't pretty but, it should work just fine since we always (soontm) clear the downloads
            var downloadId = Md5StringConverter.ComputeMd5(directory.Directory);

            // Retrieve the album data from the history service
            var releaseInfo = _historyService
                .FindByDownloadId(downloadId)
                .FirstOrDefault();

            if (releaseInfo is null)
            {
                return null;
            }

            // Slskd trims down everything like username and only outputs to the last path
            var downloadsPath = options.Directories.Downloads;
            var releasePath = directory.Directory[(directory.Directory.LastIndexOf('\\') + 1) ..];

            var outputPath = Path.Combine(downloadsPath, releasePath!);

            var item = new DownloadClientItem
            {
                DownloadId = downloadId,
                Title = releaseInfo.SourceTitle,
                TotalSize = totalSize,
                RemainingSize = bytesRemaining,
                RemainingTime = remainingTime,
                Status = status,
                CanMoveFiles = true,
                CanBeRemoved = true,
                OutputPath = new OsPath(outputPath),
                Category = "music",
            };

            return item;
        }

        public void CancelDownload(SlskdSettings settings, string username, string downloadId, bool remove = false)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/transfers/downloads/{username}/{downloadId}")
                .AddQueryParam("remove", remove);
            request.Method = HttpMethod.Delete;
            ProcessRequest(request);
        }

        public void RemoveFromQueue(SlskdSettings settings, DownloadClientItem downloadItem)
        {
            var allDownloads = GetAllDownloads(settings);

            // TODO: We should only have a single match but this will do for now
            // TODO: I think we might have cases with multiple artists in a single directory?
            foreach (var entry in allDownloads)
            {
                var filesToRemove = entry.Directories
                    .Where(directory => Md5StringConverter.ComputeMd5(directory.Directory) == downloadItem.DownloadId)
                    .SelectMany(directory => directory.Files);

                foreach (var file in filesToRemove)
                {
                    // TODO: We have to cancel the download before actually removing it or slskd would return an error
                    // TODO: Check the file status and either just cancel or remove it
                    CancelDownload(settings, entry.Username, file.Id, false);
                }
            }
        }

        public string Download(SlskdSettings settings, ReleaseInfo release)
        {
            // Parse the guid
            var matches = Regex.Match(release.Guid, @"(Slskd)-(.+)-(.+)");

            var slskd = matches.Groups[1].Value;

            if (slskd != "Slskd")
            {
                throw new DownloadClientException($"The provided guid doesn't appear to come from this plugin: '{release.Guid}'");
            }

            var releaseId = matches.Groups[2].Value;
            var username = matches.Groups[3].Value;

            // Retrieve the list of files to download
            var sEntry = BuildRequest(settings)
                .Resource(release.InfoUrl);

            var sResponses = ProcessRequest<List<SlskdResponse>>(sEntry);

            // Find the Responses for that specific user
            var userResponse = sResponses.FirstOrDefault(r => r.Username == username);

            if (userResponse == null)
            {
                throw new DownloadClientException("User not found.");
            }

            // We only want the files that are in the requested directory
            var filesToDownload = userResponse.Files
                .GroupBy(f => f.Filename[..f.Filename.LastIndexOf('\\')])
                .FirstOrDefault(g => Md5StringConverter.ComputeMd5(g.Key) == releaseId);

            if (filesToDownload == null)
            {
                throw new DownloadClientException("Unable to retrieve files per releaseId.");
            }

            // Send the download request
            var request = BuildRequest(settings)
                .Resource(release.DownloadUrl)
                .Post();

            var json = JsonConvert.SerializeObject(filesToDownload);

            request.SetJsonData(json);

            var response = ProcessRequest(request);

            if (response.StatusCode == HttpStatusCode.Created)
            {
                _logger.Trace("Downloading item {0}", releaseId);

                return releaseId;
            }

            throw new DownloadClientException("Error adding item to Slskd: StatusCode {0}", response.StatusCode);
        }

        private JsonRequestBuilder BuildRequest(SlskdSettings settings)
        {
            return new JsonRequestBuilder(settings.UseSsl, settings.Host, settings.Port, settings.UrlBase)
            {
                LogResponseContent = true
            }.SetHeader("X-API-KEY", settings.ApiKey);
        }

        private JsonRequestBuilder BuildRequest(SlskdIndexerSettings settings)
        {
            return new JsonRequestBuilder(settings.BaseUrl)
            {
                LogResponseContent = true
            }.SetHeader("X-API-KEY", settings.ApiKey);
        }

        private TResult ProcessRequest<TResult>(JsonRequestBuilder requestBuilder)
            where TResult : new()
        {
            var responseContent = ProcessRequest(requestBuilder).Content;

            return Json.Deserialize<TResult>(responseContent);
        }

        private HttpResponse ProcessRequest(JsonRequestBuilder requestBuilder)
        {
            var request = requestBuilder.Build();
            requestBuilder.LogResponseContent = true;
            requestBuilder.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.Forbidden };

            try
            {
                return _httpClient.Execute(request);
            }
            catch (HttpException ex)
            {
                throw new DownloadClientException("Failed to connect to Slskd, check your settings.", ex);
            }
            catch (WebException ex)
            {
                throw new DownloadClientException("Failed to connect to Slskd, please check your settings.", ex);
            }
        }

        public void Authenticate(SlskdSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/application")
                .Build();

            var response = _httpClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.Debug("The APIKEY is correct.");

                return;
            }

            throw new DownloadClientException("The APIKEY is wrong.");
        }

        public void Authenticate(SlskdIndexerSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/application")
                .Build();

            var response = _httpClient.Execute(request);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.Debug("The APIKEY is correct.");

                return;
            }

            throw new DownloadClientException("The APIKEY is wrong.");
        }
    }
}

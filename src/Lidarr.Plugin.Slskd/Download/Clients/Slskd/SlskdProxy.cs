using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Cache;
using NzbDrone.Common.Crypto;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.History;
using NzbDrone.Core.Indexers.Slskd;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Plugins.Slskd;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public interface ISlskdProxy
    {
        Task<SlskdOptions> GetOptionsAsync(SlskdSettings settings);
        JsonRequestBuilder BuildSearchRequest(SlskdIndexerSettings settings, SlskdSearchRequest requestData);
        Task<SlskdSearchEntry> SearchAsync(SlskdIndexerSettings settings, SlskdSearchRequest requestData);
        JsonRequestBuilder BuildSearchEntryRequest(SlskdIndexerSettings settings, string searchId);
        Task<SlskdSearchEntry> GetSearchEntryAsync(SlskdIndexerSettings settings, string searchId);
        Task<List<SlskdResponse>> GetSearchResponsesAsync(SlskdSettings settings, string searchId);
        Task<List<SlskdSearchEntry>> GetSearchesAsync(SlskdIndexerSettings settings);
        Task<SlskdSearchEntry> WaitSearchToComplete(SlskdIndexerSettings settings, string searchId);
        Task DeleteSearchAsync(SlskdIndexerSettings settings, string searchId);
        Task DeleteAllSearchesAsync(SlskdIndexerSettings settings);
        Task DeleteSearchAsync(SlskdSettings settings, string searchId);
        Task<List<SlskdDownloadEntry>> GetAllDownloadsAsync(SlskdSettings settings);
        Task<List<DownloadClientItem>> GetQueueAsync(SlskdSettings settings);
        Task<string> DownloadAsync(SlskdSettings settings, ReleaseInfo release);
        Task<bool> DownloadFilesAsync(SlskdSettings settings, string username, List<SlskdResponseFile> filesToDownload);
        Task<SlskdFile> GetDownloadAsync(SlskdSettings settings, string username, string downloadId);
        Task<SlskdFile> WaitDownloadToCompleteAsync(SlskdSettings settings, string username, string downloadId);
        Task CancelDownloadAsync(SlskdSettings settings, string username, string downloadId, bool remove = false);
        Task RemoveFromQueueAsync(SlskdSettings settings, DownloadClientItem downloadItem);
        Task AuthenticateAsync(SlskdSettings settings);
        Task AuthenticateAsync(SlskdIndexerSettings settings);
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

        public async Task<SlskdOptions> GetOptionsAsync(SlskdSettings settings)
        {
            var request = BuildRequest(settings).Resource("/api/v0/options");
            return await ProcessRequestAsync<SlskdOptions>(request).ConfigureAwait(false);
        }

        public JsonRequestBuilder BuildSearchRequest(SlskdIndexerSettings settings, SlskdSearchRequest requestData)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/searches")
                .Post();

            request.SetJsonData(requestData.ToJson());

            return request;
        }

        public async Task<SlskdSearchEntry> SearchAsync(SlskdIndexerSettings settings, SlskdSearchRequest requestData)
        {
            var request = BuildSearchRequest(settings, requestData);
            return await ProcessRequestAsync<SlskdSearchEntry>(request).ConfigureAwait(false);
        }

        public JsonRequestBuilder BuildSearchEntryRequest(SlskdIndexerSettings settings, string searchId)
        {
            return BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}")
                .AddQueryParam("includeResponses", true);
        }

        public async Task<SlskdSearchEntry> GetSearchEntryAsync(SlskdIndexerSettings settings, string searchId)
        {
            var request = BuildSearchEntryRequest(settings, searchId);
            return await ProcessRequestAsync<SlskdSearchEntry>(request).ConfigureAwait(false);
        }

        public async Task<List<SlskdResponse>> GetSearchResponsesAsync(SlskdSettings settings, string searchId)
        {
            var sEntry = BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}/responses");

            return await ProcessRequestAsync<List<SlskdResponse>>(sEntry).ConfigureAwait(false);
        }

        public async Task<List<SlskdSearchEntry>> GetSearchesAsync(SlskdIndexerSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/searches");
            return await ProcessRequestAsync<List<SlskdSearchEntry>>(request).ConfigureAwait(false);
        }

        public async Task<SlskdSearchEntry> WaitSearchToComplete(SlskdIndexerSettings settings, string searchId)
        {
            SlskdSearchEntry searchEntry;

            // Keep monitoring the search entry until it has finished
            do
            {
                // Retrieve the search entry with its responses
                searchEntry = await GetSearchEntryAsync(settings, searchId).ConfigureAwait(false);

                // Wait a bit before making another request
                await Task.Delay(1000).ConfigureAwait(false);
            }
            while (searchEntry.State < SlskdStates.CompletedSucceeded);

            return searchEntry;
        }

        public async Task DeleteSearchAsync(SlskdIndexerSettings settings, string searchId)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}");

            request.Method = HttpMethod.Delete;

            await ProcessRequestAsync(request).ConfigureAwait(false);
        }

        public async Task DeleteAllSearchesAsync(SlskdIndexerSettings settings)
        {
            var searches = await GetSearchesAsync(settings).ConfigureAwait(false);

            foreach (var sEntry in searches)
            {
                await DeleteSearchAsync(settings, sEntry.Id).ConfigureAwait(false);
            }
        }

        public async Task DeleteSearchAsync(SlskdSettings settings, string searchId)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/searches/{searchId}");

            request.Method = HttpMethod.Delete;

            await ProcessRequestAsync(request).ConfigureAwait(false);
        }

        public async Task<List<SlskdDownloadEntry>> GetAllDownloadsAsync(SlskdSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/transfers/downloads/");
            return await ProcessRequestAsync<List<SlskdDownloadEntry>>(request).ConfigureAwait(false);
        }

        public async Task<List<DownloadClientItem>> GetQueueAsync(SlskdSettings settings)
        {
            var options = await GetOptionsAsync(settings).ConfigureAwait(false);

            var allDownloads = await GetAllDownloadsAsync(settings).ConfigureAwait(false);

            var downloadDirectories = allDownloads
                .Where(d => d != null)
                .SelectMany(user => user.Directories)
                .GroupBy(d => SlskdUtils.GroupPath(d.Directory, true))
                .Select(g => ToDownloadClientItem(g, options))
                .Where(i => i != null)
                .ToList();

            return downloadDirectories;
        }

        private static DownloadItemStatus GetFileDownloadStatus(SlskdFile file)
        {
            if (file.State < SlskdStates.InProgress)
            {
                return DownloadItemStatus.Queued;
            }

            if (file.State == SlskdStates.InProgress)
            {
                return DownloadItemStatus.Downloading;
            }

            if (file.State == SlskdStates.CompletedSucceeded)
            {
                return DownloadItemStatus.Completed;
            }

            return DownloadItemStatus.Failed;
        }

        private DownloadClientItem ToDownloadClientItem(IGrouping<string, SlskdDirectory> downloadGroup, SlskdOptions options)
        {
            var commonDirectory = downloadGroup.Key;

            var files = downloadGroup
                .SelectMany(d => d.Files)
                .ToArray();

            var averageSpeed = files
                .Select(file => file.AverageSpeed)
                .Where(s => s > 0)
                .DefaultIfEmpty(0)
                .Average();

            var totalSize = files.Sum(file => file.Size);
            var bytesRemaining = files.Sum(file => file.BytesRemaining);

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
            var statuses = files
                .Select(GetFileDownloadStatus)
                .ToArray();

            DownloadItemStatus status;

            // TODO: Check canceled items
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
            var downloadId = Md5StringConverter.ComputeMd5(commonDirectory);

            // Retrieve the album data from the history service
            var releaseInfo = _historyService
                .FindByDownloadId(downloadId)
                .FirstOrDefault();

            if (releaseInfo is null)
            {
                return null;
            }

            // Slskd trims down everything like username and only outputs to the last path
            var downloadsPath = new OsPath(options.Directories.Downloads, OsPathKind.Unix);
            var releasePath = new OsPath(commonDirectory, OsPathKind.Unix);

            // Combine
            var outputPath = downloadsPath + releasePath;

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
                OutputPath = outputPath,
                Category = "music",
            };

            return item;
        }

        public async Task<bool> DownloadFilesAsync(SlskdSettings settings, string username, List<SlskdResponseFile> filesToDownload)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/transfers/downloads/{username}")
                .Post();

            request.SetJsonData(filesToDownload.ToJson());

            var result = await ProcessRequestAsync(request).ConfigureAwait(false);

            return result.StatusCode == HttpStatusCode.Created;
        }

        public async Task<SlskdFile> GetDownloadAsync(SlskdSettings settings, string username, string downloadId)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/transfers/downloads/{username}/{downloadId}");
            return await ProcessRequestAsync<SlskdFile>(request).ConfigureAwait(false);
        }

        public async Task<SlskdFile> WaitDownloadToCompleteAsync(SlskdSettings settings, string username, string downloadId)
        {
            SlskdFile download;

            do
            {
                download = await GetDownloadAsync(settings, username, downloadId).ConfigureAwait(false);

                await Task.Delay(500).ConfigureAwait(false);
            }
            while (download.State < SlskdStates.CompletedSucceeded);

            return download;
        }

        public async Task CancelDownloadAsync(SlskdSettings settings, string username, string downloadId, bool remove = false)
        {
            var request = BuildRequest(settings)
                .Resource($"/api/v0/transfers/downloads/{username}/{downloadId}")
                .AddQueryParam("remove", remove);
            request.Method = HttpMethod.Delete;
            await ProcessRequestAsync(request).ConfigureAwait(false);
        }

        public async Task RemoveFromQueueAsync(SlskdSettings settings, DownloadClientItem downloadItem)
        {
            var allDownloads = await GetAllDownloadsAsync(settings).ConfigureAwait(false);

            // TODO: We should only have a single match but this will do for now
            // TODO: I think we might have cases with multiple artists in a single directory?
            foreach (var entry in allDownloads)
            {
                var filesToRemove = entry.Directories
                    .Where(directory => Md5StringConverter.ComputeMd5(directory.Directory) == downloadItem.DownloadId)
                    .SelectMany(directory => directory.Files)
                    .ToArray();

                await Task.WhenAll(filesToRemove.Select(async file =>
                {
                    // If the download is still in progress then cancel it first
                    if (file.State < SlskdStates.CompletedSucceeded)
                    {
                        await CancelDownloadAsync(settings, entry.Username, file.Id).ConfigureAwait(false);

                        // Wait for its status to be 'Completed'
                        await WaitDownloadToCompleteAsync(settings, entry.Username, file.Id).ConfigureAwait(false);
                    }

                    // Finally delete it
                    await CancelDownloadAsync(settings, entry.Username, file.Id, true).ConfigureAwait(false);
                })).ConfigureAwait(false);
            }
        }

        public async Task<string> DownloadAsync(SlskdSettings settings, ReleaseInfo release)
        {
            // Parse the guid
            var matches = Regex.Match(release.Guid, "(Slskd)-(.+)");

            var slskd = matches.Groups[1].Value;

            if (slskd != "Slskd")
            {
                throw new DownloadClientException($"The provided guid doesn't appear to come from this plugin: '{release.Guid}'");
            }

            var releaseId = matches.Groups[2].Value;

            var searchId = release.InfoUrl;
            var username = release.DownloadUrl;

            // Retrieve the list of files to download
            var sResponses = await GetSearchResponsesAsync(settings, searchId).ConfigureAwait(false);

            // We can remove the search entry at this point
            await DeleteSearchAsync(settings, searchId).ConfigureAwait(false);

            // Find the Responses for that specific user
            var userResponse = sResponses.FirstOrDefault(r => r.Username == username);

            if (userResponse == null)
            {
                throw new DownloadClientException("User not found.");
            }

            // We only want the files that are in the requested directory
            var releaseFiles = userResponse.Files
                .GroupBy(f => SlskdUtils.GroupPath(f.Filename, false))
                .FirstOrDefault(g => Md5StringConverter.ComputeMd5(g.Key) == releaseId)?
                .ToList();

            if (releaseFiles.Empty())
            {
                throw new DownloadClientException("Unable to retrieve files per releaseId.");
            }

            // Download media files only
            var filesToDownload = SlskdUtils.GetValidMediaFiles(releaseFiles)
                .Select(f => f.ResponseFile)
                .ToList();

            if (filesToDownload.Empty())
            {
                throw new DownloadClientException("Unable to locate any valid file to download from the releaseId.");
            }

            // Send the download request
            var downloaded = await DownloadFilesAsync(settings, username, filesToDownload).ConfigureAwait(false);

            if (!downloaded)
            {
                throw new DownloadClientException("Error adding item to Slskd.");
            }

            _logger.Trace("Downloading item {0}", releaseId);

            return releaseId;
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

        private async Task<TResult> ProcessRequestAsync<TResult>(JsonRequestBuilder requestBuilder)
            where TResult : new()
        {
            var response = await ProcessRequestAsync(requestBuilder).ConfigureAwait(false);

            return Json.Deserialize<TResult>(response.Content);
        }

        private async Task<HttpResponse> ProcessRequestAsync(JsonRequestBuilder requestBuilder)
        {
            var request = requestBuilder.Build();

            requestBuilder.LogResponseContent = true;
            requestBuilder.SuppressHttpErrorStatusCodes = new[] { HttpStatusCode.Forbidden };

            try
            {
                return await _httpClient.ExecuteAsync(request).ConfigureAwait(false);
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

        public async Task AuthenticateAsync(SlskdSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/application")
                .Build();

            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.Debug("The APIKEY is correct.");

                return;
            }

            throw new DownloadClientException("The APIKEY is wrong.");
        }

        public async Task AuthenticateAsync(SlskdIndexerSettings settings)
        {
            var request = BuildRequest(settings)
                .Resource("/api/v0/application")
                .Build();

            var response = await _httpClient.ExecuteAsync(request).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                _logger.Debug("The APIKEY is correct.");

                return;
            }

            throw new DownloadClientException("The APIKEY is wrong.");
        }
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Web;
using NLog;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdRequestGenerator : IIndexerRequestGenerator
    {
        private const int PageSize = 250;
        private const int MaxPages = 30;

        public ISlskdProxy Proxy { get; set; }
        public SlskdIndexerSettings Settings { get; set; }
        public Logger Logger { get; set; }

        public virtual IndexerPageableRequestChain GetRecentRequests()
        {
            var chain = new IndexerPageableRequestChain();

            chain.Add(GetRequests("Brakence", "Bhavana"));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.Artist.Name, searchCriteria.AlbumTitle));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            // TODO: We will implement this later on
            return new IndexerPageableRequestChain();
        }

        private IEnumerable<IndexerRequest> GetRequests(string artistName, string albumTitle = "")
        {
            var searchQuery = $"{artistName} {albumTitle}".Trim();

            if (string.IsNullOrEmpty(searchQuery))
            {
                throw new Exception("The search query is empty.");
            }

            // TODO: Make it all configurable from the settings
            // TODO: Implement filters
            var searchData = new SlskdSearchRequest
            {
                Id = Guid.NewGuid().ToString(),
                FileLimit = 10000,
                FilterResponses = true,
                MaximumPeerQueueLength = 1000000,
                MinimumPeerUploadSpeed = 0,
                MinimumResponseFileCount = 1,
                ResponseLimit = PageSize,
                SearchText = searchQuery,
                SearchTimeout = 15000
            };

            // Search should be completed by now so build the final request
            // Also append the Artist and Album as headers so we can re-use them during parsing
            // instead of having to sanitize the results (just a lil test)
            var request = Proxy
                .BuildSearchRequest(Settings, searchData)
                .SetHeader("SLSKD-ARTIST", HttpUtility.UrlEncode(artistName, Encoding.UTF8))
                .SetHeader("SLSKD-ALBUM", HttpUtility.UrlEncode(albumTitle, Encoding.UTF8))
                .Build();

            yield return new IndexerRequest(request);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class SlskdRequestGenerator : IIndexerRequestGenerator
    {
        private const int PageSize = 100;
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

            // TODO: For some is better to just use the album as search query (ex: old ye albums on which he was still being called Kanye)
            chain.AddTier(GetRequests(searchCriteria.ArtistQuery, searchCriteria.AlbumQuery));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            chain.AddTier(GetRequests(searchCriteria.ArtistQuery));

            return chain;
        }

        private IEnumerable<IndexerRequest> GetRequests(string artistQuery, string albumQuery = "")
        {
            var searchQuery = $"{artistQuery} {albumQuery}".Trim();

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
                ResponseLimit = 100,
                SearchText = searchQuery,
                SearchTimeout = 15000,
            };

            var searchResponse = Proxy.Search(Settings, searchData);

            // Check the status and monitor it until it is completed
            var state = searchResponse.State;

            while (state < SlskdFileStatus.CompletedSucceeded)
            {
                // Wait a bit before making another request
                // TODO: Is this a good idea? Should we do this later on?
                Task.Delay(1000).Wait();

                var response = Proxy.GetSearchEntry(Settings, searchResponse.Id);

                state = response.State;
            }

            // Search should be completed by now so build the final request
            // Also append the Artist and Album as headers so we can re-use them during parsing
            // instead of having to sanitize the results (just a lil test)
            var request = Proxy
                .BuildSearchEntryRequest(Settings, searchResponse.Id)
                .SetHeader("SLSKD-ARTIST", artistQuery)
                .SetHeader("SLSKD-ALBUM", albumQuery)
                .Build();

            yield return new IndexerRequest(request);
        }
    }
}
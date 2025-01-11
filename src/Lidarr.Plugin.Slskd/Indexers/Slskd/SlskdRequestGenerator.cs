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

            // Lidarr provides us all the artist metadata
            var artistMetadata = searchCriteria.Artist.Metadata.Value;

            // Chain the first request with 'base' queries
            chain.AddTier(GetRequests(searchCriteria.ArtistQuery, searchCriteria.AlbumQuery));

            // Some artists may have more aliases so chain a request for each of them
            var aliases = artistMetadata.Aliases;

            // Remove the previous alias if present
            aliases.Remove(searchCriteria.ArtistQuery);

            foreach (var alias in aliases)
            {
                // TODO: Should we always return the current artist's alias? Lidarr seems to detect it from what I've tested so far
                chain.AddTier(GetRequests(alias, searchCriteria.AlbumQuery));
            }

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
                ResponseLimit = PageSize,
                SearchText = searchQuery,
                SearchTimeout = 15000,
            };

            // Search should be completed by now so build the final request
            // Also append the Artist and Album as headers so we can re-use them during parsing
            // instead of having to sanitize the results (just a lil test)
            var request = Proxy
                .BuildSearchRequest(Settings, searchData)
                .SetHeader("SLSKD-ARTIST", HttpUtility.UrlEncode(artistQuery, Encoding.UTF8))
                .SetHeader("SLSKD-ALBUM", HttpUtility.UrlEncode(albumQuery, Encoding.UTF8))
                .Build();

            yield return new IndexerRequest(request);
        }
    }
}

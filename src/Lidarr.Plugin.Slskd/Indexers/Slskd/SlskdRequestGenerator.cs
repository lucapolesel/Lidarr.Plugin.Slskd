using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Web;
using NLog;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.IndexerSearch.Definitions;

namespace NzbDrone.Core.Indexers.Slskd
{
    public enum SearchType
    {
        ArtistOnly,
        AlbumOnly,
        AliasOnly,
        ArtistAndAlbum,
        AliasAndAlbum,
    }

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

            chain.Add(GetRequests(SearchType.ArtistAndAlbum, "Brakence", "Bhavana"));

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(AlbumSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            var artistName = searchCriteria.Artist.Name;

            // Lidarr only appends one album to the list
            var albumName = searchCriteria.Albums.First().Title;

            // Lidarr provides us all the artist metadata
            var artistMetadata = searchCriteria.Artist.Metadata.Value;

            // Chain the first request with 'base' queries
            chain.AddTier(GetRequests(SearchType.ArtistAndAlbum, artistName, albumName));

            // Chain a request with just the album title
            chain.AddTier(GetRequests(SearchType.AlbumOnly, artistName, albumName));

            // Chain just the artist name and aliases, we will do the matching later on
            chain.AddTier(GetRequests(SearchType.ArtistOnly, artistName, albumName));

            foreach (var alias in artistMetadata.Aliases)
            {
                chain.AddTier(GetRequests(SearchType.AliasOnly, artistName, albumName, alias));
            }

            // TODO: I'm currently testing by using them at the end since we implemented the path matching
            // Some artists may have more aliases so chain a request for each of them as last resort
            foreach (var alias in artistMetadata.Aliases)
            {
                chain.AddTier(GetRequests(SearchType.AliasAndAlbum, artistName, albumName, alias));
            }

            return chain;
        }

        public IndexerPageableRequestChain GetSearchRequests(ArtistSearchCriteria searchCriteria)
        {
            var chain = new IndexerPageableRequestChain();

            // TODO: Check what Lidarr wants in case of full artist searches (we have the metadata and we could scrape everything ourselves)
            // chain.AddTier(GetRequests(searchCriteria.ArtistQuery));
            return chain;
        }

        // TODO: This is ugly af so change it later on
        private IEnumerable<IndexerRequest> GetRequests(SearchType searchType, string artistName, string albumTitle = "", string artistAlias = "")
        {
            var searchQuery = searchType switch
            {
                SearchType.ArtistOnly => artistName,
                SearchType.AlbumOnly => albumTitle,
                SearchType.AliasOnly => artistAlias,
                SearchType.ArtistAndAlbum => $"{artistName} {albumTitle}",
                SearchType.AliasAndAlbum => $"{artistAlias} {albumTitle}",
                _ => ""
            };

            searchQuery = searchQuery.Trim();

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

using System;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Download.Clients.Slskd;
using NzbDrone.Core.Music;
using NzbDrone.Core.Parser;

namespace NzbDrone.Core.Indexers.Slskd
{
    public class Slskd : HttpIndexerBase<SlskdIndexerSettings>
    {
        public override string Name => "Slskd";
        public override string Protocol => nameof(SlskdDownloadProtocol);
        public override bool SupportsRss => false;
        public override bool SupportsSearch => true;
        public override int PageSize => 250;
        public override TimeSpan RateLimit => new TimeSpan(0);

        private readonly ISlskdProxy _slskdProxy;
        private readonly IArtistService _artistService;
        private readonly IAlbumService _albumService;
        private readonly ITrackService _trackService;

        public Slskd(ISlskdProxy slskdProxy,
            IHttpClient httpClient,
            IIndexerStatusService indexerStatusService,
            IConfigService configService,
            IParsingService parsingService,
            IArtistService artistService,
            IAlbumService albumService,
            ITrackService trackService,
            Logger logger)
            : base(httpClient, indexerStatusService, configService, parsingService, logger)
        {
            _slskdProxy = slskdProxy;
            _artistService = artistService;
            _albumService = albumService;
            _trackService = trackService;
        }

        public override IIndexerRequestGenerator GetRequestGenerator()
        {
            return new SlskdRequestGenerator
            {
                Proxy = _slskdProxy,
                Settings = Settings,
                Logger = _logger
            };
        }

        public override IParseIndexerResponse GetParser()
        {
            _slskdProxy.AuthenticateAsync(Settings)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            return new SlskdParser
            {
                Proxy = _slskdProxy,
                Settings = Settings,
                Logger = _logger,
                ArtistService = _artistService,
                AlbumService = _albumService,
                TrackService = _trackService,
            };
        }
    }
}

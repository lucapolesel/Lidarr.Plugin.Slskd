using System.Collections.Generic;
using System.Threading.Tasks;
using FluentValidation.Results;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RemotePathMappings;
using NzbDrone.Core.Validation;

namespace NzbDrone.Core.Download.Clients.Slskd
{
    public class Slskd : DownloadClientBase<SlskdSettings>
    {
        private readonly ISlskdProxy _proxy;

        public Slskd(ISlskdProxy proxy,
                      IConfigService configService,
                      IDiskProvider diskProvider,
                      IRemotePathMappingService remotePathMappingService,
                      Logger logger)
            : base(configService, diskProvider, remotePathMappingService, logger)
        {
            _proxy = proxy;
        }

        public override string Protocol => nameof(SlskdDownloadProtocol);

        public override string Name => "Slskd";

        public override IEnumerable<DownloadClientItem> GetItems()
        {
            var queue = _proxy.GetQueueAsync(Settings)
                .ConfigureAwait(false).GetAwaiter().GetResult();

            foreach (var item in queue)
            {
                item.DownloadClientInfo = DownloadClientItemClientInfo.FromDownloadClient(this, false);
                item.OutputPath = _remotePathMappingService.RemapRemoteToLocal(Settings.Host, item.OutputPath);
            }

            return queue;
        }

        public override void RemoveItem(DownloadClientItem item, bool deleteData)
        {
            if (deleteData)
            {
                DeleteItemData(item);
            }

            _ = _proxy.RemoveFromQueueAsync(Settings, item);
        }

        public override async Task<string> Download(RemoteAlbum remoteAlbum, IIndexer indexer)
        {
            var release = remoteAlbum.Release;

            return await _proxy.DownloadAsync(Settings, release).ConfigureAwait(false);
        }

        public override DownloadClientInfo GetStatus()
        {
            var config = _proxy.GetOptionsAsync(Settings).GetAwaiter().GetResult();

            return new DownloadClientInfo
            {
                IsLocalhost = Settings.Host is "127.0.0.1" or "localhost",
                OutputRootFolders = new List<OsPath> { _remotePathMappingService.RemapRemoteToLocal(Settings.Host, new OsPath(config.Directories.Downloads)) }
            };
        }

        protected override void Test(List<ValidationFailure> failures)
        {
            failures.AddIfNotNull(TestSettings());
        }

        private ValidationFailure TestSettings()
        {
            try
            {
                _proxy.AuthenticateAsync(Settings).Wait();
            }
            catch (DownloadClientException)
            {
                return new NzbDroneValidationFailure(string.Empty, "Could not authenticate to Slskd. Invalid APIKEY?")
                {
                    InfoLink = HttpRequestBuilder.BuildBaseUrl(Settings.UseSsl, Settings.Host, Settings.Port, Settings.UrlBase),
                    DetailedDescription = "Slskd requires a valid APIKEY to handle any API request",
                };
            }

            return null;
        }
    }
}

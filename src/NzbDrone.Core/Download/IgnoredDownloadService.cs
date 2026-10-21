using System.Linq;
using NLog;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;

namespace NzbDrone.Core.Download
{
    public interface IIgnoredDownloadService
    {
        bool IgnoreDownload(TrackedDownload trackedDownload);
    }

    public class IgnoredDownloadService : IIgnoredDownloadService
    {
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;

        public IgnoredDownloadService(IEventAggregator eventAggregator,
                                      Logger logger)
        {
            _eventAggregator = eventAggregator;
            _logger = logger;
        }

        public bool IgnoreDownload(TrackedDownload trackedDownload)
        {
            // A download that was never matched to an author and book has no RemoteBook at all.
            var remoteBook = trackedDownload.RemoteBook;
            var author = remoteBook?.Author;
            var books = remoteBook?.Books;

            if (author == null || books == null || books.Empty())
            {
                _logger.Warn("Unable to ignore download for unknown author/book");
                return false;
            }

            var downloadIgnoredEvent = new DownloadIgnoredEvent
            {
                AuthorId = author.Id,
                BookIds = books.Select(e => e.Id).ToList(),
                Quality = remoteBook.ParsedBookInfo?.Quality,
                SourceTitle = trackedDownload.DownloadItem.Title,
                DownloadClientInfo = trackedDownload.DownloadItem.DownloadClientInfo,
                DownloadId = trackedDownload.DownloadItem.DownloadId,
                TrackedDownload = trackedDownload,
                Message = "Manually ignored"
            };

            _eventAggregator.PublishEvent(downloadIgnoredEvent);
            return true;
        }
    }
}

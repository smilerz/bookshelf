using System.Collections.Generic;
using System.Linq;
using NLog;
using NzbDrone.Core.Books;
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
            // A download that was never matched to an author and book has no RemoteBook at all. It can still be ignored: the ignore is
            // recorded against the download id, so the download stops being tracked and is not offered for import again.
            var remoteBook = trackedDownload.RemoteBook;
            var author = remoteBook?.Author;
            var books = remoteBook?.Books ?? new List<Book>();

            if (author == null || !books.Any())
            {
                _logger.Debug("Ignoring a download that was never matched to a book: {0}", trackedDownload.DownloadItem.Title);
            }

            var downloadIgnoredEvent = new DownloadIgnoredEvent
            {
                AuthorId = author?.Id ?? 0,
                BookIds = books.Select(e => e.Id).ToList(),
                Quality = remoteBook?.ParsedBookInfo?.Quality,
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

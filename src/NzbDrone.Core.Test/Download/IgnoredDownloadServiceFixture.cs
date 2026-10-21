using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.Download
{
    [TestFixture]
    public class IgnoredDownloadServiceFixture : CoreTest<IgnoredDownloadService>
    {
        private static DownloadClientItem Item()
        {
            return new DownloadClientItem { Title = "Some Book", DownloadId = "abc" };
        }

        [Test]
        public void should_ignore_a_download_that_was_never_matched_to_a_book()
        {
            var trackedDownload = new TrackedDownload { DownloadItem = Item(), RemoteBook = null };

            Subject.IgnoreDownload(trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e => e.AuthorId == 0 && e.BookIds.Count == 0 && e.DownloadId == "abc")), Times.Once());
        }

        [Test]
        public void should_ignore_a_download_matched_to_an_author_but_no_books()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = Item(),
                RemoteBook = new RemoteBook { Author = new Author { Id = 1 }, Books = new List<Book>(), ParsedBookInfo = new ParsedBookInfo() }
            };

            Subject.IgnoreDownload(trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e => e.AuthorId == 1 && e.BookIds.Count == 0)), Times.Once());
        }

        [Test]
        public void should_publish_the_ignore_event_for_a_matched_download()
        {
            var trackedDownload = new TrackedDownload
            {
                DownloadItem = Item(),
                RemoteBook = new RemoteBook
                {
                    Author = new Author { Id = 1 },
                    Books = new List<Book> { new Book { Id = 2 } },
                    ParsedBookInfo = new ParsedBookInfo()
                }
            };

            Subject.IgnoreDownload(trackedDownload).Should().BeTrue();

            Mocker.GetMock<IEventAggregator>()
                .Verify(v => v.PublishEvent(It.Is<DownloadIgnoredEvent>(e => e.AuthorId == 1 && e.BookIds.Contains(2))), Times.Once());
        }
    }
}

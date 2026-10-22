using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.MediaFiles.BookImport.Manual;

namespace NzbDrone.Core.Test.MediaFiles.BookImport
{
    [TestFixture]
    public class TrackedDownloadPathMatcherFixture
    {
        private static TrackedDownload Download(string id, string outputPath)
        {
            return new TrackedDownload
            {
                DownloadItem = new DownloadClientItem { DownloadId = id, OutputPath = new OsPath(outputPath) }
            };
        }

        [Test]
        public void should_find_a_single_file_download_by_the_path_of_the_file()
        {
            var downloads = new List<TrackedDownload> { Download("A", "/storage/downloads/Books/Savage Season.epub") };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/downloads/Books/Savage Season.epub").DownloadItem.DownloadId.Should().Be("A");
        }

        [Test]
        public void should_find_a_download_by_a_file_inside_its_folder()
        {
            var downloads = new List<TrackedDownload> { Download("A", "/storage/downloads/Books/Some Release") };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/downloads/Books/Some Release/Ebook/book.epub").DownloadItem.DownloadId.Should().Be("A");
        }

        [Test]
        public void should_not_match_a_path_outside_every_download()
        {
            var downloads = new List<TrackedDownload> { Download("A", "/storage/downloads/Books/Some Release") };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/books/Joe R. Lansdale/Savage Season/Savage Season.epub").Should().BeNull();
        }

        [Test]
        public void should_not_match_a_folder_that_only_shares_a_name_prefix()
        {
            var downloads = new List<TrackedDownload> { Download("A", "/storage/downloads/Books/Some Release") };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/downloads/Books/Some Release Two/book.epub").Should().BeNull();
        }

        [Test]
        public void should_prefer_the_most_specific_download_when_paths_are_nested()
        {
            var downloads = new List<TrackedDownload>
            {
                Download("OUTER", "/storage/downloads/Books"),
                Download("INNER", "/storage/downloads/Books/Some Release")
            };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/downloads/Books/Some Release/book.epub").DownloadItem.DownloadId.Should().Be("INNER");
        }

        [Test]
        public void should_ignore_downloads_without_an_output_path()
        {
            var downloads = new List<TrackedDownload> { Download("A", string.Empty), new TrackedDownload() };

            TrackedDownloadPathMatcher.Find(downloads, "/storage/downloads/Books/book.epub").Should().BeNull();
        }

        [Test]
        public void should_return_nothing_for_an_empty_path()
        {
            var downloads = new List<TrackedDownload> { Download("A", "/storage/downloads/Books/Some Release") };

            TrackedDownloadPathMatcher.Find(downloads, string.Empty).Should().BeNull();
            TrackedDownloadPathMatcher.Find(downloads, null).Should().BeNull();
        }
    }
}

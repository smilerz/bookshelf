using System.Collections.Generic;
using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.RootFolders;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private BookFile _trackFile;
        private LocalBook _localTrack;
        private string _rootPath = @"C:\Test\Music\Author".AsOsAgnostic();

        [SetUp]
        public void Setup()
        {
            _localTrack = new LocalBook();
            _localTrack.Author = new Author
            {
                Path = _rootPath
            };

            _trackFile = Builder<BookFile>
                .CreateNew()
                .Build();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FolderExists(It.IsAny<string>()))
                .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                 .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                 .Returns<string>(c => Path.GetDirectoryName(c));

            Mocker.GetMock<IRootFolderService>()
                .Setup(c => c.GetBestRootFolder(It.IsAny<string>()))
                .Returns(new RootFolder());
        }

        private void GivenSingleTrackWithSingleTrackFile()
        {
            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>(
                          new List<BookFile>
                          {
                              new BookFile
                              {
                                  Id = 1,
                                  Path = Path.Combine(_rootPath, @"Season 01\30.rock.s01e01.avi"),
                              }
                          }))
                .Build();
        }

        [Test]
        public void should_only_recycle_the_old_file_after_the_new_file_is_in_place()
        {
            GivenSingleTrackWithSingleTrackFile();
            var steps = new List<string>();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s, d, o) => steps.Add("aside"));

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Callback<BookFile, LocalBook>((b, l) => steps.Add("place"))
                .Returns(new BookFile());

            Mocker.GetMock<IRecycleBinProvider>()
                .Setup(c => c.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                .Callback<string, string>((p, s) => steps.Add("recycle"));

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade))
                .Callback<BookFile, DeleteMediaFileReason>((f, r) => steps.Add("database"));

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            steps.Should().Equal("aside", "place", "recycle", "database");
        }

        [Test]
        public void should_put_the_old_file_back_and_delete_nothing_if_the_new_file_cannot_be_placed()
        {
            GivenSingleTrackWithSingleTrackFile();
            var oldPath = _localTrack.Book.BookFiles.Value.Single().Path;
            var onDisk = new HashSet<string> { oldPath };

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns<string>(p => onDisk.Contains(p));

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.MoveFile(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>()))
                .Callback<string, string, bool>((s, d, o) =>
                {
                    onDisk.Remove(s);
                    onDisk.Add(d);
                });

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Throws(new IOException("disk full"));

            Assert.Throws<IOException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            onDisk.Should().BeEquivalentTo(new[] { oldPath });

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_still_succeed_when_the_old_file_cannot_be_recycled_after_the_new_file_is_in_place()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IRecycleBinProvider>()
                .Setup(c => c.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new RecycleBinException("recycle bin is full"));

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            result.OldFiles.Should().HaveCount(1);
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_report_a_missing_root_folder_when_no_root_folder_contains_the_author()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IRootFolderService>()
                .Setup(c => c.GetBestRootFolder(It.IsAny<string>()))
                .Returns((RootFolder)null);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_import_a_book_whose_files_were_never_loaded()
        {
            _localTrack.Book = Builder<Book>.CreateNew().With(e => e.BookFiles = null).Build();

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns(new BookFile());

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            result.OldFiles.Should().BeEmpty();
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.GetFilesByBook(_localTrack.Book.Id), Times.Once());
        }

        [Test]
        public void should_replace_the_files_found_in_the_database_when_the_book_has_none_loaded()
        {
            _localTrack.Book = Builder<Book>.CreateNew().With(e => e.BookFiles = null).Build();
            var oldFile = new BookFile { Id = 7, Path = Path.Combine(_rootPath, "old.epub") };

            Mocker.GetMock<IMediaFileService>()
                .Setup(c => c.GetFilesByBook(_localTrack.Book.Id))
                .Returns(new List<BookFile> { oldFile });

            Mocker.GetMock<IMoveBookFiles>()
                .Setup(c => c.MoveBookFile(It.IsAny<BookFile>(), It.IsAny<LocalBook>()))
                .Returns(new BookFile());

            var result = Subject.UpgradeBookFile(_trackFile, _localTrack);

            result.OldFiles.Should().ContainSingle().Which.Id.Should().Be(7);
            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(oldFile, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_report_a_missing_root_folder_when_the_author_has_no_path()
        {
            GivenSingleTrackWithSingleTrackFile();
            _localTrack.Author = new Author { Path = null };

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeBookFile(_trackFile, _localTrack));

            Mocker.GetMock<IMediaFileService>()
                .Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_delete_single_track_file_once()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_track_file_from_database()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<BookFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleTrackWithSingleTrackFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public void should_return_old_track_file_in_oldFiles()
        {
            GivenSingleTrackWithSingleTrackFile();

            Subject.UpgradeBookFile(_trackFile, _localTrack).OldFiles.Count.Should().Be(1);
        }

        [Test]
        [Ignore("Pending readarr fix")]
        public void should_import_if_existing_file_doesnt_exist_in_db()
        {
            _localTrack.Book = Builder<Book>.CreateNew()
                .With(e => e.BookFiles = new LazyLoaded<List<BookFile>>())
                .Build();

            Subject.UpgradeBookFile(_trackFile, _localTrack);

            // Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localTrack.Book.BookFiles.Value, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }
    }
}

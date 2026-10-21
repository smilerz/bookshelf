using System;
using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.History;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.BookImport.Specifications;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Specifications
{
    [TestFixture]
    public class AlreadyImportedSpecificationFixture : CoreTest<AlreadyImportedSpecification>
    {
        private const string SourcePath = "/storage/downloads/Books/Savage Season.epub";

        private static LocalEdition Incoming(long size = 1000, bool existingFile = false, List<BookFile> libraryFiles = null)
        {
            var edition = new Edition { BookId = 7, BookFiles = new LazyLoaded<List<BookFile>>(libraryFiles ?? new List<BookFile>()) };

            return new LocalEdition(new List<LocalBook> { new LocalBook { Path = SourcePath, Size = size, ExistingFile = existingFile } })
            {
                Edition = edition
            };
        }

        private void GivenImportedFrom(string droppedPath)
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.GetByBook(7, EntityHistoryEventType.BookFileImported))
                .Returns(new List<EntityHistory>
                {
                    new EntityHistory
                    {
                        EventType = EntityHistoryEventType.BookFileImported,
                        Date = new DateTime(2026, 10, 19),
                        Data = new Dictionary<string, string> { { "DroppedPath", droppedPath } }
                    }
                });
        }

        [Test]
        public void should_reject_a_file_that_was_already_imported_from_the_same_path()
        {
            GivenImportedFrom(SourcePath);

            Subject.IsSatisfiedBy(Incoming(), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_reject_a_file_with_the_same_size_as_one_already_in_the_library()
        {
            GivenImportedFrom("/storage/downloads/Books/Some other release.epub");
            var library = new List<BookFile> { new BookFile { Path = "/storage/books/Savage Season.epub", Size = 1000 } };

            Subject.IsSatisfiedBy(Incoming(1000, false, library), null).Accepted.Should().BeFalse();
        }

        [Test]
        public void should_accept_a_new_file_for_a_book_that_has_a_different_library_file()
        {
            GivenImportedFrom("/storage/downloads/Books/Some other release.epub");
            var library = new List<BookFile> { new BookFile { Path = "/storage/books/Savage Season.epub", Size = 555 } };

            Subject.IsSatisfiedBy(Incoming(1000, false, library), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_accept_a_book_that_was_never_imported()
        {
            Mocker.GetMock<IHistoryService>()
                .Setup(s => s.GetByBook(7, EntityHistoryEventType.BookFileImported))
                .Returns(new List<EntityHistory>());

            Subject.IsSatisfiedBy(Incoming(), null).Accepted.Should().BeTrue();
        }

        [Test]
        public void should_not_reject_a_library_file_that_is_being_relinked()
        {
            GivenImportedFrom(SourcePath);

            Subject.IsSatisfiedBy(Incoming(1000, true), null).Accepted.Should().BeTrue();
        }
    }
}

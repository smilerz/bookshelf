using System;
using System.Collections.Generic;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Books;
using NzbDrone.Core.Exceptions;
using NzbDrone.Core.MetadataSource;
using NzbDrone.Core.Organizer;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MusicTests
{
    [TestFixture]
    public class AddBookFixture : CoreTest<AddBookService>
    {
        private Author _fakeAuthor;
        private Book _fakeBook;

        [SetUp]
        public void Setup()
        {
            _fakeAuthor = Builder<Author>
                .CreateNew()
                .With(s => s.Path = null)
                .With(s => s.Metadata = Builder<AuthorMetadata>.CreateNew().Build())
                .Build();
        }

        private void GivenValidBook(string bookId, string editionId)
        {
            _fakeBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = Builder<Edition>
                      .CreateListOfSize(1)
                      .TheFirst(1)
                      .With(e => e.ForeignEditionId = editionId)
                      .With(e => e.Monitored = true)
                      .BuildList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      _fakeBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        private void GivenValidPath()
        {
            Mocker.GetMock<IBuildFileNames>()
                  .Setup(s => s.GetAuthorFolder(It.IsAny<Author>(), null))
                  .Returns<Author, NamingConfig>((c, n) => c.Name);
        }

        private Book BookToAdd(string editionId, string bookId, string authorId)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Editions = new List<Edition>
                {
                    new Edition
                    {
                        ForeignEditionId = editionId,
                        Monitored = true
                    }
                },
                AuthorMetadata = new AuthorMetadata
                {
                    ForeignAuthorId = authorId
                }
            };
        }

        private void GivenMetadataEditions(string bookId, params Edition[] editions)
        {
            var metadataBook = Builder<Book>
                .CreateNew()
                .With(x => x.Editions = editions.ToList())
                .Build();

            Mocker.GetMock<IProvideBookInfo>()
                .Setup(s => s.GetBookInfo(bookId))
                .Returns(Tuple.Create(_fakeAuthor.Metadata.Value.ForeignAuthorId,
                                      metadataBook,
                                      new List<AuthorMetadata> { _fakeAuthor.Metadata.Value }));

            Mocker.GetMock<IAddAuthorService>()
                .Setup(s => s.AddAuthor(It.IsAny<Author>(), It.IsAny<bool>()))
                .Returns(_fakeAuthor);
        }

        // A book as the import list sync builds it: it may name an edition, or none at all.
        private static Book ListBook(string bookId, params Edition[] editions)
        {
            return new Book
            {
                ForeignBookId = bookId,
                Monitored = true,
                AnyEditionOk = true,
                Editions = editions.ToList(),
                AddOptions = new AddBookOptions()
            };
        }

        private static Edition Ed(string id, bool monitored)
        {
            return new Edition { ForeignEditionId = id, Monitored = monitored };
        }

        [Test]
        public void should_add_a_list_book_that_names_no_edition()
        {
            GivenMetadataEditions("book", Ed("e1", false), Ed("e2", true));

            var book = Subject.AddBook(ListBook("book"));

            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("e2");
            Mocker.GetMock<IBookService>().Verify(s => s.AddBook(It.IsAny<Book>(), true), Times.Once());
        }

        [Test]
        public void should_keep_the_edition_the_caller_asked_for_when_the_metadata_has_it()
        {
            GivenMetadataEditions("book", Ed("e1", false), Ed("e2", true));

            var book = Subject.AddBook(ListBook("book", Ed("e1", true)));

            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("e1");
        }

        [Test]
        public void should_use_the_default_edition_when_the_requested_one_is_unknown()
        {
            GivenMetadataEditions("book", Ed("e1", false), Ed("e2", true));

            var book = Subject.AddBook(ListBook("book", Ed("not-in-metadata", true)));

            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("e2");
        }

        [Test]
        public void should_monitor_the_first_edition_when_the_metadata_marks_none()
        {
            GivenMetadataEditions("book", Ed("e1", false), Ed("e2", false));

            var book = Subject.AddBook(ListBook("book"));

            book.Editions.Value.Single(x => x.Monitored).ForeignEditionId.Should().Be("e1");
        }

        [Test]
        public void should_throw_a_validation_error_when_the_metadata_has_no_editions()
        {
            GivenMetadataEditions("book");

            Assert.Throws<ValidationException>(() => Subject.AddBook(ListBook("book")));

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void should_keep_going_after_a_book_that_cannot_be_added()
        {
            GivenMetadataEditions("bad");
            GivenMetadataEditions("good", Ed("e1", true));

            var added = Subject.AddBooks(new List<Book> { ListBook("bad"), ListBook("good") });

            added.Should().HaveCount(1);
            Mocker.GetMock<IBookService>().Verify(s => s.AddBook(It.IsAny<Book>(), true), Times.Once());
            ExceptionVerification.ExpectedErrors(2);
        }

        [Test]
        public void should_be_able_to_add_a_book_without_passing_in_name()
        {
            var newBook = BookToAdd("edition", "book", "author");

            GivenValidBook("book", "edition");
            GivenValidPath();

            var book = Subject.AddBook(newBook);

            book.Title.Should().Be(_fakeBook.Title);
        }

        [Test]
        public void should_throw_if_book_cannot_be_found()
        {
            var newBook = BookToAdd("edition", "book", "author");

            Mocker.GetMock<IProvideBookInfo>()
                  .Setup(s => s.GetBookInfo("book"))
                  .Throws(new BookNotFoundException("edition"));

            Assert.Throws<ValidationException>(() => Subject.AddBook(newBook));

            ExceptionVerification.ExpectedErrors(1);
        }
    }
}

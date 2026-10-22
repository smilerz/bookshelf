using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.MediaFiles.BookImport.Identification;

namespace NzbDrone.Core.Test.MediaFiles.BookImport.Identification
{
    [TestFixture]
    public class BookMatchSelectorFixture
    {
        // The two Artificial Condition editions and the All Systems Red audiobook edition scored for an Artificial Condition epub,
        // with the penalties that were logged on 2026-10-21 (title 0.0128 vs 0.184; the file's ISBN is another edition's).
        private static Distance ArtificialConditionEdition()
        {
            var d = new Distance();
            d.Add("author", 0.0);
            d.Add("book", 0.01282051282051283);
            d.AddBool("isbn", true);
            d.AddBool("asin_missing", true);
            d.AddBool("language", false);
            d.Add("publisher", 0.8);
            d.AddBool("ebook_format", true);
            return d;
        }

        private static Distance AllSystemsRedAudiobookEdition()
        {
            var d = new Distance();
            d.Add("author", 0.0);
            d.Add("book", 0.1842105263157895);
            d.AddBool("isbn_missing", true);
            d.AddBool("asin_missing", true);
            d.AddBool("language", false);
            d.Add("publisher", 0.9);
            d.AddBool("ebook_format", true);
            return d;
        }

        [Test]
        public void the_logged_numbers_reproduce_the_wrong_winner_on_total_distance()
        {
            ArtificialConditionEdition().NormalizedDistance().Should().BeGreaterThan(AllSystemsRedAudiobookEdition().NormalizedDistance());
        }

        [Test]
        public void should_choose_the_book_with_the_best_title_even_when_the_isbn_belongs_to_another_edition()
        {
            var candidates = new List<Distance> { ArtificialConditionEdition(), AllSystemsRedAudiobookEdition() };

            BookMatchSelector.ChooseBest(candidates, 1.0).Should().Be(0);
        }

        [Test]
        public void should_choose_the_book_with_the_best_title_whatever_the_candidate_order()
        {
            var candidates = new List<Distance> { AllSystemsRedAudiobookEdition(), ArtificialConditionEdition() };

            BookMatchSelector.ChooseBest(candidates, 1.0).Should().Be(1);
        }

        [Test]
        public void should_still_consider_a_candidate_whose_isbn_matches_exactly_even_with_a_different_title()
        {
            var otherTitleExactIsbn = new Distance();
            otherTitleExactIsbn.Add("author", 0.0);
            otherTitleExactIsbn.Add("book", 0.5);
            otherTitleExactIsbn.AddBool("isbn", false);

            var candidates = new List<Distance> { ArtificialConditionEdition(), otherTitleExactIsbn };

            BookMatchSelector.HasExactIdentifier(otherTitleExactIsbn).Should().BeTrue();
            BookMatchSelector.ChooseBest(candidates, 1.0).Should().Be(1);
        }

        [Test]
        public void should_choose_by_full_distance_among_candidates_with_equally_good_titles()
        {
            var wrongIsbn = new Distance();
            wrongIsbn.Add("author", 0.0);
            wrongIsbn.Add("book", 0.02);
            wrongIsbn.AddBool("isbn", true);

            var rightIsbn = new Distance();
            rightIsbn.Add("author", 0.0);
            rightIsbn.Add("book", 0.03);
            rightIsbn.AddBool("isbn", false);

            BookMatchSelector.ChooseBest(new List<Distance> { wrongIsbn, rightIsbn }, 1.0).Should().Be(1);
        }

        [Test]
        public void should_not_replace_a_match_already_held_that_is_better()
        {
            var candidates = new List<Distance> { ArtificialConditionEdition() };

            BookMatchSelector.ChooseBest(candidates, 0.05).Should().Be(-1);
        }

        [Test]
        public void should_choose_the_only_candidate_as_before()
        {
            var only = AllSystemsRedAudiobookEdition();

            BookMatchSelector.ChooseBest(new List<Distance> { only }, 1.0).Should().Be(0);
        }

        [Test]
        public void should_choose_nothing_when_there_are_no_candidates()
        {
            BookMatchSelector.ChooseBest(new List<Distance>(), 1.0).Should().Be(-1);
        }

        [Test]
        public void a_candidate_just_inside_the_margin_still_competes()
        {
            var best = new Distance();
            best.Add("author", 0.0);
            best.Add("book", 0.10);
            best.AddBool("isbn", true);

            var inside = new Distance();
            inside.Add("author", 0.0);
            inside.Add("book", 0.10 + BookMatchSelector.TitleMargin - 0.001);
            inside.AddBool("isbn", false);

            BookMatchSelector.ChooseBest(new List<Distance> { best, inside }, 1.0).Should().Be(1);
        }
    }
}

using System.Collections.Generic;
using System.Linq;

namespace NzbDrone.Core.MediaFiles.BookImport.Identification
{
    // Decides which candidate edition a file belongs to. The book is chosen first and the edition second.
    //
    // The distance weights come from a music tagger, where a different catalogue number means a different release. For books a file's
    // ISBN is usually the ISBN of another edition of the same book, yet a mismatching ISBN (weight 10) outweighs a completely different
    // title (weight 3), while a candidate with no ISBN is barely penalised. That let a different book win on missing data (an
    // audiobook edition of All Systems Red beat the Artificial Condition editions for an Artificial Condition epub).
    //
    // So only candidates whose title is close to the best title match compete on their full distance. A candidate whose ISBN or ASIN
    // matches the file exactly always competes: that is the strongest evidence of the exact edition.
    public static class BookMatchSelector
    {
        // How much worse than the best title match a candidate may be and still compete on its full distance.
        public const double TitleMargin = 0.1;

        public static double TitleDistance(Distance distance)
        {
            return distance.Penalties.TryGetValue("book", out var penalties) && penalties.Any() ? penalties.Min() : 0.0;
        }

        public static double BestTitleDistance(IEnumerable<Distance> candidates)
        {
            var list = candidates.ToList();
            return list.Any() ? list.Min(TitleDistance) : 0.0;
        }

        public static bool HasExactIdentifier(Distance distance)
        {
            return IsExact(distance, "isbn") || IsExact(distance, "asin");
        }

        public static bool IsEligible(Distance distance, double bestTitleDistance)
        {
            return TitleDistance(distance) <= bestTitleDistance + TitleMargin || HasExactIdentifier(distance);
        }

        // Returns the index of the chosen candidate, or -1 when none beats bestSoFar (the distance of any match already held).
        public static int ChooseBest(IReadOnlyList<Distance> candidates, double bestSoFar)
        {
            var bestTitle = BestTitleDistance(candidates);
            var chosen = -1;
            var chosenDistance = bestSoFar;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (!IsEligible(candidates[i], bestTitle))
                {
                    continue;
                }

                var value = candidates[i].NormalizedDistance();
                if (value < chosenDistance)
                {
                    chosen = i;
                    chosenDistance = value;
                }
            }

            return chosen;
        }

        private static bool IsExact(Distance distance, string key)
        {
            return distance.Penalties.TryGetValue(key, out var penalties) && penalties.Any() && penalties.All(x => x == 0.0);
        }
    }
}

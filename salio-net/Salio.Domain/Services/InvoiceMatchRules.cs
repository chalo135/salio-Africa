namespace Salio.Domain.Services;

// One of the shop's parts, as the matcher sees it.
public class InvoiceMatchCandidate
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
}

// A part that might be what an invoice line is for, with how alike they look
// (0 = nothing alike, 1 = the same).
public class InvoiceMatchSuggestion
{
    public Guid ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Sku { get; set; } = string.Empty;
    public double Score { get; set; }
}

// Finds the parts an invoice line most looks like. It only SUGGESTS: the
// owner chooses. Nothing here sets which part a line is for.
//
// How a score is made, kept simple on purpose:
//   - both names are tidied: lower case, punctuation becomes a space;
//   - words that are the same, or nearly (one typo in five letters), count
//     as shared words;
//   - the whole tidied names are compared letter by letter (edit distance);
//   - score = 0.6 x shared words + 0.4 x letters alike;
//   - if the part's SKU appears in the line's text, the score is at least 0.95.
public static class InvoiceMatchRules
{
    public const int MaxSuggestions = 3;

    // Below this a part is too unlike the line to be worth showing.
    public const double MinimumScore = 0.25;

    public const double SkuSeenScore = 0.95;

    public static List<InvoiceMatchSuggestion> Suggest(
        string proposedName, string rawText, List<InvoiceMatchCandidate> candidates)
    {
        string lineName = Normalise(proposedName);

        if (lineName.Length == 0)
        {
            lineName = Normalise(rawText);
        }

        string rawCompact = Compact(rawText + " " + proposedName);

        var scored = new List<InvoiceMatchSuggestion>();

        foreach (var candidate in candidates)
        {
            double score = Score(lineName, Normalise(candidate.Name));

            string skuCompact = Compact(candidate.Sku);

            // Short SKUs ("A1") would turn up inside unrelated text.
            if (skuCompact.Length >= 4 && rawCompact.Contains(skuCompact))
            {
                score = Math.Max(score, SkuSeenScore);
            }

            score = Math.Round(score, 2, MidpointRounding.AwayFromZero);

            if (score >= MinimumScore)
            {
                scored.Add(new InvoiceMatchSuggestion
                {
                    ProductId = candidate.ProductId,
                    Name = candidate.Name,
                    Sku = candidate.Sku,
                    Score = score
                });
            }
        }

        return scored
            .OrderByDescending(s => s.Score)
            .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ProductId)
            .Take(MaxSuggestions)
            .ToList();
    }

    // Both names already tidied with Normalise.
    public static double Score(string a, string b)
    {
        if (a.Length == 0 || b.Length == 0)
        {
            return 0;
        }

        return 0.6 * SharedWords(a, b) + 0.4 * Similarity(a, b);
    }

    // Lower case; anything that is not a letter or digit becomes a space;
    // runs of spaces become one.
    public static string Normalise(string? text)
    {
        if (text is null)
        {
            return string.Empty;
        }

        var kept = new System.Text.StringBuilder();

        foreach (char c in text.ToLowerInvariant())
        {
            kept.Append(char.IsLetterOrDigit(c) ? c : ' ');
        }

        return string.Join(' ', kept.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    // Letters and digits only, lower case: "BM-150" and "bm150" are one.
    private static string Compact(string? text)
    {
        return Normalise(text).Replace(" ", "");
    }

    // 0..1: how many words the two names share, out of all their words
    // (2 x shared / (words in a + words in b)). A word counts as shared, by
    // how alike it is, when it is at least 80% alike a word on the other side.
    private static double SharedWords(string a, string b)
    {
        string[] wordsA = a.Split(' ').Distinct().ToArray();
        string[] wordsB = b.Split(' ').Distinct().ToArray();

        double shared = 0;

        foreach (string wordA in wordsA)
        {
            double best = 0;

            foreach (string wordB in wordsB)
            {
                best = Math.Max(best, Similarity(wordA, wordB));
            }

            if (best >= 0.8)
            {
                shared = shared + best;
            }
        }

        return 2 * shared / (wordsA.Length + wordsB.Length);
    }

    // 0..1: 1 minus the edit distance over the longer length.
    public static double Similarity(string a, string b)
    {
        int longer = Math.Max(a.Length, b.Length);

        if (longer == 0)
        {
            return 1;
        }

        return 1.0 - (double)EditDistance(a, b) / longer;
    }

    // Levenshtein: the fewest single-letter inserts, deletes or changes that
    // turn a into b.
    private static int EditDistance(string a, string b)
    {
        int[] previous = new int[b.Length + 1];
        int[] current = new int[b.Length + 1];

        for (int j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (int i = 1; i <= a.Length; i++)
        {
            current[0] = i;

            for (int j = 1; j <= b.Length; j++)
            {
                int change = a[i - 1] == b[j - 1] ? 0 : 1;

                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + change);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}

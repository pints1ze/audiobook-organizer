using System.Text.RegularExpressions;
using AudiobookOrganizer.Api.Data;
using AudiobookOrganizer.Api.Models.Api;

namespace AudiobookOrganizer.Api.Services;

/// <summary>
/// Suggests pairs of people or genres that are probably the same entry. Pure: no database access, never merges,
/// and the result doesn't depend on input order.
/// </summary>
public static partial class CanonicalSuggestions
{
    [GeneratedRegex(@"^\p{L}\.?$")]
    private static partial Regex Initial();

    public static IReadOnlyList<MergeSuggestionDto> Suggest(CanonicalKind kind, IReadOnlyList<CanonicalEntryDto> entries)
    {
        var suggestions = new List<MergeSuggestionDto>();
        // shortcut: pairwise O(n²) over the whole catalog on each request — revisit when a catalog has more than a few thousand entries.
        for (var i = 0; i < entries.Count; i++)
            for (var j = i + 1; j < entries.Count; j++)
            {
                var (keep, merge) = KeepFirst(entries[i], entries[j]);
                // First rule found per pair wins; same letters is checked first.
                var reason = SameLetters(keep, merge)
                             ?? (kind == CanonicalKind.Person ? Initials(keep, merge) : null);
                if (reason is not null) suggestions.Add(new MergeSuggestionDto(keep, merge, reason));
            }

        return suggestions
            .OrderBy(s => s.Keep.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Merge.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.Keep.Id)
            .ThenBy(s => s.Merge.Id)
            .ToList();
    }

    /// <summary>Keep the entry with more aliases, then the lower id.</summary>
    private static (CanonicalEntryDto Keep, CanonicalEntryDto Merge) KeepFirst(CanonicalEntryDto a, CanonicalEntryDto b)
    {
        var aFirst = a.Aliases.Count != b.Aliases.Count ? a.Aliases.Count > b.Aliases.Count : a.Id < b.Id;
        return aFirst ? (a, b) : (b, a);
    }

    private static IEnumerable<string> Texts(CanonicalEntryDto entry) => [entry.Name, .. entry.Aliases];

    private static string LettersKey(string text) =>
        new string(text.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string? SameLetters(CanonicalEntryDto keep, CanonicalEntryDto merge)
    {
        foreach (var k in Texts(keep))
        {
            var key = LettersKey(k);
            if (key.Length == 0) continue;
            foreach (var m in Texts(merge))
                if (LettersKey(m) == key)
                    return $"\"{k}\" and \"{m}\" differ only in spacing or punctuation.";
        }
        return null;
    }

    private static string? Initials(CanonicalEntryDto keep, CanonicalEntryDto merge)
    {
        foreach (var k in Texts(keep))
            foreach (var m in Texts(merge))
            {
                var initialsFirst = InitialsMatch(k, m);
                if (initialsFirst is null) continue;
                var (shortForm, fullForm) = initialsFirst.Value ? (k, m) : (m, k);
                return $"\"{shortForm}\" may be \"{fullForm}\" with initials.";
            }
        return null;
    }

    /// <summary>
    /// Null unless the names have the same last word and word count, and each earlier word is equal or an initial
    /// of the other's, with at least one initial. Otherwise true when <paramref name="a"/> holds at least as many initials.
    /// </summary>
    private static bool? InitialsMatch(string a, string b)
    {
        var aWords = CanonicalNameService.Clean(a).ToUpperInvariant().Split(' ');
        var bWords = CanonicalNameService.Clean(b).ToUpperInvariant().Split(' ');
        if (aWords.Length != bWords.Length || aWords[^1] != bWords[^1]) return null;

        int aInitials = 0, bInitials = 0;
        for (var w = 0; w < aWords.Length - 1; w++)
        {
            var (x, y) = (aWords[w], bWords[w]);
            if (x == y) continue;
            if (Initial().IsMatch(x) && x[0] == y[0]) aInitials++;
            else if (Initial().IsMatch(y) && y[0] == x[0]) bInitials++;
            else return null;
        }
        if (aInitials + bInitials == 0) return null;
        return aInitials >= bInitials;
    }
}

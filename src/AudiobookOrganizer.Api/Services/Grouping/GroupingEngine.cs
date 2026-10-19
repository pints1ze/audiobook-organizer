using System.Text;
using System.Text.RegularExpressions;

namespace AudiobookOrganizer.Api.Services.Grouping;

/// <summary>
/// Deterministic rules that propose audiobook groups from scanned files. Pure: no file system or database access,
/// and the result doesn't depend on input order.
/// </summary>
public static partial class GroupingEngine
{
    [GeneratedRegex(@"^(?:disc|disk|cd|part)\s*(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex DiscFolderPattern();

    // A track token at the start: optional "track", a number (optionally in brackets), then a separator or the end.
    [GeneratedRegex(@"^(?:track[\s\-_.]*)?\(?(\d+)\)?(?:[\s\-_.]+|$)", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingTrackPattern();

    // A track token at the end, which must follow a separator so digits inside a word ("rpo1") stay.
    [GeneratedRegex(@"(?:^|[\s\-_.]+)(?:track[\s\-_.]*)?\(?(\d+)\)?$", RegexOptions.IgnoreCase)]
    private static partial Regex TrailingTrackPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    private static readonly Comparer<string> Natural = Comparer<string>.Create(NaturalCompare);

    private sealed record Parsed(
        GroupingInput Input,
        string Container,
        string? ContainerName,
        string? DiscFolder,
        int? Disc,
        int? Track,
        bool TrackFromTag,
        string Stem,
        string FileName,
        bool IsM4b,
        string? AlbumKey);

    public static GroupingResult Group(IEnumerable<GroupingInput> files)
    {
        var groups = new List<(string Container, ProposedGroup Group, string FirstPath)>();
        var unresolved = new List<long>();

        foreach (var container in files.Select(Parse).GroupBy(p => p.Container))
        {
            var all = container.ToList();
            var isRoot = container.Key == "";
            void Add(List<Parsed> members, bool wholeFolder)
            {
                var ordered = Order(members);
                groups.Add((container.Key, Build(ordered, isRoot, wholeFolder), ordered[0].Input.RelativePath));
            }

            foreach (var m4b in all.Where(p => p.IsM4b))
                Add([m4b], all.Count == 1);

            var mp3s = all.Where(p => !p.IsM4b).ToList();
            foreach (var album in mp3s.Where(p => p.AlbumKey is not null).GroupBy(p => p.AlbumKey))
                Add(album.ToList(), album.Count() == all.Count);

            var untagged = mp3s.Where(p => p.AlbumKey is null).ToList();
            if (untagged.Count == 0) continue;
            var stems = untagged.Where(p => p.Stem != "").GroupBy(p => p.Stem).ToList();

            if (isRoot)
            {
                // Loose root files need a filename series of 2 or more; anything else is unresolved.
                foreach (var stem in stems.Where(s => s.Count() >= 2))
                    Add(stem.ToList(), false);
                unresolved.AddRange(untagged
                    .Where(p => p.Stem == "" || stems.Single(s => s.Key == p.Stem).Count() < 2)
                    .Select(p => p.Input.FileId));
            }
            else if (stems.Count >= 2 && untagged.All(p => p.Stem != "") && stems.All(s => s.Count() >= 2))
            {
                // The folder holds several clear filename series (scenario D inside a folder): one group each.
                foreach (var stem in stems)
                    Add(stem.ToList(), false);
            }
            else
            {
                // Otherwise the folder is the evidence: one shared stem, no usable stems, or chapter-style names.
                Add(untagged, untagged.Count == all.Count);
            }
        }

        return new GroupingResult(
            groups.OrderBy(g => g.Container, StringComparer.Ordinal)
                .ThenBy(g => g.FirstPath, Natural)
                .ThenBy(g => g.Group.Files[0].FileId)
                .Select(g => g.Group)
                .ToList(),
            unresolved.Order().ToList());
    }

    private static Parsed Parse(GroupingInput input)
    {
        var segments = input.RelativePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var fileName = segments.Length > 0 ? segments[^1] : input.RelativePath;
        var folders = segments.Length > 0 ? segments[..^1] : [];

        string? discFolder = null;
        int? folderDisc = null;
        if (folders.Length > 0 && DiscFolderPattern().Match(folders[^1].Trim()) is { Success: true } discMatch)
        {
            discFolder = folders[^1];
            folderDisc = int.Parse(discMatch.Groups[1].Value);
            folders = folders[..^1];
        }

        var (stem, fileTrack) = ParseFileName(fileName);
        var album = Normalise(input.Album);
        var albumKey = album == "" ? null : album + "|" + Normalise(input.AlbumArtist ?? input.Artist);

        return new Parsed(
            input,
            string.Join('/', folders),
            folders.Length > 0 ? folders[^1] : null,
            discFolder,
            input.Disc ?? folderDisc,
            input.Track ?? fileTrack,
            input.Track is not null,
            stem,
            fileName,
            input.Extension.TrimStart('.').Equals("m4b", StringComparison.OrdinalIgnoreCase),
            albumKey);
    }

    /// <summary>The filename without extension and leading/trailing track tokens, lower-cased; plus that track number.</summary>
    private static (string Stem, int? Track) ParseFileName(string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        int? track = null;

        if (LeadingTrackPattern().Match(name) is { Success: true } leading)
        {
            track = int.Parse(leading.Groups[1].Value);
            name = name[leading.Length..];
        }
        else if (TrailingTrackPattern().Match(name) is { Success: true } trailing)
        {
            track = int.Parse(trailing.Groups[1].Value);
            name = name[..trailing.Index];
        }

        var stem = Whitespace().Replace(name.ToLowerInvariant(), " ").Trim(' ', '-', '_', '.');
        return (stem, track);
    }

    /// <summary>Lower-case letters and digits only, so "Heartwood" and "Heartwood." share a key.</summary>
    private static string Normalise(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        var sb = new StringBuilder();
        foreach (var c in value)
            if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    private static List<Parsed> Order(IEnumerable<Parsed> files) =>
        files.OrderBy(p => p.Disc ?? 0)
            .ThenBy(p => p.Track ?? int.MaxValue)
            .ThenBy(p => p.Input.RelativePath, Natural)
            .ThenBy(p => p.Input.FileId)
            .ToList();

    /// <param name="wholeFolder">The group holds every audio file in its (non-root) container, so the folder is a signal.</param>
    private static ProposedGroup Build(List<Parsed> files, bool isRoot, bool wholeFolder)
    {
        var evidence = new List<string>();
        var signals = 0;
        var conflict = false;
        var first = files[0];
        var useFolder = !isRoot && wholeFolder;

        if (first.IsM4b)
        {
            evidence.Add($"Single M4B file \"{first.FileName}\"");
            signals++;
        }
        else if (!isRoot)
        {
            evidence.Add($"Same folder \"{first.ContainerName}\"");
            if (wholeFolder) signals++;
        }

        var discFolders = files.Select(p => p.DiscFolder).OfType<string>().Distinct().Order(Natural).ToList();
        if (discFolders.Count > 0)
            evidence.Add("Disc folders " + string.Join(", ", discFolders.Select(d => $"\"{d}\"")));

        var albums = files.Select(p => p.Input.Album?.Trim()).Where(a => !string.IsNullOrEmpty(a)).ToList();
        if (albums.Count == files.Count)
        {
            var distinct = albums.Distinct().Order(StringComparer.Ordinal).ToList();
            if (distinct.Count == 1)
            {
                evidence.Add(files.Count == 1
                    ? $"Album tag \"{distinct[0]}\""
                    : $"Album tag \"{distinct[0]}\" on all {files.Count} files");
                signals++;
            }
            else
            {
                evidence.Add("Album tags differ slightly: " + string.Join(", ", distinct.Select(a => $"\"{a}\"")));
                conflict = true;
            }
        }

        if (!first.IsM4b && first.Stem != "" && files.All(p => p.Stem == first.Stem))
        {
            evidence.Add($"Filename pattern \"{first.Stem}\"");
            signals++;
        }

        // Tag track numbers are independent of the filename; numbers parsed from filenames are part of the filename signal.
        if (files.Count > 1 && files.All(p => p.TrackFromTag))
        {
            var byDisc = files.GroupBy(p => p.Disc ?? 0).ToList();
            if (byDisc.All(d => d.Select(p => p.Track!.Value).Order().SequenceEqual(Enumerable.Range(1, d.Count()))))
            {
                evidence.Add(byDisc.Count == 1
                    ? $"Track numbers 1–{files.Count} continuous"
                    : "Track numbers continuous on each disc");
                signals++;
            }
        }

        var tracks = files.Where(p => p.Track is not null).Select(p => p.Track!.Value).ToList();
        if (files.All(p => p.Disc is null) && tracks.Distinct().Count() < tracks.Count)
        {
            evidence.Add("Track numbers repeat with no disc info");
            conflict = true;
        }

        var (folderTitle, folderAuthor) = SplitFolderName(useFolder ? first.ContainerName : null);
        var title = MostCommon(files.Select(p => p.Input.Album))
            ?? folderTitle
            ?? MostCommon(files.Select(p => p.Stem))
            ?? (useFolder ? first.ContainerName : null);
        var author = MostCommon(files.Select(p => p.Input.AlbumArtist))
            ?? MostCommon(files.Select(p => p.Input.Artist))
            ?? folderAuthor;

        var confidence = conflict ? GroupConfidence.Low
            : signals >= 2 ? GroupConfidence.High
            : signals == 1 ? GroupConfidence.Medium
            : GroupConfidence.Low;

        return new ProposedGroup(
            title,
            author,
            confidence,
            evidence,
            files.Select(p => new GroupedFile(p.Input.FileId, p.Disc, p.Track)).ToList(),
            files.Sum(p => p.Input.DurationMs ?? 0));
    }

    /// <summary>"title - author" folder names give both parts; anything else gives neither.</summary>
    private static (string? Title, string? Author) SplitFolderName(string? name)
    {
        var parts = name?.Split(" - ", 2, StringSplitOptions.TrimEntries);
        return parts is [{ Length: > 0 } title, { Length: > 0 } author] ? (title, author) : (null, null);
    }

    private static string? MostCommon(IEnumerable<string?> values) =>
        values.Select(v => v?.Trim())
            .Where(v => !string.IsNullOrEmpty(v))
            .GroupBy(v => v!, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();

    /// <summary>Compares digit runs by value, so "track2" sorts before "track10". Ties fall back to ordinal order.</summary>
    private static int NaturalCompare(string? a, string? b)
    {
        a ??= "";
        b ??= "";
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (char.IsAsciiDigit(a[i]) && char.IsAsciiDigit(b[j]))
            {
                int si = i, sj = j;
                while (i < a.Length && char.IsAsciiDigit(a[i])) i++;
                while (j < b.Length && char.IsAsciiDigit(b[j])) j++;
                var na = a[si..i].TrimStart('0');
                var nb = b[sj..j].TrimStart('0');
                var cmp = na.Length != nb.Length ? na.Length.CompareTo(nb.Length) : string.CompareOrdinal(na, nb);
                if (cmp != 0) return cmp;
            }
            else
            {
                var cmp = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                if (cmp != 0) return cmp;
                i++;
                j++;
            }
        }
        var rest = (a.Length - i).CompareTo(b.Length - j);
        return rest != 0 ? rest : string.CompareOrdinal(a, b);
    }
}

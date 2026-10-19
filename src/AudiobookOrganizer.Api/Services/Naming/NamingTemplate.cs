using System.Text;
using System.Text.RegularExpressions;

namespace AudiobookOrganizer.Api.Services.Naming;

/// <summary>
/// Renders a naming template (<c>%token%</c>, <c>%token:N%</c> padding and <c>[optional]</c> sections) into a
/// sanitized relative path, and resolves relative paths under a root without ever escaping it. Pure: no I/O.
/// </summary>
public static partial class NamingTemplate
{
    public static readonly IReadOnlyList<string> Tokens =
        ["artist", "album", "series", "series-part", "track", "disc", "title", "year", "narrator"];

    private const int MaxSegmentLength = 200;
    private const string EmptyPathError = "The template produced an empty path.";

    private static readonly HashSet<string> ReservedNames = new(
        ["CON", "PRN", "AUX", "NUL",
         "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"],
        StringComparer.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(\d+)(.*)$", RegexOptions.Singleline)]
    private static partial Regex LeadingInteger();

    [GeneratedRegex(@"^[A-Za-z]:")]
    private static partial Regex DriveLetter();

    private abstract record Part;
    private sealed record Literal(string Text) : Part;
    private sealed record Token(string Name, int Pad) : Part;
    private sealed record Section(IReadOnlyList<Part> Parts) : Part;

    public static TemplateResult Render(string template, IReadOnlyDictionary<string, string?> values)
    {
        var (parts, error) = Parse(template);
        if (error is not null) return Fail(error);

        var lookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in values)
            if (!string.IsNullOrWhiteSpace(value)) lookup[key] = value.Trim();

        var missing = new List<string>();
        var text = new StringBuilder();
        foreach (var part in parts)
        {
            switch (part)
            {
                case Literal literal:
                    text.Append(literal.Text);
                    break;
                case Token token:
                    if (lookup.TryGetValue(token.Name, out var value)) text.Append(Substitute(value, token.Pad));
                    else if (!missing.Contains(token.Name)) missing.Add(token.Name);
                    break;
                case Section section:
                    var tokens = section.Parts.OfType<Token>().ToList();
                    if (tokens.Any(t => !lookup.ContainsKey(t.Name))) break;
                    foreach (var inner in section.Parts)
                        text.Append(inner is Token t ? Substitute(lookup[t.Name], t.Pad) : ((Literal)inner).Text);
                    break;
            }
        }

        // Token values were cleaned of '/' and '\', so the only separators left are the template's own.
        var segments = text.ToString().Split('/', '\\').Select(SanitizeSegment).Where(s => s != "").ToList();
        return segments.Count == 0
            ? Fail(EmptyPathError)
            : new TemplateResult(string.Join('/', segments), missing, null);
    }

    /// <summary>
    /// The full path of <paramref name="relativePath"/> under <paramref name="root"/>, or null when the relative path
    /// is rooted or would land outside root (or on root itself).
    /// </summary>
    public static string? ResolveUnder(string root, string relativePath)
    {
        if (Path.IsPathRooted(relativePath) || relativePath.StartsWith('/') || relativePath.StartsWith('\\')
            || DriveLetter().IsMatch(relativePath))
            return null;

        var fullRoot = Path.GetFullPath(root);
        if (!Path.EndsInDirectorySeparator(fullRoot)) fullRoot += Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(fullRoot, relativePath));

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return full.Length > fullRoot.Length && full.StartsWith(fullRoot, comparison) ? full : null;
    }

    private static TemplateResult Fail(string error) => new(null, [], error);

    private static (List<Part> Parts, string? Error) Parse(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return ([], "The template is blank.");

        var top = new List<Part>();
        List<Part>? section = null;
        var literal = new StringBuilder();

        void FlushLiteral()
        {
            if (literal.Length == 0) return;
            (section ?? top).Add(new Literal(literal.ToString()));
            literal.Clear();
        }

        for (var i = 0; i < template.Length; i++)
        {
            var c = template[i];
            switch (c)
            {
                case '%':
                    var close = template.IndexOf('%', i + 1);
                    if (close < 0) return ([], $"The '%' at position {i + 1} has no closing '%'.");
                    var (token, tokenError) = ParseToken(template[(i + 1)..close]);
                    if (tokenError is not null) return ([], tokenError);
                    FlushLiteral();
                    (section ?? top).Add(token!);
                    i = close;
                    break;
                case '[':
                    if (section is not null) return ([], "Optional sections can't be nested.");
                    FlushLiteral();
                    section = [];
                    break;
                case ']':
                    if (section is null) return ([], "A ']' has no matching '['.");
                    FlushLiteral();
                    top.Add(new Section(section));
                    section = null;
                    break;
                default:
                    literal.Append(c);
                    break;
            }
        }

        if (section is not null) return ([], "A '[' has no matching ']'.");
        FlushLiteral();
        return HasDotSegment(top) ? ([], "The template can't contain a '.' or '..' folder.") : (top, null);
    }

    private static (Token? Token, string? Error) ParseToken(string body)
    {
        var colon = body.IndexOf(':');
        var name = (colon < 0 ? body : body[..colon]).ToLowerInvariant();
        if (!Tokens.Contains(name)) return (null, $"Unknown token '%{body}%'.");
        if (colon < 0) return (new Token(name, 0), null);

        var width = body[(colon + 1)..];
        return width.Length == 1 && width[0] is >= '1' and <= '9'
            ? (new Token(name, width[0] - '0'), null)
            : (null, $"The padding in '%{body}%' must be a number from 1 to 9.");
    }

    /// <summary>True when a path segment of the template is made only of literal text that reads "." or "..".</summary>
    private static bool HasDotSegment(IEnumerable<Part> parts)
    {
        var segment = new StringBuilder();
        var hasToken = false;
        bool EndSegment()
        {
            var text = segment.ToString().Trim();
            var dot = !hasToken && text is "." or "..";
            segment.Clear();
            hasToken = false;
            return dot;
        }

        foreach (var part in parts.SelectMany(p => p is Section s ? s.Parts : [p]))
        {
            if (part is Token) { hasToken = true; continue; }
            foreach (var c in ((Literal)part).Text)
            {
                if (c is '/' or '\\') { if (EndSegment()) return true; }
                else segment.Append(c);
            }
        }
        return EndSegment();
    }

    private static string Substitute(string value, int pad)
    {
        var cleaned = ReplaceIllegal(value, alsoSeparators: true);
        if (pad == 0) return cleaned;
        var match = LeadingInteger().Match(cleaned);
        return match.Success ? match.Groups[1].Value.PadLeft(pad, '0') + match.Groups[2].Value : cleaned;
    }

    private static string ReplaceIllegal(string text, bool alsoSeparators)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            var illegal = c < ' ' || c is '<' or '>' or ':' or '"' or '|' or '?' or '*'
                || (alsoSeparators && c is '/' or '\\');
            builder.Append(illegal ? '-' : c);
        }
        return builder.ToString();
    }

    private static string SanitizeSegment(string segment)
    {
        var text = ReplaceIllegal(segment.Normalize(NormalizationForm.FormC), alsoSeparators: false);
        text = TrimSegment(Whitespace().Replace(text, " "));
        if (text == "") return "";

        var dot = text.IndexOf('.');
        var stem = dot < 0 ? text : text[..dot];
        if (ReservedNames.Contains(stem)) text = stem + "_" + text[stem.Length..];

        // shortcut: char cap, not a 255-byte UTF-8 cap — revisit when a real name hits a filesystem limit
        if (text.Length > MaxSegmentLength)
        {
            var cut = char.IsHighSurrogate(text[MaxSegmentLength - 1]) ? MaxSegmentLength - 1 : MaxSegmentLength;
            text = TrimSegment(text[..cut]);
        }
        return text;
    }

    private static string TrimSegment(string text) => text.Trim().TrimEnd('.', ' ');
}

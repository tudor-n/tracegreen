using System.Text.RegularExpressions;
using TraceGreen.Domain.Copy;

namespace TraceGreen.Domain.Chat;

/// <summary>A piece of a checked answer: model-written text, or a fact the API fills in from the data.</summary>
public sealed record AnswerSegment(string? Text, string? FactId);

/// <summary>
/// The outcome of <see cref="AnswerCheck.Check"/>: the answer cut into text and fact segments when every rule holds,
/// else <see cref="Problem"/> names the first rule it broke (for the log, never for the reader).
/// </summary>
public sealed record CheckedAnswer(bool Ok, string? Problem, IReadOnlyList<AnswerSegment> Segments, IReadOnlyList<string> FactIds);

/// <summary>
/// The rules a model's chat answer must pass before a reader sees it (F13). The model writes the sentence, but every
/// value and every name in it must be a <c>{{factId}}</c> placeholder for a fact of the pack, so the API fills them in
/// from the data. Refused: an unknown placeholder; a digit outside a placeholder (CO2 and CO2e excepted); a supplier,
/// buyer or place name of the pack written out; a company suffix (an invented supplier); a banned term; more than
/// <see cref="MaxChars"/> characters; and an answer that cites no fact at all. What it cannot catch: a sentence that
/// cites the right kind of fact for the wrong supplier. Every cited fact is shown with whose it is and links to its
/// source, so a reader can check.
/// </summary>
public static partial class AnswerCheck
{
    public const int MaxChars = 600;

    public static CheckedAnswer Check(string text, FactPack pack)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Fail("empty answer");
        }
        if (text.Length > MaxChars)
        {
            return Fail("answer too long");
        }

        var segments = new List<AnswerSegment>();
        var cited = new List<string>();
        var outside = new System.Text.StringBuilder();
        var at = 0;
        foreach (Match match in Placeholder().Matches(text))
        {
            var id = match.Groups[1].Value;
            if (pack.Find(id) is null)
            {
                return Fail($"unknown fact {id}");
            }
            if (match.Index > at)
            {
                var piece = text[at..match.Index];
                segments.Add(new AnswerSegment(piece, null));
                outside.Append(piece).Append(' ');
            }
            segments.Add(new AnswerSegment(null, id));
            if (!cited.Contains(id, StringComparer.Ordinal))
            {
                cited.Add(id);
            }
            at = match.Index + match.Length;
        }
        if (at < text.Length)
        {
            segments.Add(new AnswerSegment(text[at..], null));
            outside.Append(text[at..]);
        }

        var free = outside.ToString();
        if (free.Contains("{{", StringComparison.Ordinal) || free.Contains("}}", StringComparison.Ordinal))
        {
            return Fail("malformed placeholder");
        }
        if (Unit().Replace(free, " ").Any(char.IsDigit))
        {
            return Fail("a number written out");
        }
        if (pack.Names.Any(name => Mentions(free, name)))
        {
            return Fail("a name written out");
        }
        if (CompanySuffix().IsMatch(free) || UpperSuffix().IsMatch(free))
        {
            return Fail("a company name written out");
        }
        if (BannedTerms.Any(free))
        {
            return Fail("a banned term");
        }
        if (cited.Count == 0)
        {
            return Fail("no fact cited");
        }
        if (Claims(segments, pack) is { } claim)
        {
            return Fail(claim);
        }
        return new CheckedAnswer(true, null, Merge(segments), cited);
    }

    /// <summary>
    /// The claims a sentence makes in its own words about the suppliers it cites, checked against the facts: a status word
    /// (estimated, partly confirmed, confirmed) must be the status of every supplier the sentence cites; "largest" and
    /// its kin must cite S1, "smallest" and its kin the last supplier; "X more than Y" needs X ranked above Y. Null when
    /// every claim holds, else the claim that does not.
    /// </summary>
    private static string? Claims(IReadOnlyList<AnswerSegment> segments, FactPack pack)
    {
        foreach (var sentence in Sentences(segments))
        {
            var words = string.Join(' ', sentence.Where(s => s.Text is not null).Select(s => s.Text)).ToLowerInvariant();
            var ranks = sentence.Where(s => s.FactId is not null).Select(s => FactPack.RankOf(s.FactId!)).OfType<int>().Distinct().ToList();
            if (ranks.Count == 0)
            {
                continue;
            }

            var said = StatusesSaid(words);
            if (said.Count > 0 && ranks.Any(r => pack.StatusOf(r) is { } status && !said.Contains(status)))
            {
                return "a status claim the data does not hold";
            }
            if (Largest().IsMatch(words) && !ranks.Contains(1))
            {
                return "a largest claim the data does not hold";
            }
            if (Smallest().IsMatch(words) && !ranks.Contains(pack.SupplierCount))
            {
                return "a smallest claim the data does not hold";
            }
            if (ranks.Count >= 2 && Comparison().Match(words) is { Success: true } comparison)
            {
                var more = comparison.Groups["more"].Success;
                if (more ? ranks[0] > ranks[1] : ranks[0] < ranks[1])
                {
                    return "a comparison the data does not hold";
                }
            }
        }
        return null;
    }

    /// <summary>The statuses a sentence names; "not confirmed" and "unconfirmed" name the two that are not.</summary>
    private static HashSet<string> StatusesSaid(string words)
    {
        var said = new HashSet<string>(StringComparer.Ordinal);
        var rest = words;
        if (NotConfirmed().IsMatch(rest))
        {
            said.Add("ESTIMATED");
            said.Add("PARTIAL");
            rest = NotConfirmed().Replace(rest, " ");
        }
        if (PartlyConfirmed().IsMatch(rest))
        {
            said.Add("PARTIAL");
            rest = PartlyConfirmed().Replace(rest, " ");
        }
        if (Confirmed().IsMatch(rest))
        {
            said.Add("VERIFIED");
        }
        if (Estimated().IsMatch(rest))
        {
            said.Add("ESTIMATED");
        }
        return said;
    }

    /// <summary>The answer cut at sentence ends (. ! ? ;) in its text segments; a fact never ends a sentence.</summary>
    private static IEnumerable<List<AnswerSegment>> Sentences(IReadOnlyList<AnswerSegment> segments)
    {
        var current = new List<AnswerSegment>();
        foreach (var segment in segments)
        {
            if (segment.Text is null)
            {
                current.Add(segment);
                continue;
            }
            var parts = SentenceEnd().Split(segment.Text);
            for (var i = 0; i < parts.Length; i++)
            {
                if (i > 0)
                {
                    yield return current;
                    current = [];
                }
                current.Add(new AnswerSegment(parts[i], null));
            }
        }
        yield return current;
    }

    /// <summary>Whether <paramref name="text"/> writes out the name, or its first two words when it has more.</summary>
    private static bool Mentions(string text, string name)
    {
        var words = name.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var probe = words.Length > 2 ? string.Join(' ', words.Take(2)) : name.Trim();
        // A one-word name shorter than four letters ("RO", a code) is too common to tell apart from ordinary text.
        if (words.Length == 1 && probe.Length < 4)
        {
            return false;
        }
        return Regex.IsMatch(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(probe)}(?![\p{{L}}\p{{N}}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<AnswerSegment> Merge(List<AnswerSegment> segments) =>
        segments.Where(s => s.FactId is not null || !string.IsNullOrEmpty(s.Text)).ToList();

    private static CheckedAnswer Fail(string problem) => new(false, problem, [], []);

    [GeneratedRegex(@"[.!?;]+(?=\s|$)")]
    private static partial Regex SentenceEnd();

    [GeneratedRegex(@"\b(?:not(?:\s+yet)?\s+(?:supplier-)?confirmed|unconfirmed)\b")]
    private static partial Regex NotConfirmed();

    [GeneratedRegex(@"\b(?:partly|partially)\s+confirmed\b|\bpartial\b")]
    private static partial Regex PartlyConfirmed();

    [GeneratedRegex(@"\b(?:supplier-)?(?:confirmed|verified)\b")]
    private static partial Regex Confirmed();

    [GeneratedRegex(@"\b(?:estimated|estimate|estimates)\b")]
    private static partial Regex Estimated();

    [GeneratedRegex(@"\b(?:largest|biggest|highest|most|top|greatest|main)\b")]
    private static partial Regex Largest();

    [GeneratedRegex(@"\b(?:smallest|lowest|least|fewest)\b")]
    private static partial Regex Smallest();

    [GeneratedRegex(@"\b(?:(?<more>more|higher|larger|bigger|greater)|less|lower|smaller|fewer)\s+than\b")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The unit's own digit: CO2, CO₂, CO2e, tCO2e.</summary>
    [GeneratedRegex(@"\b(?:t\s?)?CO[2₂]e?\b", RegexOptions.IgnoreCase)]
    private static partial Regex Unit();

    [GeneratedRegex(@"(?<![\p{L}])(?:S\.R\.L\.?|SRL|GmbH|Ltd\.?|Inc\.?|LLC|PLC|Corp\.?|S\.A\.)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffix();

    /// <summary>Short suffixes that are ordinary words in lower case, matched in capitals only.</summary>
    [GeneratedRegex(@"(?<![\p{L}])(?:SA|AG|BV|SAS|Co\.|Limited)(?![\p{L}])")]
    private static partial Regex UpperSuffix();
}

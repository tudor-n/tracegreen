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
/// from the data. Refused: an unknown placeholder; a digit outside a placeholder (CO2, CO2e and Scope 1–3 excepted); a
/// supplier, buyer or place name of the pack written out; a company suffix (an invented supplier); a banned term; more
/// than <see cref="MaxChars"/> characters; and a sentence that claims something about the buyer's data in its own words
/// ("your largest supplier…", "most of it…", "your suppliers are estimated") without citing a fact. An answer may cite
/// no fact at all when it is general: help, how TraceGreen works, a term, advice. What it cannot catch: a sentence that
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
        if (FormatWords().IsMatch(free))
        {
            return Fail("the answer's format in its text");
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
        if (Claims(segments, pack) is { } claim)
        {
            return Fail(claim);
        }
        return new CheckedAnswer(true, null, Merge(segments), cited);
    }

    /// <summary>
    /// The answer without the one sentence that broke a rule, when it has at least <see cref="MinSentencesToTrim"/>
    /// sentences, dropping that one sentence is enough, and what is left still cites a fact if the answer did. Null
    /// otherwise. The last resort before "not answered": the reader sees the true part of a long answer, never the false.
    /// </summary>
    public static CheckedAnswer? Trim(string text, FactPack pack)
    {
        var sentences = SentenceBreak().Split(text.Trim()).Where(s => s.Length > 0).ToList();
        if (sentences.Count < MinSentencesToTrim)
        {
            return null;
        }
        var cited = Placeholder().IsMatch(text);
        for (var drop = sentences.Count - 1; drop >= 0; drop--)
        {
            // Without its first sentence, an answer that goes on "Confirm their deliveries…" has lost what "their" meant.
            if (drop == 0 && LeadingPronoun().IsMatch(sentences[1]))
            {
                continue;
            }
            var rest = Check(string.Join(' ', sentences.Where((_, i) => i != drop)), pack);
            if (rest.Ok && (!cited || rest.FactIds.Count > 0))
            {
                return rest;
            }
        }
        return null;
    }

    /// <summary>An answer shorter than this is not trimmed: what would be left is too little to stand on its own.</summary>
    public const int MinSentencesToTrim = 3;

    /// <summary>
    /// What a model is told when the API refused its answer and asks once more (<c>ChatService</c>): the rule it broke and
    /// what to do instead. Holds no data, like the rule itself.
    /// </summary>
    public static string RetryHint(string problem) => problem switch
    {
        "a number written out" => "it types a number; cite the fact that holds the value, or leave it out",
        "the answer's format in its text" => "it writes factIds or kind into the text; the text holds only the sentences",
        "a name written out" => "it types a name from the data; write the name's fact, such as {{S1.name}}, in its place",
        "a company name written out" => "it names a company; cite the supplier's name fact, or leave the name out",
        "a banned term" => "it uses a term TraceGreen does not use; say supplier-confirmed share, or tamper-evident audit log",
        "a status claim the data does not hold" =>
            "it gives a supplier a status its status fact does not hold; read each {{Sn.status}}, or cite {{list.estimated}}, {{list.partial}} or {{list.confirmed}}",
        "a largest claim the data does not hold" =>
            "it calls a supplier the largest when the facts rank another one higher; S1 emits the most, S2 the next, and each status list is largest first",
        "a smallest claim the data does not hold" =>
            "it calls a supplier the smallest when the facts rank another one lower; the highest S number emits the least",
        "a comparison the data does not hold" => "it compares two suppliers the wrong way round; a lower S number emits more",
        "a claim about the data without a fact" => "it states something about the buyer's data without citing the fact that holds it",
        _ => problem,
    };

    /// <summary>
    /// The claims a sentence makes in its own words about the suppliers it cites, checked against the facts: a status word
    /// (estimated, partly confirmed, confirmed) must be the status of every supplier the sentence cites; "largest" and its
    /// kin must cite S1, "second largest" S2 and so on, and with a status word the largest of that status ("the largest
    /// estimated emitter"), and "the next largest" the one after the supplier an earlier sentence called largest;
    /// "smallest" likewise from the end; "X more than Y" needs X ranked above Y. Null when every claim holds, else the
    /// claim that does not.
    /// </summary>
    private static string? Claims(IReadOnlyList<AnswerSegment> segments, FactPack pack)
    {
        int? previousLargest = null;
        foreach (var sentence in Sentences(segments))
        {
            var words = string.Join(' ', sentence.Where(s => s.Text is not null).Select(s => s.Text)).ToLowerInvariant();
            if (!sentence.Any(s => s.FactId is not null) && DataClaim().IsMatch(words))
            {
                return "a claim about the data without a fact";
            }
            var ranks = sentence.Where(s => s.FactId is not null).Select(s => FactPack.RankOf(s.FactId!)).OfType<int>().Distinct().ToList();
            if (ranks.Count == 0)
            {
                continue;
            }

            var said = StatusesSaid(words);
            // Only a status next to the rank word narrows the ranking ("the largest estimated emitter", "the largest
            // supplier still estimated"); "…, which is estimated" is said of the supplier and checked just below.
            var filter = StatusesSaid(string.Join(' ', RankPhrase().Matches(words).Select(m => m.Groups["after"].Value)));
            if (said.Count > 0 && ranks.Any(r => pack.StatusOf(r) is { } status && !said.Contains(status)))
            {
                return "a status claim the data does not hold";
            }
            if (Largest().IsMatch(words) && !NextSmallest().IsMatch(words))
            {
                // "The next largest" follows the supplier an earlier sentence called largest (S1 when none did).
                var expected = NextLargest().IsMatch(words)
                    ? After(pack, filter, previousLargest ?? 1)
                    : Ranked(pack, filter, Nth(OrdinalLargest().Match(words)), fromEnd: false);
                if (expected is not { } largest || !ranks.Contains(largest))
                {
                    return "a largest claim the data does not hold";
                }
                previousLargest = largest;
            }
            // "The largest emitters are X and Y": the top ones, in turn, of the status the sentence names, if any.
            if (PluralLargest().IsMatch(words) && ranks.Count >= 2
                && !Enumerable.Range(1, ranks.Count).All(n => Ranked(pack, filter, n, fromEnd: false) is { } r && ranks.Contains(r)))
            {
                return "a largest claim the data does not hold";
            }
            if (Smallest().IsMatch(words) && !NextSmallest().IsMatch(words) && !NextLargest().IsMatch(words)
                && (Ranked(pack, filter, Nth(OrdinalSmallest().Match(words)), fromEnd: true) is not { } smallest || !ranks.Contains(smallest)))
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

    /// <summary>
    /// The rank of the <paramref name="nth"/> largest supplier (or smallest, <paramref name="fromEnd"/>) among those with a
    /// status the sentence names, or among all when it names none; null when there are not that many.
    /// </summary>
    private static int? Ranked(FactPack pack, HashSet<string> said, int nth, bool fromEnd)
    {
        var matching = Enumerable.Range(1, pack.SupplierCount)
            .Where(r => said.Count == 0 || (pack.StatusOf(r) is { } status && said.Contains(status)))
            .ToList();
        if (fromEnd)
        {
            matching.Reverse();
        }
        return nth <= matching.Count ? matching[nth - 1] : null;
    }

    /// <summary>The first rank after <paramref name="rank"/> among those with a status the sentence names (or all).</summary>
    private static int? After(FactPack pack, HashSet<string> said, int rank) =>
        Enumerable.Range(rank + 1, Math.Max(0, pack.SupplierCount - rank))
            .Where(r => said.Count == 0 || (pack.StatusOf(r) is { } status && said.Contains(status)))
            .Select(r => (int?)r)
            .FirstOrDefault();

    /// <summary>The n of "second largest" (2) and its kin; 1 when the rank word has no ordinal.</summary>
    private static int Nth(Match ordinal) => !ordinal.Success ? 1 : ordinal.Groups["n"].Value switch
    {
        "second" => 2,
        "third" => 3,
        "fourth" => 4,
        _ => 5,
    };

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

    /// <summary>The answer's own fields written into its text ("factIds: …", "kind: data"), which a small model does.</summary>
    [GeneratedRegex(@"\bfact\s?ids?\b|\bkind\s*:", RegexOptions.IgnoreCase)]
    private static partial Regex FormatWords();

    /// <summary>A pronoun that points back at an earlier sentence, anywhere in the sentence's first few words.</summary>
    [GeneratedRegex(@"^(?:\W*\w+\W+){0,3}?(?:it|its|they|them|their|this|these|those|that|he|she|his|her)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LeadingPronoun();

    /// <summary>The space after a sentence's end, so a split keeps each sentence's own punctuation.</summary>
    [GeneratedRegex(@"(?<=[.!?;])\s+")]
    private static partial Regex SentenceBreak();

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

    [GeneratedRegex(@"\b(?<n>second|third|fourth|fifth)[\s-]+(?:largest|biggest|highest|greatest|most)\b")]
    private static partial Regex OrdinalLargest();

    [GeneratedRegex(@"\b(?<n>second|third|fourth|fifth)[\s-]+(?:smallest|lowest|least|fewest)\b")]
    private static partial Regex OrdinalSmallest();

    /// <summary>A rank word and up to four words after it, up to its verb: where a status narrows the ranking.</summary>
    [GeneratedRegex(@"\b(?:largest|biggest|highest|greatest|most|top|main|smallest|lowest|least|fewest)\b(?<after>(?:\s+(?!(?:is|are|was|were)\b)[\w-]+){0,4})")]
    private static partial Regex RankPhrase();

    /// <summary>"The largest emitters", "the top suppliers": the sentence names several at the top.</summary>
    [GeneratedRegex(@"\b(?:largest|biggest|highest|greatest|top|main)\s+(?:\w+\s+)?(?:emitters|suppliers|sources|polluters)\b")]
    private static partial Regex PluralLargest();

    /// <summary>"The next largest": the one after the supplier an earlier sentence called largest.</summary>
    [GeneratedRegex(@"\bnext[\s-]+(?:largest|biggest|highest|greatest|most)\b")]
    private static partial Regex NextLargest();

    /// <summary>"The next smallest": relative too, and rare; not checked.</summary>
    [GeneratedRegex(@"\bnext[\s-]+(?:smallest|lowest|least|fewest)\b")]
    private static partial Regex NextSmallest();

    [GeneratedRegex(@"\b(?:(?<more>more|higher|larger|bigger|greater)|less|lower|smaller|fewer)\s+than\b")]
    private static partial Regex Comparison();

    [GeneratedRegex(@"\{\{\s*([A-Za-z][A-Za-z0-9]*(?:\.[A-Za-z][A-Za-z0-9]*)+)\s*\}\}")]
    private static partial Regex Placeholder();

    /// <summary>The digits a sentence may hold of its own: the unit (CO2, CO₂, CO2e, tCO2e) and the GHG scopes (Scope 1–3).</summary>
    [GeneratedRegex(@"\b(?:t\s?)?CO[2₂]e?\b|\bScope\s?[123]\b", RegexOptions.IgnoreCase)]
    private static partial Regex Unit();

    /// <summary>
    /// A claim about the buyer's own data in a sentence's own words: "your largest supplier…", "most of your emissions…",
    /// "your suppliers are estimated". Saying what the chat can tell ("who emits the most") is not one. Mirrored in
    /// <c>ai/app/chat.py</c> (DATA_CLAIM), so a model is told and asked again before the answer reaches the API.
    /// </summary>
    [GeneratedRegex(@"\byour\b[^.!?;]{0,40}\b(?:largest|biggest|highest|top|main|smallest|lowest|least)\b|\b(?:most|majority|half|all|none)\s+of\s+(?:your|it|them)\b|\byour\b[^.!?;]{0,60}\b(?:is|are)\s+(?:mostly\s+|still\s+|all\s+|now\s+)?(?:estimated|(?:supplier-)?confirmed|verified|partly\s+confirmed)\b")]
    private static partial Regex DataClaim();

    [GeneratedRegex(@"(?<![\p{L}])(?:S\.R\.L\.?|SRL|GmbH|Ltd\.?|Inc\.?|LLC|PLC|Corp\.?|S\.A\.)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffix();

    /// <summary>Short suffixes that are ordinary words in lower case, matched in capitals only.</summary>
    [GeneratedRegex(@"(?<![\p{L}])(?:SA|AG|BV|SAS|Co\.|Limited)(?![\p{L}])")]
    private static partial Regex UpperSuffix();
}

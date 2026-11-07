using System.Globalization;
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
        // The panel shows plain text: Markdown emphasis and list marks a model adds would show as asterisks.
        text = Plain(text);
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
        if (ActionPromise().IsMatch(free))
        {
            return Fail("an action the assistant cannot take");
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
        "an action the assistant cannot take" =>
            "it says the assistant will do or did something, but the assistant only reads the data; say where the buyer does it: the supplier's What if tab on the Suppliers screen, or Request data",
        "a change the data does not hold" =>
            "it says a change lowers something its fact shows rising, or the other way round; a negative change is less, a positive one is more",
        "a value repeated" => "it says the same value twice in one sentence; say each value once, and two deliveries from one supplier as two deliveries",
        "a change the supplier has no what-if for" =>
            "it suggests a transport change the supplier has no what-if for; suggest only changes its own W facts show, and a supplier with an {{Sn.whatIf}} fact has none",
        "a what-if given to another supplier" =>
            "it puts one supplier's what-if under another supplier's name; cite the what-if's own {{Wn.supplier}}, and a supplier with an {{Sn.whatIf}} fact has none",
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
        "a claim about the data without a fact" =>
            "it states something about the buyer's data (who emits the most, a status, a share or a count) without citing the fact that holds it; cite that fact, such as {{S1.name}}, {{list.estimated}} or {{count.deliveries}}, or leave the claim out",
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
        List<int> earlier = [];
        var about = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sentence in Sentences(segments))
        {
            // Who the sentence is about: the suppliers it names, else the ones the sentence before was about ("its").
            var named = NamedIn(sentence, pack);
            if (named.Count > 0)
            {
                about = named;
            }
            var words = string.Join(' ', sentence.Where(s => s.Text is not null).Select(s => s.Text)).ToLowerInvariant();
            if (!sentence.Any(s => s.FactId is not null) && ClaimsData(words))
            {
                return "a claim about the data without a fact";
            }
            // The same value twice in one sentence, by one fact or two ("estimated are {{list.estimated}}: {{S3.name}}"
            // with one supplier, "{{S1.status}} and {{S2.status}}"): it reads as a stutter, or two suppliers where one is.
            if (sentence.Where(s => s.FactId is not null).Select(s => pack.Find(s.FactId!)).OfType<Fact>()
                .GroupBy(f => (f.Kind, f.Value)).Any(g => g.Count() > 1))
            {
                return "a value repeated";
            }
            if (WrongDirection(sentence, pack))
            {
                return "a change the data does not hold";
            }
            if (WhatIfOfAnother(sentence, pack, about))
            {
                return "a what-if given to another supplier";
            }
            if (ModeNotOnOffer(pack, words, about))
            {
                return "a change the supplier has no what-if for";
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
            // "The larger of the two", "of the two, {{S4.name}} has the larger figure": the better ranked of the suppliers
            // this sentence and the one before cite, not S1 (Claude Sonnet 5.5 gave the smaller of two the larger figure).
            var among = ranks.Union(earlier).ToList();
            earlier = ranks;
            if (OfTheTwo().Match(words) is { Success: true } relative)
            {
                var expected = relative.Groups["less"].Success ? among.Max() : among.Min();
                if (!ranks.Contains(expected) || (ComparedSupplier(sentence) is { } compared && compared != expected))
                {
                    return "a comparison the data does not hold";
                }
                continue;
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

    private static readonly HashSet<string> DeltaFields = new(["carbonDelta", "costDelta", "leadDelta"], StringComparer.Ordinal);

    /// <summary>
    /// Whether a sentence says a change goes one way while its fact goes the other: "cuts emissions by {{W1.carbonDelta}}"
    /// with a positive change, "raises cost by {{C1.costDelta}}" with a negative one. A direction word ("would cut",
    /// "saves", "and adds") holds for the quantity it is about: the first one named after it (emissions, cost, lead time),
    /// else the first change fact. "and cost by" carries it over to another quantity; a quantity named without a direction
    /// word of its own (", with lead time {{W1.leadDelta}}", "though lead time changes by") and a neutral "change by" leave
    /// the change unread, since the signed value says which way it goes.
    /// </summary>
    private static bool WrongDirection(List<AnswerSegment> sentence, FactPack pack)
    {
        bool? less = null;
        string? about = null;
        foreach (var segment in sentence)
        {
            if (segment.Text is { } text)
            {
                var words = text.ToLowerInvariant();
                if (CarriedOver().Match(words) is { Success: true } carried)
                {
                    about = QuantityField(carried);
                    continue;
                }
                foreach (Match word in ChangeWord().Matches(words))
                {
                    if (QuantityField(word) is { } quantity)
                    {
                        if (about is not null && about != quantity)
                        {
                            less = null;
                        }
                        about = quantity;
                    }
                    else
                    {
                        less = word.Groups["neutral"].Success ? null : word.Groups["less"].Success;
                        about = null;
                    }
                }
                continue;
            }
            if (less is null || pack.Find(segment.FactId!) is not { } fact || !DeltaFields.Contains(fact.Field)
                || !decimal.TryParse(fact.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var change) || change == 0)
            {
                continue;
            }
            about ??= fact.Field;
            if (about == fact.Field && (less.Value ? change > 0 : change < 0))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The rank of the supplier a comparison among those just named (<see cref="OfTheTwo"/>) is said of: the last one cited
    /// before it in "{{S5.name}} is the larger of the two" and "it is the larger of the two", the next one in "the larger of
    /// the two is {{S4.name}}" and "of the two, {{S4.name}} has the larger figure". Null when the sentence does not say
    /// ("so the larger of the two is the one to ask first").
    /// </summary>
    private static int? ComparedSupplier(List<AnswerSegment> sentence)
    {
        int? last = null;
        for (var i = 0; i < sentence.Count; i++)
        {
            if (sentence[i].FactId is { } id)
            {
                last = FactPack.RankOf(id) ?? last;
                continue;
            }
            var text = sentence[i].Text!.ToLowerInvariant();
            if (OfTheTwoFirst().IsMatch(text))
            {
                return NextRank(sentence, i);
            }
            if (OfTheTwo().Match(text) is { Success: true } phrase)
            {
                return SaidOf().IsMatch(text[..phrase.Index]) ? last
                    : IsNext().IsMatch(text[(phrase.Index + phrase.Length)..]) ? NextRank(sentence, i)
                    : null;
            }
        }
        return null;
    }

    private static int? NextRank(List<AnswerSegment> sentence, int after) =>
        sentence.Skip(after + 1).Select(s => s.FactId is { } id ? FactPack.RankOf(id) : null).FirstOrDefault(r => r is not null);

    /// <summary>The change field a quantity word names: emissions → carbonDelta, cost → costDelta, lead time → leadDelta.</summary>
    private static string? QuantityField(Match word) =>
        word.Groups["carbon"].Success ? "carbonDelta"
        : word.Groups["cost"].Success ? "costDelta"
        : word.Groups["lead"].Success ? "leadDelta"
        : null;

    /// <summary>The suppliers a sentence names (by name fact), compared by name so two deliveries from one count as one.</summary>
    private static HashSet<string> NamedIn(List<AnswerSegment> sentence, FactPack pack) =>
        sentence.Where(s => s.FactId is not null).Select(s => pack.Find(s.FactId!)).OfType<Fact>()
            .Where(f => f.Field == "name" && f.Subject is not null)
            .Select(f => f.Subject!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a sentence with a what-if (W) or saved scenario (C) puts a supplier's figure beside a supplier it is not
    /// about. <paramref name="named"/> is who the sentence is about: the suppliers it names, or, when it names none ("its
    /// transport…"), those the sentence before named. Refused: "switching {{S2.name}} to rail cuts {{W1.carbonDelta}}"
    /// with W1 another supplier's, the same with "its" for {{S2.name}}, and "to lower {{S2.carbon}}, try {{W1.change}} on
    /// {{W1.supplier}}".
    /// </summary>
    private static bool WhatIfOfAnother(List<AnswerSegment> sentence, FactPack pack, HashSet<string> named)
    {
        var cited = sentence.Where(s => s.FactId is not null).Select(s => pack.Find(s.FactId!)).OfType<Fact>().ToList();
        if (named.Count == 0 || !cited.Any(f => WhatIfFact().IsMatch(f.Id) && f.Field != "name"))
        {
            return false;
        }
        return cited.Any(f => f.Field != "name" && f.Subject is { } whose && !named.Contains(whose));
    }

    /// <summary>
    /// Whether a sentence sends a supplier "to rail" (or sea, air, road) when none of the suppliers it is about has that
    /// what-if: "open Oradea Harness SRL and try switch to rail" for a supplier whose lane has road only. Only suppliers
    /// the simulator looked at are checked; for the others nothing is known.
    /// </summary>
    private static bool ModeNotOnOffer(FactPack pack, string words, HashSet<string> named)
    {
        var said = ModeTo().Matches(words).Select(m => m.Groups["mode"].Value).ToHashSet(StringComparer.Ordinal);
        var known = named.Where(pack.ModesOnOffer.ContainsKey).Select(n => pack.ModesOnOffer[n]).ToList();
        return said.Count > 0 && known.Count > 0 && said.Any(mode => !known.Any(offer => offer.Contains(mode)));
    }

    /// <summary>The text without Markdown emphasis, code marks or list bullets.</summary>
    private static string Plain(string text) => ListMark().Replace(Emphasis().Replace(text, "${t}"), "");

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

    /// <summary>A transport change in the sentence's own words: "to rail", "to sea", "to air", "to road".</summary>
    [GeneratedRegex(@"\bto\s+(?<mode>rail|sea|air|road)\b")]
    private static partial Regex ModeTo();

    [GeneratedRegex(@"^[WC]\d+\.")]
    private static partial Regex WhatIfFact();

    /// <summary>
    /// The words <see cref="WrongDirection"/> reads, in order: which way a change goes (less: cut, save, lower…; more:
    /// raise, add, increase…; neutral: change), and the quantity it is about (emissions, cost, lead time).
    /// </summary>
    [GeneratedRegex(@"\b(?:(?<less>cut|cuts|cutting|save|saves|saving|savings|reduce|reduces|reducing|reduction|lower|lowers|lowering|decrease|decreases|drop|drops|fall|falls|falling|less|fewer|cheaper|faster|shorter)|(?<neutral>change|changes|changed|changing)|(?<carbon>emissions?|carbon|co2e?|footprint)|(?<cost>costs?|prices?|spend|spending)|(?<lead>lead[\s-]?times?|delivery\s+times?|transit\s+times?)|raise|raises|raising|rise|rises|rising|increase|increases|increasing|grow|grows|growing|add|adds|adding|more|higher|costlier|pricier|slower|longer|extra)\b")]
    private static partial Regex ChangeWord();

    /// <summary>
    /// A comparison among the suppliers just named, not all of them: "the larger of the two", "the lowest of them", "of the
    /// two, … has the larger figure", "the most of the two". <c>less</c>: the smaller one.
    /// </summary>
    [GeneratedRegex(@"\b(?:(?<less>smaller|smallest|lower|lowest)|larger|largest|bigger|biggest|higher|highest|greater|greatest)\b(?:\s+\w+){0,2}?\s+of\s+(?:the\s+|these\s+|those\s+)?(?:two|both|them|these|those)\b|\bof\s+(?:the|these|those)\s+two\b[^.;]{0,80}?\b(?:(?<less>smaller|smallest|lower|lowest)|larger|largest|bigger|biggest|higher|highest|greater|greatest)\b|\bthe\s+(?:(?<less>least)|most)\s+of\s+(?:the\s+two|both)\b")]
    private static partial Regex OfTheTwo();

    /// <summary>"Of the two, " before the supplier a comparison is said of.</summary>
    [GeneratedRegex(@"\bof\s+(?:the|these|those)\s+two\s*,?\s*$")]
    private static partial Regex OfTheTwoFirst();

    /// <summary>A comparison said of the supplier before it: "… is the", "it was by far the".</summary>
    [GeneratedRegex(@"\b(?:is|was|['’]s)\s+(?:by\s+far\s+)?(?:the\s+)?$")]
    private static partial Regex SaidOf();

    /// <summary>A comparison said of the supplier after it: "the larger of the two is {{S4.name}}".</summary>
    [GeneratedRegex(@"^\s*(?:is|was|would\s+be)\s*$")]
    private static partial Regex IsNext();

    /// <summary>
    /// Text between two facts that at most names the next quantity, so the direction before it holds for the next change
    /// too: "cuts emissions by {{W1.carbonDelta}} and cost by {{W1.costDelta}}", ") and cost (", ", lead time by", "saves
    /// {{W1.costDelta}} and {{W1.carbonDelta}}", "cut {{S1.carbon}} by {{W3.carbonDelta}}".
    /// </summary>
    [GeneratedRegex(@"^[\s,;:()]*(?:(?:and|or|plus)\s+)?(?:(?:the|its|their)\s+)?(?:(?<carbon>emissions?|carbon|co2e?|footprint)|(?<cost>costs?|prices?|spend|spending)|(?<lead>lead[\s-]?times?|delivery\s+times?|transit\s+times?))?\s*(?:by|of)?[\s(]*$")]
    private static partial Regex CarriedOver();

    /// <summary>
    /// The assistant saying it will do, or did, what only the buyer can: "I'll add a what-if", "I've changed", "let me send".
    /// It only reads the data.
    /// </summary>
    [GeneratedRegex(@"\b(?:i['’]ll|i\s+will|i['’]ve|i\s+have|let\s+me|i['’]m\s+going\s+to|i\s+am\s+going\s+to)\s+(?:\w+\s+){0,2}?(?:add|added|create|created|change|changed|switch|switched|set|send|sent|save|saved|apply|applied|run|update|updated|make|made|request|requested|confirm|confirmed|delete|deleted|contact|contacted|email|emailed|order|ordered)\b", RegexOptions.IgnoreCase)]
    private static partial Regex ActionPromise();

    [GeneratedRegex(@"\*\*(?<t>[^*]+)\*\*|__(?<t>[^_]+)__|(?<![\w*])\*(?<t>[^*\s][^*]*?)\*(?![\w*])|`(?<t>[^`]+)`")]
    private static partial Regex Emphasis();

    [GeneratedRegex(@"(?m)^\s*(?:[-•*]|\d+[.)])\s+")]
    private static partial Regex ListMark();

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
    /// Whether a sentence that cites no fact claims something about the buyer's data in its own words: who emits the most
    /// (<see cref="RankClaim"/>), a status over several suppliers or deliveries (<see cref="StatusOfMany"/>), a count
    /// (<see cref="CountClaim"/>), or a share, size or status of "your" data (<see cref="DataClaim"/>). What the chat can
    /// tell ("I can tell you who emits the most", "try asking which supplier…") is taken out first (<see cref="CanTell"/>),
    /// and advice that names no one ("start with the largest emitters") is not a claim. Still a list of patterns: a claim
    /// in other words passes, which is why every value an answer shows is a cited fact. Mirrored in <c>ai/app/chat.py</c>
    /// (<c>claims_data</c>), so a model is told and asked again before the answer reaches the API.
    /// </summary>
    private static bool ClaimsData(string words)
    {
        var said = CanTell().Replace(words, " ");
        return DataClaim().IsMatch(said) || RankClaim().IsMatch(said) || StatusOfMany().IsMatch(said) || CountClaim().IsMatch(said);
    }

    /// <summary>
    /// A share, size or status of the buyer's own data: "your largest supplier…", "most of your emissions…", "most of the
    /// emissions…", "your suppliers are estimated", "the total is large", "your footprint is mostly…", "the main source of
    /// your emissions".
    /// </summary>
    [GeneratedRegex(@"\byour\b[^.!?;]{0,40}\b(?:largest|biggest|highest|top|main|smallest|lowest|least)\b|\b(?:most|majority|half|all|none|much|bulk)\s+of\s+(?:your|it|them|the\s+(?:emissions|total|carbon|footprint|spend|suppliers|deliveries|invoices))\b|\byour\b[^.!?;]{0,60}\b(?:is|are)\s+(?:mostly\s+|still\s+|all\s+|now\s+)?(?:estimated|(?:supplier-)?confirmed|verified|partly\s+confirmed)\b|\b(?:the|your)\s+(?:[\w-]+\s+)?(?:total|footprint|emissions|spend|costs?|carbon)\s+(?:is|are|was|were|comes?|came)\s+(?:\w+\s+)?(?:large|small|high|low|big|huge|tiny|mostly|mainly|largely|dominated|driven|concentrated)\b|\b(?:largest|biggest|highest|top|main|primary|smallest|lowest)\s+(?:[\w-]+\s+)?(?:sources?|emitters?|suppliers?|contributors?|drivers?|share|part)\s+of\s+your\b")]
    private static partial Regex DataClaim();

    /// <summary>
    /// Who emits the most or the least, said of someone: "the biggest emitter ships by air", "the board maker is the
    /// largest emitter", "it emits the most". "Start with the largest emitters" names no one and passes.
    /// </summary>
    [GeneratedRegex(@"\b(?:the|your)\s+(?:largest|biggest|highest|top|main|smallest|lowest)\s+(?:[\w-]+\s+)?(?:emitters?|suppliers?|polluters?|deliver(?:y|ies))\b(?:\s+[\w-]+){0,3}?\s+(?:is|are|was|were|has|have|ships?|emits?|uses?|comes?|sends?|accounts?|makes?|produces?|remains?|sits?)\b|\b(?:is|are|was|were|remains?)\s+(?:by\s+far\s+|still\s+)?(?:the|your)\s+(?:largest|biggest|highest|top|main|smallest|lowest)\s+(?:[\w-]+\s+)?(?:emitters?|suppliers?|polluters?)\b|\bemits?\s+(?:by\s+far\s+)?the\s+(?:most|least)\b")]
    private static partial Regex RankClaim();

    /// <summary>
    /// A status said of several suppliers or deliveries, or of all of them: "none of the suppliers has confirmed",
    /// "every delivery is supplier-confirmed", "two deliveries are still estimated", "the suppliers are mostly estimated".
    /// How TraceGreen works ("a supplier confirms a delivery in two taps", "each delivery starts as an estimate") passes.
    /// </summary>
    [GeneratedRegex(@"\b(?:none|all|every|most|many|some|few|half|several|both|neither|no|majority|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve)\b(?:\s+of)?(?:\s+(?:the|your|these|those|their))?(?:\s+[\w-]+){0,2}?\s+(?:suppliers?|deliver(?:y|ies)|invoices?|figures?|emitters?)\b[^.!?;]{0,40}?\b(?:is|are|was|were|has|have|had|remains?|stays?)\b[^.!?;]{0,30}?\b(?:estimated|estimates?|confirmed|verified|unconfirmed|spend-based)\b|\bthe\s+(?:[\w-]+\s+)?(?:suppliers|deliveries|invoices|figures|emitters)\b[^.!?;]{0,40}?\b(?:is|are|was|were|has|have|remain|stay)\b[^.!?;]{0,30}?\b(?:estimated|estimates?|confirmed|verified|unconfirmed|spend-based)\b")]
    private static partial Regex StatusOfMany();

    /// <summary>How many there are, in words: "you have four suppliers", "there is no what-if for it".</summary>
    [GeneratedRegex(@"\b(?:you\s+have|there\s+(?:are|is))\s+(?:only\s+|just\s+)?(?:no|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|several|many|few|some|a\s+few)\s+(?:[\w-]+\s+)?(?:suppliers?|deliver(?:y|ies)|invoices?|emitters?|scenarios?|what-ifs?)\b")]
    private static partial Regex CountClaim();

    /// <summary>
    /// What the chat can tell, up to the next comma or the sentence's end: "tell you who emits the most", "try asking
    /// which supplier…", "find out what a change would do". Not a claim, so <see cref="ClaimsData"/> reads past it.
    /// </summary>
    [GeneratedRegex(@"\b(?:tell|tells|show|shows|see|know|find|ask|asking|check|learn|explain|compare|say|says)\s+(?:out\s+)?(?:you\s+|me\s+)?(?:who|which|what|whether|where|how)\b[^,;:.!?]*")]
    private static partial Regex CanTell();

    [GeneratedRegex(@"(?<![\p{L}])(?:S\.R\.L\.?|SRL|GmbH|Ltd\.?|Inc\.?|LLC|PLC|Corp\.?|S\.A\.)(?![\p{L}])", RegexOptions.IgnoreCase)]
    private static partial Regex CompanySuffix();

    /// <summary>Short suffixes that are ordinary words in lower case, matched in capitals only.</summary>
    [GeneratedRegex(@"(?<![\p{L}])(?:SA|AG|BV|SAS|Co\.|Limited)(?![\p{L}])")]
    private static partial Regex UpperSuffix();
}

namespace TraceGreen.Domain.Model;

/// <summary>One numbered section of the supplier terms.</summary>
public sealed record TermsSection(int Number, string Heading, IReadOnlyList<string> Paragraphs);

/// <summary>
/// The statement next to the checkbox, in three parts so the page can make <see cref="Link"/> the link to the full terms:
/// <c>Before + Link + After</c> is the sentence the supplier accepts. <see cref="Before"/> or <see cref="After"/> may be empty.
/// </summary>
public sealed record TermsAcceptance(string Before, string Link, string After);

/// <summary>
/// The supplier terms in one language. <see cref="Acceptance"/> is the statement the supplier ticks before submitting;
/// <see cref="KeyPoints"/> open the printable page. Both are part of the hashed document.
/// </summary>
public sealed record TermsText(string Title, IReadOnlyList<string> KeyPoints, TermsAcceptance Acceptance, IReadOnlyList<TermsSection> Sections);

/// <summary>
/// The terms a supplier accepts before answering (<c>seed/supplier-terms.json</c>). <see cref="Sha256"/> is the SHA-256
/// (hex) of the file's canonical JSON (keys sorted, no whitespace), computed when the seed loads; a submit must send it
/// back, and the verification row and its audit entry record it with <see cref="Version"/>. A published version is never
/// edited: a change gets a new version, and the old text stays findable in git by its hash.
/// </summary>
public sealed record SupplierTerms(string Version, string Sha256, TermsText Ro, TermsText En);

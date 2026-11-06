namespace TraceGreen.Domain.Trust;

/// <summary>
/// The trust model's constants (F7, <c>docs/TRUST.md</c>). They are policy for a person to confirm, like
/// <c>AIR_HEAVY</c>'s 2 t, not factor data: changing one changes a score or a review flag, never an emission factor.
/// </summary>
public static class TrustPolicy
{
    /// <summary>Points for the evidence behind the claims that lower the figure.</summary>
    public const int EvidencePoints = 70;

    /// <summary>Points for how the answer arrived; signals take points off this part only.</summary>
    public const int ChannelPoints = 30;

    /// <summary>A contradicted claim caps the score here, whatever else backs the answer.</summary>
    public const int ContradictionCap = 25;

    public const int StrongFrom = 80;

    public const int ModerateFrom = 50;

    public const int SameNetworkPenalty = 20;

    public const int FastAnswerPenalty = 10;

    /// <summary>An answer this soon after the link was made is more likely the link's maker than the supplier.</summary>
    public const int FastAnswerSeconds = 120;

    public const int TaxIdPenalty = 10;

    public const int ModeChangedPenalty = 10;

    public const int PriorRejectionPenalty = 20;

    /// <summary>The demo tool answered, not a supplier: the channel part is gone.</summary>
    public const int SimulatedPenalty = ChannelPoints;

    /// <summary>
    /// A delivery received in under this share of the claimed mode's lead time contradicts the mode. The lanes' lead times
    /// are illustrative, so the margin is a factor of two.
    /// </summary>
    public const decimal TransitTooFastShare = 0.5m;

    /// <summary>A transit time between <see cref="TransitTooFastShare"/> and this multiple of a mode's lead time fits it.</summary>
    public const decimal TransitFitsUpTo = 2m;

    /// <summary>A confirmed mass above this multiple of the invoice's net mass is flagged; it raises the figure, so it is not a contradiction.</summary>
    public const decimal MassFarAboveFactor = 2m;

    /// <summary>The share of a claim's weight each level earns in the evidence part.</summary>
    public static decimal Credit(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Contradicted => 0m,
        EvidenceLevel.Unproven => 0.25m,
        EvidenceLevel.Documented => 0.7m,
        EvidenceLevel.Corroborated or EvidenceLevel.Reviewed or EvidenceLevel.NotNeeded => 1m,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, null),
    };

    /// <summary>A reduction counts in the figure from this level up; below it the figure keeps the claim's fallback.</summary>
    public static bool Counts(EvidenceLevel level) => level is EvidenceLevel.Documented or EvidenceLevel.Corroborated or EvidenceLevel.Reviewed or EvidenceLevel.NotNeeded;
}

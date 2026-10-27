using System.Globalization;
using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Plausibility;

/// <summary>
/// Checks a supplier's answers for combinations worth a second look. Warnings never block a verification (D2): the phone
/// shows a "please reconfirm" banner and the dashboard marks the node. Only confirmed answers are checked, so a node still
/// on its lane's default mode carries no warning. <c>SEA_TOO_FAST</c> waits for dispatch and delivery dates (D3).
/// </summary>
public static class PlausibilityRules
{
    /// <summary><c>AIR_HEAVY</c> fires above this mass by air. The demo invoice N1 is 2.3 t, so it fires there on purpose.</summary>
    public const decimal AirHeavyThresholdT = 2m;

    public static IReadOnlyList<PlausibilityWarning> Check(TransportMode? confirmedMode, decimal? massT)
    {
        var warnings = new List<PlausibilityWarning>();
        if (confirmedMode == TransportMode.Air && massT > AirHeavyThresholdT)
        {
            warnings.Add(new PlausibilityWarning(
                PlausibilityCode.AirHeavy,
                string.Create(CultureInfo.InvariantCulture, $"Air freight confirmed for {massT:0.###} t; more than {AirHeavyThresholdT} t by air is unusual.")));
        }
        return warnings;
    }
}

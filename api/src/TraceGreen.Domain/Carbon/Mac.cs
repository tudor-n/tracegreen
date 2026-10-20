namespace TraceGreen.Domain.Carbon;

/// <summary>Marginal abatement cost in euro per tonne of CO2e saved. Division is guarded: no saving, no ratio.</summary>
public static class Mac
{
    public static MacResult Compute(decimal costOldEur, decimal costNewEur, decimal carbonOldKg, decimal carbonNewKg)
    {
        var deltaCost = costNewEur - costOldEur;
        var deltaCarbonT = (carbonOldKg - carbonNewKg) / 1000m;
        if (deltaCarbonT <= 0m)
        {
            return new MacResult(MacKind.NotAbatement, null, deltaCost, deltaCarbonT);
        }
        var mac = Math.Round(deltaCost / deltaCarbonT, 2, MidpointRounding.AwayFromZero);
        return new MacResult(MacKind.Abatement, mac, deltaCost, deltaCarbonT);
    }

    public static MacBadge Badge(MacResult result, decimal etsPriceEurPerT)
    {
        if (result.Kind == MacKind.NotAbatement || result.MacEurPerT is not { } mac)
        {
            return MacBadge.NotAbatement;
        }
        if (mac < 0m)
        {
            return MacBadge.WinWin;
        }
        return mac < etsPriceEurPerT ? MacBadge.BelowEts : MacBadge.Premium;
    }
}

using TraceGreen.Domain.Model;

namespace TraceGreen.Domain.Carbon;

public static class KpiCalculator
{
    /// <summary>Primary-data share is by emissions (verified kg over total kg), not by node count.</summary>
    public static Kpis Compute(IEnumerable<SupplierNode> nodes)
    {
        decimal totalKg = 0m, verifiedKg = 0m, spendEur = 0m;
        var nodeCount = 0;
        var verifiedCount = 0;
        foreach (var node in nodes)
        {
            nodeCount++;
            totalKg += node.CarbonKg;
            spendEur += node.SpendEur;
            if (node.Status == NodeStatus.Verified)
            {
                verifiedKg += node.CarbonKg;
                verifiedCount++;
            }
        }

        var primaryShare = totalKg > 0 ? (double)(verifiedKg / totalKg) : 0d;
        var estimatedShare = totalKg > 0 ? 1d - primaryShare : 0d;
        return new Kpis(totalKg, primaryShare, estimatedShare, spendEur, nodeCount, verifiedCount);
    }
}

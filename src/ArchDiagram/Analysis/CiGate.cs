namespace ArchDiagram.Analysis;

/// <summary>CI-mode quality gates: maps a short, stable flag name to the scorecard row it
/// reads (see <see cref="ScorecardBuilder"/>). The scorecard already grades every signal
/// Ok/Watch/Fail; this just turns a Fail into a reason string for a non-zero exit. Pre-comply
/// validated: every gate name maps to a real metric, checked once at startup.</summary>
public static class CiGate
{
    /// <summary>gate name -&gt; the ScorecardBuilder.Row.Metric it reads. "scorecard" is
    /// special-cased in <see cref="Evaluate"/> (checks the overall grade, not one row).</summary>
    public static readonly Dictionary<string, string> KnownGates = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cycles"] = "Dependency cycles",
        ["layering"] = "Layering violations",
        ["secrets"] = "Credentials in source",
        ["drift"] = "Package version drift",
        ["scorecard"] = "",
    };

    /// <summary>Every tripped gate, as a human-readable reason (empty when all pass).
    /// Assumes every entry in <paramref name="gates"/> is already a known gate name — the
    /// CLI validates that at parse time so this never silently ignores a typo.</summary>
    public static List<string> Evaluate(IReadOnlyList<string> gates, ScorecardBuilder.Card card)
    {
        var failed = new List<string>();
        var byMetric = card.Rows.ToDictionary(r => r.Metric, StringComparer.Ordinal);
        foreach (var gate in gates)
        {
            if (string.Equals(gate, "scorecard", StringComparison.OrdinalIgnoreCase))
            {
                if (card.Overall == ScorecardBuilder.Status.Fail) { failed.Add("scorecard: overall grade is AT RISK"); }
                continue;
            }
            if (!KnownGates.TryGetValue(gate, out var metric)) { continue; }
            if (byMetric.TryGetValue(metric, out var row) && row.Status == ScorecardBuilder.Status.Fail)
            {
                failed.Add($"{gate}: {row.Metric} = {row.Value} — {row.Note}");
            }
        }
        return failed;
    }
}

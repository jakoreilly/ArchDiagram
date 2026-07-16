using System.Text.Json;

namespace ArchDiagram.Site;

/// <summary>Single source of truth for the plain-language + technical explanations behind the
/// ⓘ "explain" affordances. Each term has a <c>Simple</c> line (for a newcomer) and a
/// <c>Detail</c> expansion (formula / architectural nuance). Emitted once per page as JSON and
/// rendered on demand by site.js, so the wording stays consistent everywhere.</summary>
public static class Glossary
{
    public sealed record Entry(string Simple, string Detail);

    public static readonly IReadOnlyDictionary<string, Entry> Terms = new Dictionary<string, Entry>(StringComparer.Ordinal)
    {
        ["ca"] = new("How many other modules depend on this one.",
            "Afferent coupling (Ca): the count of distinct modules that reference this module. High Ca means many things break if you change it — it is load-bearing."),
        ["ce"] = new("How many other modules this one depends on.",
            "Efferent coupling (Ce): the count of distinct modules this module references. High Ce means it knows about a lot and is sensitive to changes elsewhere."),
        ["instability"] = new("How likely this module is to change because something it depends on changed. 0 = rock-solid, 1 = easily shaken.",
            "Instability I = Ce / (Ca + Ce). 0 = maximally stable (many depend on it, it depends on nothing); 1 = maximally unstable (depends outward, nothing depends on it). Stable modules should change rarely."),
        ["abstractness"] = new("What share of this module is interfaces/abstract types rather than concrete code.",
            "Abstractness A = abstract types ÷ total types. Robert C. Martin's principle: modules that are depended on a lot (stable) should be abstract, so dependents rely on contracts, not concretes."),
        ["distance"] = new("How far this module is from a healthy balance of abstract-vs-depended-on. Lower is healthier.",
            "Distance from the main sequence D = |A + I − 1|. The main sequence (A + I = 1) is the ideal line. D near 0 is healthy; near 1 means either rigid-and-concrete (zone of pain) or abstract-and-unused (zone of uselessness)."),
        ["zone-of-pain"] = new("Concrete code that lots of things depend on — hard to change safely.",
            "Low abstractness + low instability. Heavily depended-on and concrete, so changes ripple widely and there are no contracts to depend on instead. Introduce interfaces, or accept it for stable leaf types."),
        ["zone-of-uselessness"] = new("Abstract code that almost nothing uses.",
            "High abstractness + high instability. Lots of abstractions no one depends on — usually dead or speculative. Delete or give it concrete work."),
        ["propagation-cost"] = new("How much of the system a typical change can reach.",
            "The density of the transitive dependency matrix: the average fraction of modules reachable from any module by following dependencies. Lower means changes stay local; high means tight coupling."),
        ["cycles"] = new("Groups of modules that depend on each other in a loop.",
            "Strongly-connected module groups (A → B → A). Cyclic modules can't be built, tested or understood in isolation — a prime refactor target. Break by inverting one edge behind an interface."),
        ["fan-in"] = new("How many files import this file.",
            "Incoming internal dependencies. High fan-in files are risky to change because many others rely on them — change them carefully and keep them well-tested."),
        ["fan-out"] = new("How many files this file imports.",
            "Outgoing internal dependencies. High fan-out files 'know too much' and are sensitive to changes across the codebase — candidates for decomposition."),
        ["cyclomatic"] = new("How many independent paths run through a method — a rough size of its logic.",
            "Cyclomatic complexity = 1 + number of decision points (if/for/while/case/&&/||). Higher means more test cases are needed to cover it."),
        ["cognitive"] = new("How hard a method is for a human to follow.",
            "SonarSource cognitive complexity: structural increments for control flow plus extra penalties for nesting. Unlike cyclomatic, it models readability, so deeply-nested code scores worse."),
        ["test-code-ratio"] = new("How much of the code is tests — a rough sign of how well it's exercised.",
            "Test lines ÷ (first-party + test lines). A proxy, not execution coverage: a high ratio doesn't guarantee good tests, and a low one is a prompt to add them. Measure real coverage with a coverage tool."),
        ["version-drift"] = new("The same package pulled at different versions in different projects.",
            "Two or more projects reference one NuGet package at differing versions. This can load duplicate assemblies and cause subtle conflicts; align on one version or use Central Package Management."),
        ["critical-path"] = new("The chain of files you pass through to reach an important file.",
            "The shortest sequence of imports from an entry point (a file nothing else imports) to a key file. Reading it left-to-right shows how the code reaches that file."),
        ["layering"] = new("Whether dependencies flow one way, top layer down to foundation.",
            "In a layered architecture the top (UI/API) may depend on lower layers (domain/core) but never the reverse. An 'upward' dependency breaks the contract and couples the foundation to detail above it."),
    };

    public static string Json() =>
        JsonSerializer.Serialize(Terms.ToDictionary(kv => kv.Key, kv => new { simple = kv.Value.Simple, detail = kv.Value.Detail }));

    /// <summary>An inline ⓘ affordance for <paramref name="term"/>; empty if the term is unknown
    /// (so a typo degrades to nothing rather than a dead button).</summary>
    public static string Info(string term) =>
        Terms.ContainsKey(term)
            ? $"<button class=\"explain\" type=\"button\" data-term=\"{Html.Encode(term)}\" aria-label=\"Explain this term\">&#9432;</button>"
            : "";
}

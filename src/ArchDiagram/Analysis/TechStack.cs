using ArchDiagram.Graph;

namespace ArchDiagram.Analysis;

/// <summary>Recognises frameworks/libraries from NuGet package names and external import targets,
/// mapping each to a human category (Web, Data access, Messaging, …). Lets an architect see the
/// stack and the system's integration points at a glance instead of reading a raw package list.
/// Pure; unknown packages are simply not classified (never guessed).</summary>
public static class TechStack
{
    public sealed record Tech(string Name, string Category, IReadOnlyList<string> UsedBy);

    // Category names that represent an outward integration point (DB, broker, cloud, remote I/O).
    public static readonly string[] IntegrationCategories =
        ["Data access", "Messaging", "Caching", "Cloud", "RPC", "Realtime", "Web / API"];

    // Longest-prefix-ish rules; first match wins per package (rules ordered specific → general).
    private static readonly (string Pattern, string Name, string Category)[] Rules =
    [
        ("microsoft.aspnetcore", "ASP.NET Core", "Web / API"),
        ("microsoft.azure.functions", "Azure Functions", "Web / API"),
        ("swashbuckle", "Swagger / OpenAPI", "Web / API"),
        ("nswag", "NSwag", "Web / API"),
        ("microsoft.entityframeworkcore", "EF Core", "Data access"),
        ("microsoft.entityframework", "Entity Framework", "Data access"),
        ("dapper", "Dapper", "Data access"),
        ("microsoft.data.sqlclient", "SQL Server client", "Data access"),
        ("system.data.sqlclient", "SQL Server client", "Data access"),
        ("npgsql", "Npgsql (PostgreSQL)", "Data access"),
        ("mysql", "MySQL client", "Data access"),
        ("oracle.manageddataaccess", "Oracle client", "Data access"),
        ("mongodb", "MongoDB", "Data access"),
        ("stackexchange.redis", "Redis", "Caching"),
        ("microsoft.extensions.caching", "MS caching", "Caching"),
        ("masstransit", "MassTransit", "Messaging"),
        ("rabbitmq", "RabbitMQ", "Messaging"),
        ("azure.messaging.servicebus", "Azure Service Bus", "Messaging"),
        ("confluent.kafka", "Kafka", "Messaging"),
        ("nservicebus", "NServiceBus", "Messaging"),
        ("mediatr", "MediatR", "Mediator / CQRS"),
        ("grpc", "gRPC", "RPC"),
        ("microsoft.aspnetcore.signalr", "SignalR", "Realtime"),
        ("azure.storage", "Azure Storage", "Cloud"),
        ("azure.identity", "Azure Identity", "Cloud"),
        ("awssdk", "AWS SDK", "Cloud"),
        ("google.cloud", "Google Cloud", "Cloud"),
        ("serilog", "Serilog", "Logging"),
        ("nlog", "NLog", "Logging"),
        ("microsoft.extensions.logging", "MS logging", "Logging"),
        ("newtonsoft.json", "Newtonsoft.Json", "Serialization"),
        ("system.text.json", "System.Text.Json", "Serialization"),
        ("automapper", "AutoMapper", "Mapping"),
        ("fluentvalidation", "FluentValidation", "Validation"),
        ("polly", "Polly", "Resilience"),
        ("hangfire", "Hangfire", "Background jobs"),
        ("quartz", "Quartz", "Background jobs"),
        ("autofac", "Autofac", "DI / hosting"),
        ("microsoft.extensions.dependencyinjection", "MS DI", "DI / hosting"),
        ("microsoft.extensions.hosting", "Generic Host", "DI / hosting"),
        ("xunit", "xUnit", "Testing"),
        ("nunit", "NUnit", "Testing"),
        ("mstest", "MSTest", "Testing"),
        ("microsoft.net.test.sdk", "Test SDK", "Testing"),
        ("fluentassertions", "FluentAssertions", "Testing"),
        ("moq", "Moq", "Testing"),
        ("nsubstitute", "NSubstitute", "Testing"),
        ("coverlet", "Coverlet (coverage)", "Testing"),
        ("microsoft.codeanalysis", "Roslyn", "Code analysis"),
    ];

    public static IReadOnlyList<Tech> Detect(ProjectModel model)
    {
        // package name -> projects using it (case-insensitive).
        var pkgToProjects = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in model.Projects)
        {
            foreach (var pkg in p.PackageReferences)
            {
                (pkgToProjects.TryGetValue(pkg, out var set) ? set : pkgToProjects[pkg] = new(StringComparer.Ordinal)).Add(p.Name);
            }
        }
        // Also fold in external import targets (covers non-.csproj languages), attributed to "(imports)".
        foreach (var e in model.FileDependencies.Where(e => e.ExternalTarget.Length > 0))
        {
            (pkgToProjects.TryGetValue(e.ExternalTarget, out var set) ? set : pkgToProjects[e.ExternalTarget] = new(StringComparer.Ordinal)).Add("(imports)");
        }

        var byTech = new Dictionary<string, (string Category, SortedSet<string> Projects)>(StringComparer.Ordinal);
        foreach (var (pkg, projects) in pkgToProjects)
        {
            var lower = pkg.ToLowerInvariant();
            var rule = Rules.FirstOrDefault(r => lower.StartsWith(r.Pattern, StringComparison.Ordinal));
            if (rule.Name is null) { continue; }
            if (!byTech.TryGetValue(rule.Name, out var agg)) { byTech[rule.Name] = agg = (rule.Category, new(StringComparer.Ordinal)); }
            foreach (var pr in projects) { agg.Projects.Add(pr); }
        }

        return byTech
            .Select(kv => new Tech(kv.Key, kv.Value.Category, kv.Value.Projects.ToList()))
            .OrderBy(t => t.Category, StringComparer.Ordinal).ThenBy(t => t.Name, StringComparer.Ordinal)
            .ToList();
    }
}

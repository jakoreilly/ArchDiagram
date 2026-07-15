namespace ArchDiagram.Graph;

/// <summary>Root of everything ArchDiagram learned about a scanned folder.
/// Serialized verbatim to model.json so other tools can reuse the analysis.</summary>
public sealed record ProjectModel
{
    public required string RootName { get; init; }
    public required string SourcePath { get; init; }
    public List<FileNode> Files { get; init; } = [];
    public List<CsprojInfo> Projects { get; init; } = [];
    public List<DbNode> Databases { get; init; } = [];
    public List<DepEdge> FileDependencies { get; init; } = [];
    public List<CallEdge> Calls { get; init; } = [];
    public List<string> Diagnostics { get; init; } = [];
    public Dictionary<string, int> LanguageLoc { get; init; } = [];

    /// <summary>How to link nodes back to source; null = no source configured.</summary>
    public SourceLink? SourceLink { get; init; }
}

/// <summary>One source/config file in the scanned tree.</summary>
public sealed record FileNode
{
    public required string RelPath { get; init; }
    public required string Slug { get; init; }
    public required string Language { get; init; }
    public long SizeBytes { get; init; }
    public int Loc { get; init; }
    public string Purpose { get; set; } = "";
    public string PurposeSource { get; set; } = "";
    public List<string> Imports { get; init; } = [];
    public List<TypeInfo> Types { get; init; } = [];
    public List<TodoItem> Todos { get; init; } = [];
}

/// <summary>A TODO/FIXME/HACK/BUG/XXX marker found in a source comment.</summary>
public sealed record TodoItem(int Line, string Tag, string Text);

public sealed record TypeInfo
{
    public required string Name { get; init; }
    public required string Kind { get; init; }
    public string Namespace { get; init; } = "";
    public string Modifiers { get; init; } = "";
    public List<string> BaseTypes { get; init; } = [];
    public string XmlSummary { get; init; } = "";
    public List<MethodInfo> Methods { get; init; } = [];
}

public sealed record MethodInfo
{
    public required string Name { get; init; }
    public int Arity { get; init; }
    public string Signature { get; init; } = "";
    public string XmlSummary { get; init; } = "";
    /// <summary>Cyclomatic complexity: 1 + count of decision points (see ComplexityMetrics).</summary>
    public int Cyclomatic { get; init; }
    /// <summary>SonarSource cognitive complexity: structural + nesting increments.</summary>
    public int Cognitive { get; init; }
    /// <summary>1-based first line of the declaration in its source file (0 = unknown).</summary>
    public int StartLine { get; init; }
    /// <summary>1-based last line of the declaration in its source file (0 = unknown).</summary>
    public int EndLine { get; init; }
    public List<InvocationRef> Invocations { get; init; } = [];
}

/// <summary>A call site inside a method body: the invoked identifier + argument count.</summary>
public sealed record InvocationRef(string Name, int Arity);

/// <summary>File-to-file (or file-to-external) dependency discovered from imports.</summary>
public sealed record DepEdge
{
    public required string FromSlug { get; init; }
    /// <summary>Slug of a scanned file, or empty when the target is external.</summary>
    public string ToSlug { get; init; } = "";
    /// <summary>Package/module name when the import did not resolve to a scanned file.</summary>
    public string ExternalTarget { get; init; } = "";
}

/// <summary>Heuristic method call edge (name + arity matching; see CallGraphBuilder).</summary>
public sealed record CallEdge
{
    public required string CallerSlug { get; init; }
    public required string CallerType { get; init; }
    public required string CallerMethod { get; init; }
    public required string CalleeSlug { get; init; }
    public required string CalleeType { get; init; }
    public required string CalleeMethod { get; init; }
    public bool Ambiguous { get; init; }
}

public sealed record CsprojInfo
{
    public required string Name { get; init; }
    public required string RelPath { get; init; }
    public string TargetFramework { get; init; } = "";
    public List<string> ProjectReferenceNames { get; init; } = [];
    public List<string> PackageReferences { get; init; } = [];
    public List<DbUse> ConnectionStrings { get; init; } = [];
}

/// <summary>A connection-string usage inside a project. Label is always
/// human-readable (catalog > variable name > short hash); the full hash lives
/// here for tooltips/metadata only.</summary>
public sealed record DbUse
{
    public required string Hash { get; init; }
    public required string Label { get; init; }
    public string Server { get; init; } = "";
    public string Catalog { get; init; } = "";
    public string VariableName { get; init; } = "";
    public string Evidence { get; init; } = "";
}

/// <summary>One logical database node (deduped across projects by hash).</summary>
public sealed record DbNode
{
    public required string Hash { get; init; }
    public required string Label { get; init; }
    public string Server { get; init; } = "";
    public string Catalog { get; init; } = "";
}

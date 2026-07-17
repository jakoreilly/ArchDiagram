// ArchDiagram — point it at any project folder, get a fully-offline static HTML
// documentation site: architecture overview, folder structure, file dependency
// graphs, C# types + heuristic call graphs, and a drill-down page per file.
// All diagrams pan/zoom and export to PNG. Nothing is fetched at runtime.
//
// Usage: archdiagram <path-to-project> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]...
using ArchDiagram.Cli;

if (args.Length > 0 && args[0] == "--landscape") { return Verbs.RunLandscape(args); }
if (args.Length > 0 && args[0] == "--diff") { return Verbs.RunDiff(args); }
if (args.Length > 0 && args[0] == "--from-model") { return Verbs.RunFromModel(args); }
return Verbs.RunDefault(args);

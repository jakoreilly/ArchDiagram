# ArchDiagram

> Turn any codebase into a beautiful, searchable architecture website.

ArchDiagram is a .NET 10 command-line tool that turns an existing codebase into a fully offline, searchable architecture website. It helps teams understand a project quickly by generating dependency maps, folder structure views, type listings, call graphs, and file-by-file insights without requiring a build.

It is designed for software architects, maintainers, and developers who need to explore unfamiliar repositories, document legacy systems, or share architecture context with colleagues.

## Why this project matters

- Analyze a folder or repository without compiling it first
- Generate a polished, offline HTML site that can be shared anywhere
- Surface structure, dependencies, hotspots, and TODO markers in one place
- Produce Mermaid-compatible architecture views for docs, wikis, and design reviews

## Quick start

1. Edit [archdiagram.config.json](archdiagram.config.json) and set the source path you want to analyze.
2. Run [ArchDiagram.cmd](ArchDiagram.cmd) on Windows, or use the CLI below.
3. Open the generated site in your browser.

### CLI

```bash
dotnet run --project src/ArchDiagram -- <path-to-project> [--out <dir>] [--no-open] [--max-nodes <n>] [--exclude <dirname>]... [--source-link-type <github|gitlab|local>] [--source-link-base <url>] [--source-link-ref <branch>]
```

Requires the .NET 10 SDK. Git is only needed when analyzing a remote repository reference.

When `--out` is omitted, the site is written to `site-<project>` (named after the scanned folder), so reports for different projects can sit side by side without overwriting each other.

## Modes

ArchDiagram supports three ways to run, all driven by [archdiagram.config.json](archdiagram.config.json):

### 1. Single project (standalone docs)
Set `ProjectPath` (or `GitLabUrl`) and run `ArchDiagram.cmd`. One site is generated at `site-<project>`. This is the default mode and is unchanged.

### 2. Landscape of everything already generated
Set `"Landscape": true`. No source is scanned; instead every already-generated `site-*/model.json` in the launcher folder is cross-referenced into a parent viewer at `site-landscape`.

### 3. Groups (batch update + per-group landscapes)
Set a non-empty `Groups` array. In **one** run the launcher scans every project in each group into its own `site-<project>` folder, then builds **one landscape per group** at `site-landscape-<group>`. Set `"OverallLandscape": true` to also build a combined landscape of every project across all groups at `site-landscape`. When `Groups` is non-empty it takes precedence over `ProjectPath`/`GitLabUrl`/`Landscape`.

A `Projects` entry is either a local path string or an object `{ "GitLabUrl": "…", "GitRef": "…" }`, so cloned masters and remote repos can be mixed:

```json
"Groups": [
  {
    "Name": "Shopping",
    "Projects": [
      "C:\\repos\\my-shop-master",
      { "GitLabUrl": "https://gitlab.example.com/team/myshop.git", "GitRef": "main" }
    ]
  },
  { "Name": "Cart", "Projects": ["C:\\repos\\shopping-cart", "C:\\repos\\shopping-cart-main"] }
],
"OverallLandscape": true
```

Because group mode is config-driven and idempotent, **re-running `ArchDiagram.cmd` periodically** on your cloned masters rebuilds every site and landscape so the documentation stays up to date. Each landscape's interconnections diagram has layer filters (cross-service calls / shared packages / package links), a minimum-calls threshold, and call edges weighted by volume so large systems stay readable.

## What gets generated

- An architecture overview with stats, diagnostics, and diagrams
- A collapsible folder tree and file inventory
- File-to-file dependency graphs
- An interactive **3D dependency graph** (WebGL) with click-to-focus "unfold"
- Type and member listings for C# projects
- Method call graphs and coupling hotspot analysis
- Per-file detail pages with purpose summaries and TODO markers
- Optional **source links** back to GitHub / GitLab / local files

## 3D dependency graph

The **Graph (3D)** page renders every file as a node in a WebGL force-directed
graph (offline, powered by a vendored `3d-force-graph` bundle — no network).
**Click any node to focus it**: the camera flies in and its neighbours (up to the
selected number of hops) unfold around it while the rest of the graph fades back.

It encodes five *data* channels — not five geometric dimensions (a screen is three
at most):

| Channel | Meaning |
|---|---|
| Position (X/Y/Z) | Force layout from the dependency edges |
| Node colour | Top-level folder |
| Node size | Coupling (fan-in + fan-out) |
| Edge colour | Blue = import/reference · amber = heuristic call |
| Focus animation | Distance (hops) from the clicked node |

Requires WebGL; where it is unavailable the page degrades to a message pointing at
the 2D Dependencies page. The graph data is embedded inline so it works from
`file://`, and is also written to `graph.json` for external tooling.

## Source links

Set `SourceLinkType` (`github` | `gitlab` | `local`), `SourceLinkBase`
(repo web URL or local root) and `SourceLinkRef` (branch/tag) in
[archdiagram.config.json](archdiagram.config.json) — or the CLI flags
`--source-link-type/-base/-ref` — to add "Open source ↗" links on file pages
(down to the method line for GitHub/GitLab) and in the 3D graph's node panel.
If no source is baked in, the viewer offers a one-time in-browser prompt and
remembers the answer in that browser (`localStorage`).

## Features

- Fully offline browsing with no runtime data fetches
- Fast local search across files, types, and methods
- Interactive diagrams with zoom, pan, fit-to-view, and export to PNG/SVG
- Light and dark themes
- Mermaid output that can be copied into documentation

## How it works

ArchDiagram performs a syntax-aware scan of your codebase and produces a static documentation site. It supports:

- Tier 1 analysis for any language: inventory, imports, and basic dependency detection
- Tier 2 C# analysis using Roslyn syntax parsing for types, members, and signatures
- Database connection-string detection and normalization
- Heuristic purpose summaries for files and types

## Development

```bash
dotnet build src/ArchDiagram
dotnet test tests/ArchDiagram.Tests
```

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

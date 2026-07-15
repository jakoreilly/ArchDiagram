# ArchDiagram

ArchDiagram is a .NET 10 command-line tool that turns an existing codebase into a fully offline, searchable architecture website. It helps teams understand a project quickly by generating diagrams, dependency maps, type listings, and file-by-file insights without requiring a build.

It is designed for software architects, maintainers, and developers who need to explore unfamiliar repositories, document legacy systems, or share architecture context with colleagues.

## Why it exists

- Analyze a folder or repository without compiling the project
- Generate a polished, offline HTML site that can be shared anywhere
- Surface structure, dependencies, hotspots, and TODO markers in one place
- Produce Mermaid-compatible architecture views that can be reused in docs or wikis

## What the generated site includes

- **Overview** — a generated plain-language summary, a "Start here" ranking of the most
  central files to read first, stat tiles, language breakdown, and the architecture diagram
- **Structure** — a colour-by-language, sized-by-LOC treemap plus the full folder/file tree
  with per-folder "what lives here" blurbs
- **Modules** — files grouped into modules (namespace or folder) with a module dependency
  diagram and a coupling matrix (the mid-level view)
- **Metrics** — quantitative architecture health per module: coupling (Ca/Ce), Instability,
  Abstractness, Distance from the main sequence (with a scatter plot), propagation cost, and
  dependency-cycle detection
- **Dependencies, Types & Members (with a type-hierarchy diagram), Call Graph, Hotspots**
  (coupling, complexity, unreferenced files, TODO/FIXME with author/ticket attribution)
- A page per file (owning project, connections, imports, complexity), plus `model.json`,
  `graph.json`, `ARCHITECTURE.md` and an offline wiki export

Test files are detected and hidden by default across the busy displays (a 🧪 toggle in the
sidebar reveals them). You can supply an optional `archdiagram.descriptions.json` sidecar to
add an authored project overview and per-file/per-folder descriptions that override the
heuristic text — see `DescriptionsFile` in `archdiagram.config.json`.

## Ideal use cases

- Legacy codebase onboarding
- Architecture documentation for internal or external projects
- Repository exploration before refactoring
- Sharing technical context without requiring a live web app

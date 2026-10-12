# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Naiad is a .NET library that renders Mermaid diagram markup to SVG without requiring a browser or JavaScript runtime. Built on .NET 10.0 with C# latest, using Parlot parser combinators for input parsing.

## Build & Test Commands

```bash
# Build
dotnet build src --configuration Release

# Run all tests
dotnet test src --configuration Release

# Run a single test (TUnit uses Microsoft.Testing.Platform; filter goes after `--`)
dotnet test src --configuration Release -- --treenode-filter "/*/*/PieTests/Simple"

# Regenerate all .verified.png baselines from the .verified.svg files (after a rendering change).
# PngRegenerator is [Explicit]; targeting it by filter runs it.
dotnet test src --configuration Release -- --treenode-filter "/*/*/PngRegenerator/*"
```

## Architecture

**Pipeline:** Each diagram type follows a three-stage pipeline: **Parser → Model → Renderer**

- **Parser** (`IDiagramParser<TModel>`): Converts Mermaid text to a model using Parlot's fluent parser combinators (`Parlot.Fluent`). A rule is a chain of `.And()` / `.SkipAnd()` / `.AndSkip()` ending in `.Map(...)` or `.Then(...)`; `Literals` only, never the whitespace-skipping `Terms`, since the grammars are line-oriented. Shared parser building blocks live in `CommonParsers`, and what Parlot lacks (`Many`, `Map`, `AsString`, `Assert`, `LineBefore`, `TextIgnoreCase`) in `GrammarExtensions`. Case-insensitive keywords use `TextIgnoreCase("x")`, not `Literals.Text("x", caseInsensitive: true)`: up to at least Parlot 1.5.11 the latter picks its lookup characters from the culture the grammar is built under, and misses `I` for `i` under a Turkish one. Parlot always backtracks and reports nothing about a failure by itself, so `CommonParsers.LineEnd` / `Newline` / `End` and `.Many()` note how far they got in `DiagramParseContext`, which is where a parse error's line and column come from. Use `.Many()` rather than Parlot's `ZeroOrMany`: it also stops on a rule that matches without consuming anything instead of looping forever.
- **Model** (`DiagramBase`): Domain objects. `GraphDiagramBase` extends this for node/edge diagrams (Flowchart, Class, ER, State, etc.) with `Node`, `Edge`, `Subgraph`.
- **Renderer** (`IDiagramRenderer<TModel>`): Converts model to `SvgDocument` using `SvgBuilder` fluent API.

**Entry point:** `Mermaid.Render(input, options?)` in `src/Naiad/Mermaid.cs` — auto-detects diagram type from first line, dispatches to the appropriate parser+renderer pair. `Mermaid.RenderToSvgDocument(...)` (internal) returns the `SvgDocument` model instead of serialised markup, and is the seam the PNG backends rasterize.

**PNG rendering:** Two optional packages render to PNG: `Naiad.Skia` (SkiaSharp) and `Naiad.ImageSharp` (SixLabors.ImageSharp). Both share one pipeline in `src/Naiad/Rendering/Raster/`: `SvgRasterizer` walks an `SvgDocument` (resolving the CSS cascade via `Stylesheet`/`ComputedStyle`, composing transforms, flattening path/shape geometry to polyline `SubPath`s with `PathFlattener`, drawing markers, and laying out `<text>`/`<foreignObject>` labels) and paints into an internal `IRenderSurface`. Each backend is just a thin `IRenderSurface` over its rasterizer + a public `SkiaRenderer`/`ImageSharpRenderer` `RenderPng(...)` facade. The seam types are `internal`, exposed to the backends (and tests) via `InternalsVisibleTo`; all three assemblies sign with `src/key.snk`. There is intentionally no built-in (dependency-free) rasterizer. Rasterization is controlled by `RenderOptions.Png` (`Scale`, `Background`).

**Layout engine:** `DagreEngine` in `src/Naiad/Layout/` (a C# implementation of dagre under `src/Naiad/Dagre/`) implements Sugiyama-style graph layout (used by Flowchart and similar graph-based diagrams).

**Key directories:**
- `src/Naiad/Diagrams/` — one subfolder per diagram type, each containing parser and renderer
- `src/Naiad/Layout/` — Dagre-based graph layout algorithm
- `src/Naiad/Models/` — shared data models
- `src/Naiad/Rendering/` — SVG element types (`SvgBuilder`, `SvgDocument`, `SvgGroup`, `SvgPath`, etc.) and `MermaidStyles`

## Testing

Tests use TUnit + Svg.Skia + Verify (snapshot testing). Each diagram type has a test folder under `src/Tests/`. TUnit is Microsoft.Testing.Platform-native (good IDE/Rider discovery); assertions are async fluent (`await Assert.That(x).IsEqualTo(y)`). The suite runs serially (`[assembly: NotInParallel]` in `ModuleInitializer.cs`) because it shares process-global state (`IconPackRegistry`, reset per test via `[Before(Test)]`).

- `TestBase` (in `SvgVerify.cs`) renders SVG, then rasterizes it to PNG in-process via `SvgRenderer` (Svg.Skia — no browser required) for visual regression via Verify.ImageSharp.Compare.
- The Naiad.Skia / Naiad.ImageSharp PNG backends have their own Verify snapshot coverage in `src/Tests/Rendering/PngRenderTests.cs` (one baseline per backend per sample diagram).
- `SvgRenderer` flattens `<foreignObject>` labels (which Svg.Skia cannot render) into centered `<text>` elements for the PNG pass only; the `.verified.svg` files keep their original `<foreignObject>` markup.
- `PngRegenerator` (explicit test) rebuilds all `.verified.png` baselines from the `.verified.svg` files; run it after changing the rasterization path.
- `DocGeneratorTests` auto-generates markdown test renders in `src/test-renders/`.
- When tests fail, `.received.*` files are created alongside `.verified.*` files — review and accept with your Verify diff tool.

## Code Style

- Strict `.editorconfig` with `TreatWarningsAsErrors: true` and `EnforceCodeStyleInBuild`
- File-scoped namespaces, expression-bodied members, `var` everywhere
- LF line endings, UTF-8, 4-space indent for C#
- Central package management via `src/Directory.Packages.props`

## Maintenance log

- 2026-10-11: full performance and bug scan done (parsers, renderers, dagre layout, PNG backends). The findings were fixed in Papyrine/Naiad PRs #44 to #58.

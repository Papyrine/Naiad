# <img src="/src/icon.png" height="30px"> Naiad

[![Build status](https://github.com/Papyrine/Naiad/actions/workflows/build.yml/badge.svg)](https://github.com/Papyrine/Naiad/actions/workflows/build.yml)
[![NuGet Status](https://img.shields.io/nuget/v/Naiad.svg)](https://www.nuget.org/packages/Naiad/)

A .NET library for rendering [Mermaid](https://mermaid.js.org) diagrams to SVG. No browser or JavaScript runtime required.

**[Try it in a browser ↗](https://naiad.papyrine.org)** — a live Mermaid editor (in the spirit of [mermaid.live](https://mermaid.live)) rendered entirely client-side by Naiad on Blazor WebAssembly.

PNG output is available via two optional companion packages — [`Naiad.Skia`](#png-output) (SkiaSharp) and [`Naiad.ImageSharp`](#png-output) (SixLabors.ImageSharp).


## Open Source Maintenance Fee

This project participates in the [Open Source Maintenance Fee](https://opensourcemaintenancefee.org). The source code is freely available under the terms of the [license](license.txt). To support sustainable maintenance, use of the project's official binary releases in revenue-generating activities and all government agencies requires adherence to the [Open Source Maintenance Fee EULA](OsmfEula.txt). The fee is paid by [sponsoring Papyrine](https://github.com/sponsors/Papyrine).

This project uses [SponsorCheck](https://github.com/SimonCropp/SponsorCheck) to surface a build-time reminder in consuming projects that are not yet sponsoring.


## NuGet package

https://nuget.org/packages/Naiad


## Usage

<!-- snippet: Usage -->
<a id='snippet-Usage'></a>
```cs
var svg = Mermaid.Render(
    """
    flowchart LR
        A[Start] --> B[Process] --> C[End]
    """);
```
<sup><a href='/src/Tests/Snippets.cs#L6-L12' title='Snippet source file'>snippet source</a> | <a href='#snippet-Usage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The diagram type is automatically detected from the input.


### Render Options

<!-- snippet: RenderOptions -->
<a id='snippet-RenderOptions'></a>
```cs
var svg = Mermaid.Render(
    input,
    new()
    {
        Padding = 20,
        FontSize = 14,
        FontFamily = "Arial, sans-serif"
    });
```
<sup><a href='/src/Tests/Snippets.cs#L18-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-RenderOptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## PNG output

The core `Naiad` package renders SVG only and has no third-party dependencies. To rasterize a diagram to PNG, add one of the two backend packages. Both drive the exact same parse → layout → style pipeline that produces the SVG, and share all of the SVG-to-pixels code; they differ only in the rasterizer and font engine they use.

| Package | Rasterizer | When to choose |
| --- | --- | --- |
| [`Naiad.Skia`](https://nuget.org/packages/Naiad.Skia/) | [SkiaSharp](https://github.com/mono/SkiaSharp) | Native Skia rendering; bundled native binaries. |
| [`Naiad.ImageSharp`](https://nuget.org/packages/Naiad.ImageSharp/) | [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) | Fully managed, cross-platform; uses installed system fonts for text. Depends on ImageSharp under the [Six Labors Split License](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE). |

<!-- snippet: RenderToPng -->
<a id='snippet-RenderToPng'></a>
```cs
// Naiad.Skia
var skiaPng = SkiaRenderer.RenderPng(input);
SkiaRenderer.RenderPng(input, "diagram.png");

// Naiad.ImageSharp
var imageSharpPng = ImageSharpRenderer.RenderPng(input);
ImageSharpRenderer.RenderPng(input, "diagram.png");
```
<sup><a href='/src/Tests/Snippets.cs#L33-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-RenderToPng' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Both renderers accept the same `RenderOptions` as `Mermaid.Render`, plus a `Png` section controlling rasterization:

<!-- snippet: PngOptions -->
<a id='snippet-PngOptions'></a>
```cs
SkiaRenderer.RenderPng(
    input,
    "diagram.png",
    new()
    {
        Png =
        {
            // 2x device-pixel scale for high-DPI output
            Scale = 2,
            // any CSS colour, or "transparent"
            Background = "white",
            // trade encode speed against file size: Fast, Balanced (default) or Small
            Compression = PngCompression.Balanced
        }
    });
```
<sup><a href='/src/Tests/Snippets.cs#L46-L62' title='Snippet source file'>snippet source</a> | <a href='#snippet-PngOptions' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

`RenderPng` also has an overload that writes to a `Stream`.

`Compression` trades PNG encode speed against file size; the rendered pixels are identical at every level. `Fast` (zlib level 1) encodes much faster but produces larger files, `Small` (level 9) the opposite, and `Balanced` (the default, level 6) sits between. Encoding is a sizeable share of render time — especially on the ImageSharp backend — so `Fast` is worth choosing when throughput matters more than file size.


## AllowHtmlElements

By default, node and edge labels are emitted as HTML inside `<foreignObject>` (matching Mermaid's own output). Set `AllowHtmlElements = false` to render labels as native SVG `<text>` instead, producing self-contained SVG for viewers and rasterizers that have no HTML/CSS engine. Icons that rely on the HTML path — FontAwesome glyphs and icons embedded in labels — are omitted in this mode; iconify pack icons drawn as inline SVG are unaffected.

For example, this single-node graph:

```cs
var svg = Mermaid.Render(
    """
    flowchart LR
        A[Hello]
    """);
```

By default the label is a `<foreignObject>`, and a Font Awesome `@import` is included:

<!-- snippet: AllowHtmlElementsTests.SingleNodeHtml.verified.svg -->
<a id='snippet-AllowHtmlElementsTests.SingleNodeHtml.verified.svg'></a>
```svg
<svg
  id="naiad"
  width="100%"
  xmlns="http://www.w3.org/2000/svg"
  viewBox="0 0 108.5 88"
  style="max-width: 108.5px;"
  role="graphics-document document">
  <style
    xmlns="http://www.w3.org/1999/xhtml">@import url("https://cdnjs.cloudflare.com/ajax/libs/font-awesome/6.7.2/css/all.min.css");</style>
  <style>
#naiad{font-family:"trebuchet ms",verdana,arial,sans-serif;font-size:16px;fill:#333;}#naiad p{margin:0;}
#naiad .marker{fill:#333333;stroke:#333333;}
#naiad .marker.cross{stroke:#333333;}
#naiad span{fill:#333;color:#333;}
#naiad .flowchart-link{stroke:#333333;fill:none;}
#naiad .edgeLabel{background-color:rgba(232,232,232, 0.8);text-align:center;}
#naiad .edgeLabel p{background-color:rgba(232,232,232, 0.8);}
#naiad .edgeLabel rect{opacity:0.5;background-color:rgba(232,232,232, 0.8);fill:rgba(232,232,232, 0.8);}
</style>
  <g
    transform="translate(20,20)">
    <path
      d="M0,0 H68.5 V48 H0 Z"
      fill="#ECECFF"
      stroke="#9370DB" />
    <foreignObject
      x="0"
      y="0"
      width="68.5"
      height="48"
      class="nodeLabel">
      <div
        xmlns="http://www.w3.org/1999/xhtml"
        style="display: table-cell; white-space: nowrap; text-align: center; vertical-align: middle; width: 68.5px; height: 48px;">
        <span>
          <p>Hello</p>
        </span>
      </div>
    </foreignObject>
  </g>
</svg>
```
<sup><a href='/src/Tests/AllowHtmlElementsTests.SingleNodeHtml.verified.svg#L1-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-AllowHtmlElementsTests.SingleNodeHtml.verified.svg' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

With `AllowHtmlElements = false`, the label is a native `<text>` and the Font Awesome `@import` is gone:

<!-- snippet: AllowHtmlElementsTests.SingleNodeNoHtml.verified.svg -->
<a id='snippet-AllowHtmlElementsTests.SingleNodeNoHtml.verified.svg'></a>
```svg
<svg
  id="naiad"
  width="100%"
  xmlns="http://www.w3.org/2000/svg"
  viewBox="0 0 108.5 88"
  style="max-width: 108.5px;"
  role="graphics-document document">
  <style>
#naiad{font-family:"trebuchet ms",verdana,arial,sans-serif;font-size:16px;fill:#333;}#naiad p{margin:0;}
#naiad .marker{fill:#333333;stroke:#333333;}
#naiad .marker.cross{stroke:#333333;}
#naiad span{fill:#333;color:#333;}
#naiad .flowchart-link{stroke:#333333;fill:none;}
#naiad .edgeLabel{background-color:rgba(232,232,232, 0.8);text-align:center;}
#naiad .edgeLabel p{background-color:rgba(232,232,232, 0.8);}
#naiad .edgeLabel rect{opacity:0.5;background-color:rgba(232,232,232, 0.8);fill:rgba(232,232,232, 0.8);}
</style>
  <g
    transform="translate(20,20)">
    <path
      d="M0,0 H68.5 V48 H0 Z"
      fill="#ECECFF"
      stroke="#9370DB" />
    <text
      x="34.25"
      y="24"
      text-anchor="middle"
      dominant-baseline="middle"
      font-size="14px"
      font-family="Arial, sans-serif"
      class="nodeLabel">Hello</text>
  </g>
</svg>
```
<sup><a href='/src/Tests/AllowHtmlElementsTests.SingleNodeNoHtml.verified.svg#L1-L33' title='Snippet source file'>snippet source</a> | <a href='#snippet-AllowHtmlElementsTests.SingleNodeNoHtml.verified.svg' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Icon packs

Naiad can render icons from [iconify](https://iconify.design) icon packs. Packs are not bundled — load the ones you need (in the iconify JSON format) from a file or a stream:

<!-- snippet: LoadIconPack -->
<a id='snippet-LoadIconPack'></a>
```cs
IconPack.Load("logos.json");

// ...or from a stream
using var stream = File.OpenRead("logos.json");
IconPack.Load(stream);
```
<sup><a href='/src/Tests/Snippets.cs#L66-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-LoadIconPack' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Pack files are published as [`@iconify-json/*`](https://icon-sets.iconify.design/) packages (the `icons.json` file), e.g. `@iconify-json/logos`. `Load` registers the pack under its prefix and returns it. Register all packs once at startup — calling `IconPack.Load` after the first `Mermaid.Render` throws a `MermaidException`.

Once loaded, reference icons as `prefix:name` wherever a diagram supports icons — architecture services and groups, flowchart node labels, and mindmap nodes:

<!-- snippet: IconUsage -->
<a id='snippet-IconUsage'></a>
```cs
// Architecture
Mermaid.Render(
    """
    architecture-beta
    service fn(logos:aws-lambda)[Lambda]
    service db(logos:postgresql)[Database]
    fn:R -- L:db
    """);

// Flowchart (inline in labels)
Mermaid.Render(
    """
    flowchart LR
        A[logos:redis Cache] --> B[logos:postgresql DB]
    """);

// Mindmap
Mermaid.Render(
    """
    mindmap
      Project
        Storage ::icon(logos:aws-s3)
    """);
```
<sup><a href='/src/Tests/Snippets.cs#L77-L101' title='Snippet source file'>snippet source</a> | <a href='#snippet-IconUsage' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Single-color icons (e.g. `mdi`, `tabler`) inherit the surrounding color; multi-color icons (e.g. `logos`) keep their own palette.

Icons also rasterize to [PNG](#png-output). Iconify pack icons are inline SVG, so they render identically in SVG and PNG. [FontAwesome](https://fontawesome.com) icons (`fa:fa-name` in flowcharts, `::icon(fa fa-name)` in mindmaps) resolve through the FontAwesome webfont in SVG without a pack; PNG has no webfont, so a `fa:` icon is drawn there only when a pack registered under the `fa` prefix supplies its geometry (an icon keyed `fa-car`, `fa-bell`, …), and is otherwise omitted:

<!-- snippet: IconsInPng -->
<a id='snippet-IconsInPng'></a>
```cs
// Load packs up front, before the first render.
IconPack.Load("logos.json");
IconPack.Load("fontawesome.json");

// Iconify pack icons are inline SVG, so they rasterize to PNG just like the rest of the diagram.
var withIconifyIcon = SkiaRenderer.RenderPng(
    """
    flowchart LR
        A[logos:redis Cache] --> B[logos:postgresql DB]
    """);

// FontAwesome glyphs have no geometry of their own, so a fa: icon only appears in PNG when a
// pack registered under the "fa" prefix supplies it (icons keyed fa-bell, fa-car, ...).
var withFontAwesomeIcon = SkiaRenderer.RenderPng(
    """
    flowchart LR
        A[fa:fa-bell Alerts]
    """);
```
<sup><a href='/src/Tests/Snippets.cs#L106-L125' title='Snippet source file'>snippet source</a> | <a href='#snippet-IconsInPng' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Supported Diagram Types

 * [Flowchart / Graph](https://mermaid.js.org/syntax/flowchart.html)
 * [Sequence Diagram](https://mermaid.js.org/syntax/sequenceDiagram.html)
 * [Class Diagram](https://mermaid.js.org/syntax/classDiagram.html)
 * [State Diagram](https://mermaid.js.org/syntax/stateDiagram.html)
 * [Entity Relationship Diagram](https://mermaid.js.org/syntax/entityRelationshipDiagram.html)
 * [Gantt Chart](https://mermaid.js.org/syntax/gantt.html)
 * [Pie Chart](https://mermaid.js.org/syntax/pie.html)
 * [Git Graph](https://mermaid.js.org/syntax/gitgraph.html)
 * [Mindmap](https://mermaid.js.org/syntax/mindmap.html)
 * [Timeline](https://mermaid.js.org/syntax/timeline.html)
 * [User Journey](https://mermaid.js.org/syntax/userJourney.html)
 * [Quadrant Chart](https://mermaid.js.org/syntax/quadrantChart.html)
 * [Requirement Diagram](https://mermaid.js.org/syntax/requirementDiagram.html)
 * [C4 Diagrams](https://mermaid.js.org/syntax/c4.html) (Context, Container, Component, Deployment)
 * [Kanban](https://mermaid.js.org/syntax/kanban.html)
 * [XY Chart](https://mermaid.js.org/syntax/xyChart.html) (beta)
 * [Sankey](https://mermaid.js.org/syntax/sankey.html) (beta)
 * [Block Diagram](https://mermaid.js.org/syntax/block.html) (beta)
 * [Packet Diagram](https://mermaid.js.org/syntax/packet.html) (beta)
 * [Architecture](https://mermaid.js.org/syntax/architecture.html) (beta)
 * [Radar](https://mermaid.js.org/syntax/radar.html) (beta)
 * [Treemap](https://mermaid.js.org/syntax/treemap.html) (beta)


## Test Renders<!-- include: renders. path: /src/test-renders/renders.include.md -->

Auto-generated documentation from the test suite.
- [C4](/src/test-renders/C4.md)
- [Class](/src/test-renders/Class.md)
- [EntityRelationship](/src/test-renders/EntityRelationship.md)
- [Flowchart](/src/test-renders/Flowchart.md)
- [Gantt](/src/test-renders/Gantt.md)
- [GitGraph](/src/test-renders/GitGraph.md)
- [Kanban](/src/test-renders/Kanban.md)
- [Mindmap](/src/test-renders/Mindmap.md)
- [Pie](/src/test-renders/Pie.md)
- [Quadrant](/src/test-renders/Quadrant.md)
- [Requirement](/src/test-renders/Requirement.md)
- [Sequence](/src/test-renders/Sequence.md)
- [State](/src/test-renders/State.md)
- [Timeline](/src/test-renders/Timeline.md)
- [UserJourney](/src/test-renders/UserJourney.md)

### Beta diagram types

- [Architecture](Architecture.md)
- [Block](Block.md)
- [Packet](Packet.md)
- [Radar](Radar.md)
- [Sankey](Sankey.md)
- [Treemap](Treemap.md)
- [XYChart](XYChart.md)<!-- endInclude -->


## Icon

[Naiad](https://thenounproject.com/icon/naiad-1389186/) designed by [Icons Producer](https://thenounproject.com/creator/iconsproducer/) from [The Noun Project](https://thenounproject.com).

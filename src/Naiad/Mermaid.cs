namespace Naiad;

public static class Mermaid
{
    public static string Render(string input, RenderOptions? options = null)
    {
        options ??= RenderOptions.Default;
        return ToXml(RenderToSvgDocument(input, options), options);
    }

    /// <summary>
    /// Renders to the in-memory <see cref="SvgDocument"/> rather than serialised markup. This is the
    /// seam the PNG render packages (Naiad.Skia, Naiad.ImageSharp) rasterize, so they share the exact
    /// parse → layout → model pipeline that produces the SVG and differ only in how the document is
    /// turned into pixels.
    /// </summary>
    internal static SvgDocument RenderToSvgDocument(string input, RenderOptions? options = null)
    {
        IconPackRegistry.MarkRendered();
        options ??= RenderOptions.Default;
        input = StripPreamble(input.Trim(), out var title);

        if (!TryMatchType(input, out var diagramType))
        {
            throw new MermaidException($"Unknown diagram type in: {input.Split('\n')[0]}");
        }

        return diagramType switch
        {
            DiagramType.Pie =>
                Render(new PieParser(), new PieRenderer(), "pie chart", input, title, options),
            DiagramType.Flowchart =>
                Render(new FlowchartParser(), new FlowchartRenderer(), "flowchart", input, title, options),
            DiagramType.Sequence =>
                Render(new SequenceParser(), new SequenceRenderer(), "sequence diagram", input, title, options),
            DiagramType.Class =>
                Render(new ClassParser(), new ClassRenderer(), "class diagram", input, title, options),
            DiagramType.State =>
                Render(new StateParser(), new StateRenderer(), "state diagram", input, title, options),
            DiagramType.EntityRelationship =>
                Render(new ERParser(), new ERRenderer(), "ER diagram", input, title, options),
            DiagramType.GitGraph =>
                Render(new GitGraphParser(), new GitGraphRenderer(), "git graph", input, title, options),
            DiagramType.Gantt =>
                Render(new GanttParser(), new GanttRenderer(), "gantt chart", input, title, options),
            DiagramType.Mindmap =>
                Render(new MindmapParser(), new MindmapRenderer(), "mindmap", input, title, options),
            DiagramType.Timeline =>
                Render(new TimelineParser(), new TimelineRenderer(), "timeline", input, title, options),
            DiagramType.UserJourney =>
                Render(new UserJourneyParser(), new UserJourneyRenderer(), "user journey", input, title, options),
            DiagramType.Quadrant =>
                Render(new QuadrantParser(), new QuadrantRenderer(), "quadrant chart", input, title, options),
            DiagramType.XYChart =>
                Render(new XYChartParser(), new XYChartRenderer(), "XY chart", input, title, options),
            DiagramType.Sankey =>
                Render(new SankeyParser(), new SankeyRenderer(), "Sankey diagram", input, title, options),
            DiagramType.Block =>
                Render(new BlockParser(), new BlockRenderer(), "block diagram", input, title, options),
            DiagramType.Kanban =>
                Render(new KanbanParser(), new KanbanRenderer(), "kanban board", input, title, options),
            DiagramType.Packet =>
                Render(new PacketParser(), new PacketRenderer(), "packet diagram", input, title, options),
            DiagramType.C4Context or
                DiagramType.C4Container or
                DiagramType.C4Component or
                DiagramType.C4Deployment =>
                Render(new C4Parser(), new C4Renderer(), "C4 diagram", input, title, options),
            DiagramType.Requirement =>
                Render(new RequirementParser(), new RequirementRenderer(), "requirement diagram", input, title, options),
            DiagramType.Architecture =>
                Render(new ArchitectureParser(), new ArchitectureRenderer(), "architecture diagram", input, title, options),
            DiagramType.Radar =>
                Render(new RadarParser(), new RadarRenderer(), "radar diagram", input, title, options),
            DiagramType.Treemap =>
                Render(new TreemapParser(), new TreemapRenderer(), "treemap diagram", input, title, options),
            _ => throw new MermaidException($"Unsupported diagram type: {diagramType}")
        };
    }

    /// <summary>
    /// Detects the <see cref="DiagramType"/> from the opening keyword of Mermaid <paramref name="input"/>,
    /// skipping whatever may precede it: YAML front matter, <c>%%{init:...}%%</c> configuration blocks and
    /// <c>%%</c> comment lines. Unlike rendering this never throws: it returns <see langword="false"/> for
    /// empty input or markup whose first line names no known diagram, so callers such as the live editor
    /// can label whatever is currently being typed.
    /// </summary>
    public static bool TryDetectType(string? input, out DiagramType type)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            type = default;
            return false;
        }

        return TryMatchType(StripPreamble(input.Trim(), out _), out type);
    }

    static SvgDocument Render<TModel>(
        IDiagramParser<TModel> parser,
        IDiagramRenderer<TModel> renderer,
        string description,
        string input,
        string? frontMatterTitle,
        RenderOptions options)
        where TModel : DiagramBase
    {
        var result = parser.Parse(input);

        if (!result.Success)
        {
            throw new MermaidParseException($"Failed to parse {description}: {result.Error}");
        }

        // A title given in front matter stands in when the diagram text sets none of its own.
        var model = result.Value;
        if (frontMatterTitle is not null &&
            string.IsNullOrEmpty(model.Title))
        {
            model.Title = frontMatterTitle;
        }

        return renderer.Render(model, options);
    }

    static bool TryMatchType(string firstLine, out DiagramType type)
    {
        if (firstLine.StartsWith("pie", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Pie;
        }
        else if (firstLine.StartsWith("flowchart", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("graph", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Flowchart;
        }
        else if (firstLine.StartsWith("sequenceDiagram", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Sequence;
        }
        else if (firstLine.StartsWith("classDiagram", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Class;
        }
        else if (firstLine.StartsWith("stateDiagram", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.State;
        }
        else if (firstLine.StartsWith("erDiagram", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.EntityRelationship;
        }
        else if (firstLine.StartsWith("gantt", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Gantt;
        }
        else if (firstLine.StartsWith("gitGraph", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.GitGraph;
        }
        else if (firstLine.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Mindmap;
        }
        else if (firstLine.StartsWith("timeline", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Timeline;
        }
        else if (firstLine.StartsWith("journey", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.UserJourney;
        }
        else if (firstLine.StartsWith("quadrantChart", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Quadrant;
        }
        else if (firstLine.StartsWith("xychart", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.XYChart;
        }
        else if (firstLine.StartsWith("sankey", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Sankey;
        }
        else if (firstLine.StartsWith("block", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Block;
        }
        else if (firstLine.StartsWith("packet", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Packet;
        }
        else if (firstLine.StartsWith("kanban", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Kanban;
        }
        else if (firstLine.StartsWith("architecture-beta", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("architecture", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Architecture;
        }
        else if (firstLine.StartsWith("C4Context", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.C4Context;
        }
        else if (firstLine.StartsWith("C4Container", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.C4Container;
        }
        else if (firstLine.StartsWith("C4Component", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.C4Component;
        }
        else if (firstLine.StartsWith("C4Deployment", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.C4Deployment;
        }
        else if (firstLine.StartsWith("requirementDiagram", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Requirement;
        }
        else if (firstLine.StartsWith("radar-beta", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("radar", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Radar;
        }
        else if (firstLine.StartsWith("treemap-beta", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("treemap", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Treemap;
        }
        else
        {
            type = default;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Removes what Mermaid allows ahead of the diagram's opening keyword: a YAML front matter block (which
    /// must come first), then any mix of <c>%%{init:...}%%</c> configuration blocks and <c>%%</c> comment
    /// lines. What is left starts at the keyword, which is where every diagram parser expects to begin.
    /// </summary>
    static string StripPreamble(string input, out string? title)
    {
        var rest = StripFrontMatter(input.AsSpan().TrimStart(), out title);

        while (rest.StartsWith("%%"))
        {
            if (rest.StartsWith("%%{"))
            {
                var close = rest.IndexOf("}%%");
                if (close < 0)
                {
                    break;
                }

                rest = rest[(close + 3)..].TrimStart();
                continue;
            }

            var lineEnd = rest.IndexOfAny('\r', '\n');
            if (lineEnd < 0)
            {
                // Nothing but a comment.
                rest = [];
                break;
            }

            rest = rest[lineEnd..].TrimStart();
        }

        if (rest.Length == input.Length)
        {
            return input;
        }

        return rest.ToString();
    }

    /// <summary>
    /// Skips a front matter block: a line of three dashes, YAML, and a closing line of three dashes. Its
    /// top-level <c>title</c>, if it has one, is handed back; the rest (such as <c>config</c>) is not used.
    /// Input that opens with dashes but never closes the block is left alone.
    /// </summary>
    static CharSpan StripFrontMatter(CharSpan input, out string? title)
    {
        title = null;
        if (!IsFence(FirstLine(input, out var body)))
        {
            return input;
        }

        var remaining = body;
        while (!remaining.IsEmpty)
        {
            var line = FirstLine(remaining, out var next);
            if (IsFence(line))
            {
                return next.TrimStart();
            }

            // Top-level keys only: an indented `title:` belongs to some nested mapping.
            if (title is null &&
                line.StartsWith("title:"))
            {
                title = Unquote(line["title:".Length..].Trim()).ToString();
            }

            remaining = next;
        }

        title = null;
        return input;
    }

    // Splits off the first line, without its line ending.
    static CharSpan FirstLine(CharSpan text, out CharSpan rest)
    {
        var lineEnd = text.IndexOf('\n');
        if (lineEnd < 0)
        {
            rest = [];
            return text;
        }

        rest = text[(lineEnd + 1)..];
        return text[..lineEnd].TrimEnd('\r');
    }

    static bool IsFence(CharSpan line) =>
        line.TrimEnd() is "---";

    static CharSpan Unquote(CharSpan value)
    {
        if (value.Length >= 2 &&
            value[0] == value[^1] &&
            value[0] is '"' or '\'')
        {
            return value[1..^1];
        }

        return value;
    }

    static string ToXml(SvgDocument svg, RenderOptions options)
    {
        if (!options.AllowHtmlElements)
        {
            // The Font Awesome @import is the only HTML (xhtml-namespaced) markup not produced
            // through the label seam, so drop it here when HTML output is disabled.
            svg.FontAwesomeImport = null;
        }

        var builder = new StringBuilder();
        svg.ToXml(builder);
        return builder.ToString();
    }
}

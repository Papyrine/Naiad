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
        var source = input;
        input = StripPreamble(TrimInput(input), out var title);

        // What came before the diagram's keyword: white space, front matter, init blocks and comments. The
        // parser reads only what follows, so the places it reports are moved down past this.
        var skipped = source[..(source.TrimEnd().Length - input.Length)];

        if (!TryMatchType(input, out var diagramType))
        {
            throw new MermaidException($"Unknown diagram type in: {input.Split('\n')[0]}");
        }

        return diagramType switch
        {
            DiagramType.Pie =>
                Render(new PieParser(), new PieRenderer(), "pie chart", input, title, skipped, options),
            DiagramType.Flowchart =>
                Render(new FlowchartParser(), new FlowchartRenderer(), "flowchart", input, title, skipped, options),
            DiagramType.Sequence =>
                Render(new SequenceParser(), new SequenceRenderer(), "sequence diagram", input, title, skipped, options),
            DiagramType.Class =>
                Render(new ClassParser(), new ClassRenderer(), "class diagram", input, title, skipped, options),
            DiagramType.State =>
                Render(new StateParser(), new StateRenderer(), "state diagram", input, title, skipped, options),
            DiagramType.EntityRelationship =>
                Render(new ERParser(), new ERRenderer(), "ER diagram", input, title, skipped, options),
            DiagramType.GitGraph =>
                Render(new GitGraphParser(), new GitGraphRenderer(), "git graph", input, title, skipped, options),
            DiagramType.Gantt =>
                Render(new GanttParser(), new GanttRenderer(), "gantt chart", input, title, skipped, options),
            DiagramType.Mindmap =>
                Render(new MindmapParser(), new MindmapRenderer(), "mindmap", input, title, skipped, options),
            DiagramType.Timeline =>
                Render(new TimelineParser(), new TimelineRenderer(), "timeline", input, title, skipped, options),
            DiagramType.UserJourney =>
                Render(new UserJourneyParser(), new UserJourneyRenderer(), "user journey", input, title, skipped, options),
            DiagramType.Quadrant =>
                Render(new QuadrantParser(), new QuadrantRenderer(), "quadrant chart", input, title, skipped, options),
            DiagramType.XYChart =>
                Render(new XYChartParser(), new XYChartRenderer(), "XY chart", input, title, skipped, options),
            DiagramType.Sankey =>
                Render(new SankeyParser(), new SankeyRenderer(), "Sankey diagram", input, title, skipped, options),
            DiagramType.Block =>
                Render(new BlockParser(), new BlockRenderer(), "block diagram", input, title, skipped, options),
            DiagramType.Kanban =>
                Render(new KanbanParser(), new KanbanRenderer(), "kanban board", input, title, skipped, options),
            DiagramType.Packet =>
                Render(new PacketParser(), new PacketRenderer(), "packet diagram", input, title, skipped, options),
            DiagramType.C4Context or
                DiagramType.C4Container or
                DiagramType.C4Component or
                DiagramType.C4Dynamic or
                DiagramType.C4Deployment =>
                Render(new C4Parser(), new C4Renderer(), "C4 diagram", input, title, skipped, options),
            DiagramType.Requirement =>
                Render(new RequirementParser(), new RequirementRenderer(), "requirement diagram", input, title, skipped, options),
            DiagramType.Architecture =>
                Render(new ArchitectureParser(), new ArchitectureRenderer(), "architecture diagram", input, title, skipped, options),
            DiagramType.Radar =>
                Render(new RadarParser(), new RadarRenderer(), "radar diagram", input, title, skipped, options),
            DiagramType.Treemap =>
                Render(new TreemapParser(), new TreemapRenderer(), "treemap diagram", input, title, skipped, options),
            DiagramType.ZenUML =>
                Render(new ZenUMLParser(), new SequenceRenderer(), "ZenUML diagram", input, title, skipped, options),
            DiagramType.Ishikawa =>
                Render(new IshikawaParser(), new IshikawaRenderer(), "Ishikawa diagram", input, title, skipped, options),
            DiagramType.TreeView =>
                Render(new TreeViewParser(), new TreeViewRenderer(), "tree view", input, title, skipped, options),
            DiagramType.Wardley =>
                Render(new WardleyParser(), new WardleyRenderer(), "Wardley map", input, title, skipped, options),
            DiagramType.Venn =>
                Render(new VennParser(), new VennRenderer(), "Venn diagram", input, title, skipped, options),
            DiagramType.Swimlane =>
                Render(new SwimlaneParser(), new SwimlaneRenderer(), "swimlane diagram", input, title, skipped, options),
            DiagramType.EventModeling =>
                Render(new EventModelingParser(), new EventModelingRenderer(), "event modeling diagram", input, title, skipped, options),
            DiagramType.UseCase =>
                Render(new UseCaseParser(), new UseCaseRenderer(), "use case diagram", input, title, skipped, options),
            DiagramType.Cynefin =>
                Render(new CynefinParser(), new CynefinRenderer(), "Cynefin diagram", input, title, skipped, options),
            DiagramType.Info =>
                Render(new InfoParser(), new InfoRenderer(), "info diagram", input, title, skipped, options),
            DiagramType.AgentFlow =>
                Render(new AgentFlowParser(), new AgentFlowRenderer(), "agent flow", input, title, skipped, options),
            DiagramType.Railroad =>
                Render(new RailroadParser(), new RailroadRenderer(), "railroad diagram", input, title, skipped, options),
            DiagramType.RailroadAbnf =>
                Render(new RailroadAbnfParser(), new RailroadRenderer(), "ABNF railroad diagram", input, title, skipped, options),
            DiagramType.RailroadEbnf =>
                Render(new RailroadEbnfParser(), new RailroadRenderer(), "EBNF railroad diagram", input, title, skipped, options),
            DiagramType.RailroadPeg =>
                Render(new RailroadPegParser(), new RailroadRenderer(), "PEG railroad diagram", input, title, skipped, options),
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

        return TryMatchType(StripPreamble(TrimInput(input), out _), out type);
    }

    static SvgDocument Render<TModel>(
        IDiagramParser<TModel> parser,
        IDiagramRenderer<TModel> renderer,
        string description,
        string input,
        string? frontMatterTitle,
        string skipped,
        RenderOptions options)
        where TModel : DiagramBase
    {
        var result = parser.Parse(input);

        if (!result.Success)
        {
            throw new MermaidParseException($"Failed to parse {description}: {Relocate(result.Error, skipped)}");
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

    // A parse error ends "at line L, col C", counted from the diagram's keyword. Moves that past what was
    // skipped ahead of it, so the place is the one in the text the caller passed in.
    static string Relocate(string error, string skipped)
    {
        const string atLine = " at line ";
        const string atColumn = ", col ";
        var at = error.LastIndexOf(atLine, StringComparison.Ordinal);
        if (skipped.Length == 0 ||
            at < 0)
        {
            return error;
        }

        var place = error.AsSpan(at + atLine.Length);
        var comma = place.IndexOf(atColumn, StringComparison.Ordinal);
        if (comma < 0 ||
            !int.TryParse(place[..comma], NumberStyles.None, CultureInfo.InvariantCulture, out var line) ||
            !int.TryParse(place[(comma + atColumn.Length)..], NumberStyles.None, CultureInfo.InvariantCulture, out var column))
        {
            return error;
        }

        // The keyword's line starts partway through the last skipped line.
        if (line == 1)
        {
            column += skipped.Length - (skipped.LastIndexOf('\n') + 1);
        }

        line += skipped.AsSpan().Count('\n');
        return string.Create(CultureInfo.InvariantCulture, $"{error[..at]}{atLine}{line}{atColumn}{column}");
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
        else if (firstLine.StartsWith("C4Dynamic", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.C4Dynamic;
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
        // Mermaid only knows the tree view by its -beta keyword. It shares its first four letters with
        // treemap, but neither keyword starts with the other, so their order here does not matter.
        else if (firstLine.StartsWith("treeView-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.TreeView;
        }
        else if (firstLine.StartsWith("treemap-beta", StringComparison.OrdinalIgnoreCase) ||
                 firstLine.StartsWith("treemap", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Treemap;
        }
        else if (firstLine.StartsWith("ishikawa", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Ishikawa;
        }
        else if (firstLine.StartsWith("eventmodeling", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.EventModeling;
        }
        else if (IsCynefinKeyword(firstLine))
        {
            type = DiagramType.Cynefin;
        }
        // Mermaid only knows railroad diagrams by their -beta keywords. The notation keywords share the
        // `railroad-` prefix, so they are matched whole before the plain one.
        else if (firstLine.StartsWith("railroad-abnf-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.RailroadAbnf;
        }
        else if (firstLine.StartsWith("railroad-ebnf-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.RailroadEbnf;
        }
        else if (firstLine.StartsWith("railroad-peg-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.RailroadPeg;
        }
        else if (firstLine.StartsWith("railroad-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Railroad;
        }
        else if (firstLine.StartsWith("zenuml", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.ZenUML;
        }
        // Mermaid only knows Wardley maps by their beta keyword so far.
        else if (firstLine.StartsWith("wardley-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Wardley;
        }
        // Mermaid only recognises the -beta spelling, so plain `venn` is not taken.
        else if (firstLine.StartsWith("venn-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.Venn;
        }
        else if (firstLine.StartsWith("swimlane-beta", StringComparison.Ordinal))
        {
            // Mermaid knows only the `-beta` spelling, and matches it case-sensitively.
            type = DiagramType.Swimlane;
        }
        // Mermaid only knows the keyword with its suffix, and nothing else starts with it.
        else if (firstLine.StartsWith("usecase-beta", StringComparison.OrdinalIgnoreCase))
        {
            type = DiagramType.UseCase;
        }
        // "info" is short and a common start of a word, so it only counts when it is the whole word.
        else if (IsWholeKeyword(firstLine, "info"))
        {
            type = DiagramType.Info;
        }
        // Mermaid's detector is /^\s*agentflow-beta\b/: the -beta spelling only, case-sensitive, and not
        // followed by more of a word. No other keyword starts with it or is a prefix of it.
        else if (firstLine.StartsWith("agentflow-beta", StringComparison.Ordinal) &&
                 (firstLine.Length == "agentflow-beta".Length ||
                  !(char.IsLetterOrDigit(firstLine["agentflow-beta".Length]) || firstLine["agentflow-beta".Length] == '_')))
        {
            type = DiagramType.AgentFlow;
        }
        else
        {
            type = default;
            return false;
        }

        return true;
    }

    // True when the input opens with the keyword followed by whitespace or nothing at all.
    static bool IsWholeKeyword(string input, string keyword) =>
        input.StartsWith(keyword, StringComparison.OrdinalIgnoreCase) &&
        (input.Length == keyword.Length || char.IsWhiteSpace(input[keyword.Length]));

    /// <summary>
    /// Mermaid's detector for Cynefin takes only <c>cynefin-beta</c>, in lower case, followed by whitespace,
    /// a colon or the end of the input; the plain <c>cynefin</c> is not a diagram.
    /// </summary>
    static bool IsCynefinKeyword(string firstLine)
    {
        const string keyword = "cynefin-beta";
        return firstLine.StartsWith(keyword, StringComparison.Ordinal) &&
               (firstLine.Length == keyword.Length ||
                firstLine[keyword.Length] == ':' ||
                char.IsWhiteSpace(firstLine[keyword.Length]));
    }

    // Text decoded from UTF-8 bytes without stripping the byte order mark starts with U+FEFF, which
    // string.Trim does not count as white space, so the diagram type was not found behind it.
    const char byteOrderMark = (char) 0xFEFF;

    static string TrimInput(string input) =>
        input.TrimStart(byteOrderMark).Trim();

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
        var markup = builder.ToString();
        return options.PrettyPrint ? SvgPrettyPrinter.Format(markup) : markup;
    }
}

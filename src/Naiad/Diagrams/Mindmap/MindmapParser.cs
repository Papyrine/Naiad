class MindmapParser : IDiagramParser<MindmapModel>
{
    static Parser<MindmapModel> parser;

    static MindmapParser()
    {
        // Parse indentation (spaces or tabs)
        var indentationParser = CommonParsers.Indentation;

        // Icon: ::icon(fa fa-book)
        var iconParser =
            Between(Literals.Text("::icon("), Literals.NoneOf(")"), Literals.Char(')'))
                .AsString();

        // CSS class: :::className
        var cssClassParser =
            Literals.Text(":::")
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-'))
                .AsString();

        // Node with shape: ((circle)), (rounded), [square], {{hexagon}}, ))bang((, )cloud(
        var shapedNodeParser =
            OneOf(
                // Circle: ((text))
                Between(Literals.Text("(("), Literals.NoneOf(")"), Literals.Text("))"))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Circle)),
                // Bang/explosion: ))text((
                Between(Literals.Text("))"), Literals.NoneOf("("), Literals.Text("(("))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Bang)),
                // Cloud: )text(
                Between(Literals.Char(')'), Literals.NoneOf("("), Literals.Char('('))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Cloud)),
                // Hexagon: {{text}}
                Between(Literals.Text("{{"), Literals.NoneOf("}"), Literals.Text("}}"))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Hexagon)),
                // Rounded: (text)
                Between(Literals.Char('('), Literals.NoneOf(")"), Literals.Char(')'))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Rounded)),
                // Square: [text]
                Between(Literals.Char('['), Literals.NoneOf("]"), Literals.Char(']'))
                    .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Square))
            );

        // Optional node id preceding a shape, e.g. the "root" in root((text)). Mindmap
        // nodes have no edges, so the id is not used when rendering — the shape's text
        // is the label.
        var nodeIdParser =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-');

        // A shape with an optional leading id: id((circle)), id[square], or just ((circle)).
        var shapedNodeWithIdParser =
            nodeIdParser.Optional()
                .SkipAnd(shapedNodeParser);

        // A line with no shape is its text, up to any icon or class.
        var plainNodeParser =
            Literals.NoneOf(":\r\n", minSize: 0)
                .Then(static _ => (text: _.Span.Trim().ToString(), shape: MindmapShape.Default));

        // Node line: indentation + optional shape + text + optional icon/class
        var nodeLineParser =
            indentationParser
                .And(shapedNodeWithIdParser.Or(plainNodeParser))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(iconParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(cssClassParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (indent, node, icon, cssClass) =>
                    (indent, node.text, node.shape, icon.OrNull(), cssClass.OrNull()));

        // Content line - node line, skip line (comment/empty), or end
        var contentLine =
            OneOf(
                nodeLineParser.Then<(int, string, MindmapShape, string?, string?)?>(static _ => _),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment)
                    .Then<(int, string, MindmapShape, string?, string?)?>(static _ => null),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline)
                    .Then<(int, string, MindmapShape, string?, string?)?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("mindmap"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentLine.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.Where(static _ => _.HasValue).Select(static _ => _!.Value).ToList()));
    }

    static MindmapModel BuildModel(List<(int indent, string text, MindmapShape shape, string? icon, string? cssClass)> lines)
    {
        var model = new MindmapModel();

        if (lines.Count == 0)
            return model;

        // Build tree from indentation
        var nodes = lines.Select((line, index) => new MindmapNode
        {
            Text = line.text,
            Shape = line.shape,
            Icon = line.icon,
            CssClass = line.cssClass,
            Level = index == 0 ? 0 : -1 // Root is level 0, others TBD
        }).ToList();

        // First node is root
        model.Root = nodes[0];
        model.Root.Level = 0;

        if (nodes.Count == 1)
            return model;

        // Calculate base indentation (from first node after root)
        var baseIndent = lines[0].indent;
        var indentStack = new Stack<(int indent, MindmapNode node)>();
        indentStack.Push((baseIndent, model.Root));

        for (var i = 1; i < lines.Count; i++)
        {
            var (indent, _, _, _, _) = lines[i];
            var node = nodes[i];

            // Pop stack until we find a parent with smaller indentation
            while (indentStack.TryPeek(out var top) &&
                   top.indent >= indent)
            {
                indentStack.Pop();
            }

            if (indentStack.Count == 0)
            {
                // This shouldn't happen with valid input, but treat as child of root
                indentStack.Push((baseIndent, model.Root));
            }

            var parent = indentStack.Peek().node;
            node.Level = parent.Level + 1;
            parent.Children.Add(node);

            indentStack.Push((indent, node));
        }

        return model;
    }

    public DiagramParseResult<MindmapModel> Parse(string input) => parser.ParseDiagram(input);
}

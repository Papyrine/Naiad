class BlockParser : IDiagramParser<BlockModel>
{
    // The head that may close a link, and the one that may open a two-headed link.
    static readonly Parser<char> linkHead =
        OneOf(Literals.Char('>'), Literals.Char('o'), Literals.Char('x'));

    static readonly Parser<char> linkHeadStart =
        OneOf(Literals.Char('<'), Literals.Char('o'), Literals.Char('x'));

    static Parser<BlockModel> parser;

    static BlockParser()
    {
        // An id is letters, digits and `_`, with single hyphens inside it (`my-block`). A hyphen that is not
        // followed by another of those characters starts a link instead: `A-->B`, `A-.->B`.
        var idChars =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_');

        var identifier =
            Capture(idChars.And(ZeroOrMany(Literals.Char('-').And(idChars))))
                .AsString();

        // What may follow a statement on its line.
        var lineEnd =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.LineEnd));

        var gap =
            Literals.AnyOf(" \t");

        // `columns auto` is the default spelled out: no fixed count.
        var columnsParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("columns"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(CommonParsers.UnsignedInt.Or(TextIgnoreCase("auto").Then(0)))
                .AndSkip(lineEnd);

        // Slash and backslash framed shapes. Bare text stops at / \ ], so the opener (`[/` or `[\`) and the
        // closer (`/]` or `\]`) together pick between the parallelograms and the trapezoids.
        const string slashEnd = "/\\]";

        var direction =
            OneOf(
                TextIgnoreCase("right").Then(BlockArrowDirection.Right),
                TextIgnoreCase("left").Then(BlockArrowDirection.Left),
                TextIgnoreCase("up").Then(BlockArrowDirection.Up),
                TextIgnoreCase("down").Then(BlockArrowDirection.Down),
                TextIgnoreCase("x").Then(BlockArrowDirection.Left | BlockArrowDirection.Right),
                TextIgnoreCase("y").Then(BlockArrowDirection.Up | BlockArrowDirection.Down));

        // `(right)`, `(x, down)`: the comma between directions is optional.
        var directions =
            Literals.Char('(')
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace
                            .And(Literals.Char(',').Optional())
                            .And(CommonParsers.InlineWhitespace),
                        direction))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(')'))
                .Then(static _ => _.Aggregate(BlockArrowDirection.None, static (all, one) => all | one));

        // Block arrow: `id<["label"]>(right)`.
        var blockArrow =
            Literals.Text("<[")
                .SkipAnd(Label("]>", "]"))
                .AndSkip(Literals.Text("]>"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(directions)
                .Map(static (label, arrow) => new ShapeSpec(label, BlockShape.BlockArrow, arrow));

        // Longer openers first: `(((` before `((` before `(`, `[[` before `[`, `{{` before `{`.
        var shapeParser =
            OneOf(
                Shape("(((", ")))", BlockShape.DoubleCircle, ")"),
                Shape("((", "))", BlockShape.Circle, ")"),
                Shape("([", "])", BlockShape.Stadium, "]"),
                Shape("(", ")", BlockShape.Rounded, ")"),
                Shape("[[", "]]", BlockShape.Subroutine, "]"),
                Shape("[(", ")]", BlockShape.Cylinder, ")"),
                Shape("[/", "/]", BlockShape.LeanRight, slashEnd),
                Shape("[/", "\\]", BlockShape.Trapezoid, slashEnd),
                Shape("[\\", "\\]", BlockShape.LeanLeft, slashEnd),
                Shape("[\\", "/]", BlockShape.InvertedTrapezoid, slashEnd),
                Shape("[", "]", BlockShape.Rectangle, "]"),
                Shape("{{", "}}", BlockShape.Hexagon, "}"),
                Shape("{", "}", BlockShape.Diamond, "}"),
                Shape(">", "]", BlockShape.Asymmetric, "]"),
                blockArrow);

        // Span: :N
        var spanParser =
            Literals.Char(':').SkipAnd(CommonParsers.UnsignedInt);

        // Block element: id["label"]:2. A bare `space` or `space:2` is a gap of that many columns. A bare
        // `block` opens a composite and a bare `end` closes one, so neither is a block, but with a shape
        // either is: `End(("End"))`.
        var elementParser =
            identifier
                .And(shapeParser.Optional())
                .And(spanParser.Optional())
                .Map(static (id, shape, span) => new ElementRef(id, shape.ToNullable(), span.ToNullable()))
                .When(static (_, element) => element.Shape is not null || !IsKeyword(element.Id));

        // A dotted link closes with `.-`, `.->`, `..->` and so on.
        var dottedTail =
            Literals.AnyOf(".")
                .SkipAnd(Literals.Char('-'))
                .SkipAnd(linkHead.Optional())
                .Then(static _ => _.ToNullable());

        // Links written in one piece: `-->`, `---`, `--x`, `--o`, `-.->`, `==>`, `~~~`.
        var contiguousLink =
            OneOf(
                LineTail('-').Then(static _ => new LinkBody(EdgeStyle.Solid, _, null, false)),
                Literals.Char('-').SkipAnd(dottedTail).Then(static _ => new LinkBody(EdgeStyle.Dotted, _, null, false)),
                LineTail('=').Then(static _ => new LinkBody(EdgeStyle.Thick, _, null, false)),
                Literals.AnyOf("~", minSize: 3).Then(static _ => new LinkBody(EdgeStyle.Solid, null, null, true)));

        // Links carrying a label: `-- "text" -->`, `-. "text" .->`, `== "text" ==>`.
        var labelledLink =
            OneOf(
                Labelled("--", LineTail('-'), EdgeStyle.Solid),
                Labelled("-.", dottedTail, EdgeStyle.Dotted),
                Labelled("==", LineTail('='), EdgeStyle.Thick));

        // Contiguous is tried first, so `A --> B --> C` is never read as one link labelled `> B`.
        var linkParser =
            linkHeadStart.Optional()
                .And(contiguousLink.Or(labelledLink))
                .Map(static (start, body) => ToLink(start.ToNullable(), body));

        // A link and the block it leads to. Tried as a unit, so whitespace after the last block of a chain is
        // left for whatever comes next on the line.
        var linkedElement =
            CommonParsers.InlineWhitespace
                .SkipAnd(linkParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(elementParser);

        // `A`, or blocks joined by links: `A --> B --> C`.
        var chainParser =
            elementParser
                .And(linkedElement.Many())
                .Map(static (first, rest) =>
                    new Chain(
                        [first, .. rest.Select(_ => _.Item2)],
                        rest.Select(_ => _.Item1).ToList()));

        // A line of chains, separated by blanks: `A space B`. Each line is read on its own, so a comment, a
        // `columns` line or any other statement may come between lines of blocks.
        var elementsLineParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Separated(gap, chainParser))
                .AndSkip(lineEnd)
                .Then<IBlockContent?>(static _ => new ChainsItem(_));

        var idList =
            Separated(Literals.Char(',').And(CommonParsers.InlineWhitespace), identifier);

        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsString();

        // `style id1,id2 fill:#f9f,stroke:#333`
        var styleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("style"))
                .SkipAnd(gap)
                .SkipAnd(idList)
                .AndSkip(gap)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IBlockContent?>(static _ => new StyleItem(_.Item1, _.Item2));

        // `classDef name fill:#f9f,stroke:#333`
        var classDefParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("classDef"))
                .SkipAnd(gap)
                .SkipAnd(idList)
                .AndSkip(gap)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IBlockContent?>(static _ => new ClassDefItem(_.Item1, _.Item2));

        // `class id1,id2 name`
        var classParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("class"))
                .SkipAnd(gap)
                .SkipAnd(idList)
                .AndSkip(gap)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<IBlockContent?>(static _ => new ClassItem(_.Item1, _.Item2));

        // A composite opens with `block` on its own line, or `block:id`, which may carry a label and a span
        // like any other block (`block:group1:2`), and closes with `end`.
        var compositeHeader =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("block"))
                .SkipAnd(Literals.Char(':').SkipAnd(elementParser).Optional())
                .AndSkip(lineEnd);

        var compositeEnd =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("end"))
                .AndSkip(lineEnd);

        // Comments and empty lines
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Defined below, once the composite rule exists: what a composite holds may itself be a composite.
        var contentItem = Deferred<IBlockContent?>();

        var compositeParser =
            compositeHeader
                .And(contentItem.Many())
                .AndSkip(compositeEnd)
                .Map(static IBlockContent? (header, content) => new CompositeItem(header.OrNull(), content));

        // `classDef` before `class`, which is the start of it. The keyword statements before a line of blocks,
        // which would otherwise read `style` as the id of one.
        contentItem.Parser =
            OneOf(
                columnsParser.Then<IBlockContent?>(static _ => new ColumnsItem(_)),
                compositeParser,
                classDefParser,
                classParser,
                styleParser,
                elementsLineParser,
                skipLine.Then<IBlockContent?>(static _ => null));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("block"))
                .SkipAnd(lineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.End)
                .Then(static _ => new ModelBuilder().Build(_));
    }

    static bool IsKeyword(string id) =>
        id.Equals("end", StringComparison.OrdinalIgnoreCase) ||
        id.Equals("block", StringComparison.OrdinalIgnoreCase);

    // A block shape: its opening token, its label, its closing token.
    static Parser<ShapeSpec> Shape(string open, string close, BlockShape shape, string bareEnd) =>
        Literals.Text(open)
            .SkipAnd(Label(close, bareEnd))
            .AndSkip(Literals.Text(close))
            .Then(_ => new ShapeSpec(_, shape, BlockArrowDirection.None));

    // A label is either a quoted string - the quotes are dropped, and inside them the characters that would
    // otherwise close the shape are just text - or bare text running up to the first of `bareEnd`. One that
    // opens with a quote is a quoted one: left unclosed, it is not read as bare text instead. HTML entities
    // such as `&nbsp;` stand for the characters they name, as they do in Mermaid's HTML labels.
    static Parser<string> Label(string close, string bareEnd) =>
        CommonParsers.InlineWhitespace
            .SkipAnd(CommonParsers.DoubleQuotedString)
            .AndSkip(CommonParsers.InlineWhitespace)
            .WhenFollowedBy(Literals.Text(close))
            .Or(
                Not(Literals.Char('"'))
                    .SkipAnd(Literals.NoneOf(bareEnd + "\r\n", minSize: 0).AsTrimmedString()))
            .Then(Decode);

    static string Decode(string label)
    {
        if (label.Contains('&'))
        {
            return WebUtility.HtmlDecode(label);
        }

        return label;
    }

    // The closing run of a solid (`-`) or thick (`=`) link, giving the head it ends in: `-->`, `--x`, `---`,
    // `----`. A link needs two line characters and a head, or three without one.
    static Parser<char?> LineTail(char line) =>
        Literals.AnyOf(line.ToString())
            .And(linkHead.Optional())
            .When(static (_, tail) => tail.Item1.Length >= 3 || (tail.Item1.Length == 2 && tail.Item2.HasValue))
            .Then(static _ => _.Item2.ToNullable());

    // A link with its label inside it. The text runs up to the first place the closing run matches, which
    // also fixes the head.
    static Parser<LinkBody> Labelled(string open, Parser<char?> close, EdgeStyle style) =>
        Literals.Text(open)
            .SkipAnd(LineBefore(close))
            .And(close)
            .Map((text, head) => new LinkBody(style, head, LinkLabel(text.Span), false));

    // A link label written in quotes is shown without them.
    static string? LinkLabel(CharSpan text)
    {
        text = text.Trim();
        if (text.Length >= 2 &&
            text[0] == '"' &&
            text[^1] == '"')
        {
            text = text[1..^1];
        }

        if (text.Length == 0)
        {
            return null;
        }

        return Decode(text.ToString());
    }

    static Link ToLink(char? start, LinkBody body)
    {
        var type = EdgeType.Invisible;
        if (!body.Invisible)
        {
            type = FlowchartParser.LinkType(start, body.Style, body.Head);
        }

        return new(type, body.Style, body.Label);
    }

    public DiagramParseResult<BlockModel> Parse(string input) => parser.ParseDiagram(input);

    // Turns the statements into the model. A block is defined where it is first named, and a later mention
    // of it (in a link, say) can give it a label and shape without adding it to the grid again, as in
    // Mermaid. Styles and classes apply by id, wherever the block is defined.
    sealed class ModelBuilder
    {
        BlockModel model = new();
        Dictionary<string, BlockElement> blocks = new(StringComparer.Ordinal);
        Dictionary<string, NodeStyle> classDefs = new(StringComparer.Ordinal);
        List<ClassItem> classAssignments = [];
        List<StyleItem> styles = [];
        int composites;

        public BlockModel Build(IReadOnlyList<IBlockContent?> content)
        {
            model.Columns = Fill(model.Elements, content);
            ApplyStyles();
            return model;
        }

        // Adds what a container holds to its children, returning the container's column count.
        int Fill(List<BlockElement> children, IReadOnlyList<IBlockContent?> content)
        {
            var columns = 0;
            foreach (var item in content)
            {
                switch (item)
                {
                    case ColumnsItem columnsItem:
                        columns = columnsItem.Count;
                        break;

                    case ChainsItem chains:
                        foreach (var chain in chains.Chains)
                        {
                            AddChain(children, chain);
                        }

                        break;

                    case CompositeItem composite:
                        AddComposite(children, composite);
                        break;

                    case StyleItem style:
                        styles.Add(style);
                        break;

                    case ClassItem classItem:
                        classAssignments.Add(classItem);
                        break;

                    case ClassDefItem classDef:
                        var definition = FlowchartParser.ParseStyleProps(classDef.Props);
                        foreach (var name in classDef.Names)
                        {
                            classDefs[name] = classDefs.TryGetValue(name, out var existing)
                                ? existing.MergedWith(definition)
                                : definition;
                        }

                        break;
                }
            }

            return columns;
        }

        void AddChain(List<BlockElement> children, Chain chain)
        {
            var previous = Add(children, chain.Elements[0]);
            for (var i = 1; i < chain.Elements.Count; i++)
            {
                var block = Add(children, chain.Elements[i]);
                var link = chain.Links[i - 1];
                if (!previous.IsSpace &&
                    !block.IsSpace)
                {
                    model.Edges.Add(
                        new()
                        {
                            SourceId = previous.Id,
                            TargetId = block.Id,
                            Label = link.Label,
                            Type = link.Type,
                            LineStyle = link.Style
                        });
                }

                previous = block;
            }
        }

        BlockElement Add(List<BlockElement> children, ElementRef element)
        {
            var span = Math.Max(1, element.Span ?? 1);
            if (element is {Id: "space", Shape: null})
            {
                var space = new BlockElement
                {
                    Id = element.Id,
                    Span = span,
                    IsSpace = true
                };
                children.Add(space);
                return space;
            }

            if (blocks.TryGetValue(element.Id, out var existing))
            {
                if (element.Shape is { } shape)
                {
                    existing.Label = shape.Label;
                    existing.Shape = shape.Shape;
                    existing.ArrowDirections = shape.Directions;
                }

                if (element.Span is not null)
                {
                    existing.Span = span;
                }

                return existing;
            }

            var block = new BlockElement
            {
                Id = element.Id,
                Label = element.Shape?.Label ?? element.Id,
                Shape = element.Shape?.Shape ?? BlockShape.Rectangle,
                ArrowDirections = element.Shape?.Directions ?? BlockArrowDirection.None,
                Span = span
            };
            blocks[block.Id] = block;
            children.Add(block);
            return block;
        }

        // A composite without an id gets one no block can have, so it can be told apart from the others but
        // never linked to.
        void AddComposite(List<BlockElement> children, CompositeItem item)
        {
            composites++;
            var header = item.Header;
            var composite = new BlockElement
            {
                Id = header?.Id ?? $"#composite{composites}",
                Label = header?.Shape?.Label,
                Span = Math.Max(1, header?.Span ?? 1),
                IsComposite = true
            };

            if (header is not null)
            {
                blocks[composite.Id] = composite;
            }

            children.Add(composite);
            composite.Columns = Fill(composite.Children, item.Content);
        }

        // `classDef default` styles every block, then classes apply in the order they are assigned, and a
        // `style` line wins over both.
        void ApplyStyles()
        {
            if (classDefs.TryGetValue("default", out var defaultStyle))
            {
                foreach (var block in blocks.Values)
                {
                    block.Style = defaultStyle;
                }
            }

            foreach (var assignment in classAssignments)
            {
                var classNames = assignment.Classes.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
                foreach (var id in assignment.Ids)
                {
                    if (!blocks.TryGetValue(id, out var block))
                    {
                        continue;
                    }

                    foreach (var className in classNames)
                    {
                        if (classDefs.TryGetValue(className, out var classStyle))
                        {
                            block.Style = Merge(block.Style, classStyle);
                        }
                    }
                }
            }

            foreach (var style in styles)
            {
                var inline = FlowchartParser.ParseStyleProps(style.Props);
                foreach (var id in style.Ids)
                {
                    if (blocks.TryGetValue(id, out var block))
                    {
                        block.Style = Merge(block.Style, inline);
                    }
                }
            }
        }

        static NodeStyle Merge(NodeStyle? current, NodeStyle style)
        {
            if (current is null)
            {
                return style;
            }

            return current.MergedWith(style);
        }
    }

    readonly record struct ShapeSpec(string Label, BlockShape Shape, BlockArrowDirection Directions);

    sealed record ElementRef(string Id, ShapeSpec? Shape, int? Span);

    readonly record struct LinkBody(EdgeStyle Style, char? Head, string? Label, bool Invisible);

    readonly record struct Link(EdgeType Type, EdgeStyle Style, string? Label);

    sealed record Chain(List<ElementRef> Elements, List<Link> Links);

    internal interface IBlockContent;

    sealed record ColumnsItem(int Count) : IBlockContent;

    sealed record ChainsItem(IReadOnlyList<Chain> Chains) : IBlockContent;

    sealed record CompositeItem(ElementRef? Header, IReadOnlyList<IBlockContent?> Content) : IBlockContent;

    sealed record StyleItem(IReadOnlyList<string> Ids, string Props) : IBlockContent;

    sealed record ClassDefItem(IReadOnlyList<string> Names, string Props) : IBlockContent;

    sealed record ClassItem(IReadOnlyList<string> Ids, string Classes) : IBlockContent;
}

class FlowchartParser : IDiagramParser<FlowchartModel>
{
    static Parser<FlowchartModel> parser;

    // The head that may close a link, and the one that may open a two-headed link.
    static readonly Parser<char> linkHead =
        OneOf(Literals.Char('>'), Literals.Char('o'), Literals.Char('x'));

    static readonly Parser<char> linkHeadStart =
        OneOf(Literals.Char('<'), Literals.Char('o'), Literals.Char('x'));

    static FlowchartParser()
    {
        // Node shape parsers - returns (label, shape)
        var doubleCircleShape = Shape("(((", ")))", NodeShape.DoubleCircle, ")");
        var circleShape = Shape("((", "))", NodeShape.Circle, ")");
        var stadiumShape = Shape("([", "])", NodeShape.Stadium, "]");
        var subroutineShape = Shape("[[", "]]", NodeShape.Subroutine, "]");
        var cylinderShape = Shape("[(", ")]", NodeShape.Cylinder, ")");
        var hexagonShape = Shape("{{", "}}", NodeShape.Hexagon, "}");
        var diamondShape = Shape("{", "}", NodeShape.Diamond, "}");
        var roundedShape = Shape("(", ")", NodeShape.RoundedRectangle, ")");
        var rectangleShape = Shape("[", "]", NodeShape.Rectangle, "]");
        var asymmetricShape = Shape(">", "]", NodeShape.Asymmetric, "]");

        // Slash/backslash framed shapes. Bare text excludes / \ ] so the closing token is unambiguous; the
        // opener (`[/` vs `[\`) and closer (`/]` vs `\]`) together pick parallelogram vs trapezoid.
        const string slashShapeEnd = "/\\]";

        var parallelogramShape = Shape("[/", "/]", NodeShape.Parallelogram, slashShapeEnd);
        var trapezoidShape = Shape("[/", "\\]", NodeShape.Trapezoid, slashShapeEnd);
        var parallelogramAltShape = Shape("[\\", "\\]", NodeShape.ParallelogramAlt, slashShapeEnd);
        var trapezoidAltShape = Shape("[\\", "/]", NodeShape.TrapezoidAlt, slashShapeEnd);

        var nodeShapeParser =
            OneOf(
                doubleCircleShape,
                circleShape,
                stadiumShape,
                subroutineShape,
                cylinderShape,
                hexagonShape,
                diamondShape,
                roundedShape,
                asymmetricShape,
                parallelogramShape,
                trapezoidShape,
                parallelogramAltShape,
                trapezoidAltShape,
                rectangleShape
            );

        // Node id: letters, digits and underscores, plus a hyphen that sits between two of them (`my-node`).
        // A hyphen that starts a link is not part of the id, so `A-->B`, `A-.->B` and `A---B` split into
        // node, link, node without needing spaces around the link.
        var idChars =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_');

        var nodeId =
            Capture(idChars.And(ZeroOrMany(Literals.Char('-').And(idChars))))
                .AsString();

        // `@{ shape: cyl, label: "Database" }` - the body is handed to ApplyShapeAttributes as text. A
        // quoted value is taken whole, so it may contain a `}`.
        var shapeAttributes =
            Literals.Text("@{")
                .SkipAnd(
                    Capture(
                        OneOf(
                                Capture(Literals.Char('"').And(Literals.NoneOf("\"", minSize: 0)).And(Literals.Char('"'))),
                                Literals.NoneOf("}\""))
                            .Many()))
                .AndSkip(Literals.Char('}'))
                .AsString();

        // Node parser: identifier optionally followed by a shape and/or `@{ ... }` attributes, then an
        // optional `:::class` shorthand whose class name is recorded on the node and resolved against
        // `classDef` styles at model-build time.
        var nodeParser =
            nodeId
                .And(nodeShapeParser.Optional())
                .And(shapeAttributes.Optional())
                .And(Literals.Text(":::").SkipAnd(CommonParsers.Identifier).Optional())
                .Map(BuildNode);

        // `A & B` - several nodes sharing the links on either side of the group.
        var nodeGroup =
            nodeParser
                .And(
                    CommonParsers.InlineWhitespace
                        .SkipAnd(Literals.Char('&'))
                        .SkipAnd(CommonParsers.InlineWhitespace)
                        .SkipAnd(nodeParser)
                        .Many())
                .Map((first, more) => new List<Node>([first, .. more]));

        // A dotted link closes with `.-`, `.->`, `..->` etc. - one dot per rank it should span.
        var dottedTail =
            Literals.AnyOf(".")
                .AndSkip(Literals.Char('-'))
                .And(linkHead.Optional())
                .Map((dots, head) => new LinkTail(head.ToNullable(), dots.Length));

        var invisibleTail =
            Literals.AnyOf("~")
                .Then(static _ => _.Length)
                .Assert(_ => _ >= 3, "an invisible link is at least three tildes")
                .Then(static _ => new LinkTail(null, _ - 2));

        // Contiguous links: `-->`, `---`, `--->`, `--o`, `-.->`, `==>`, `====`, `~~~`.
        var contiguousLink =
            OneOf(
                LineTail('-').Then(static _ => new LinkBody(EdgeStyle.Solid, _, null, false)),
                Literals.Char('-').SkipAnd(dottedTail).Then(static _ => new LinkBody(EdgeStyle.Dotted, _, null, false)),
                LineTail('=').Then(static _ => new LinkBody(EdgeStyle.Thick, _, null, false)),
                invisibleTail.Then(static _ => new LinkBody(EdgeStyle.Solid, _, null, true)));

        // Links carrying their label inline: `-- text -->`, `-. text .->`, `== text ==>`.
        var inlineLabeledLink =
            OneOf(
                InlineLabeled("--", EdgeStyle.Solid, LineTail('-')),
                InlineLabeled("-.", EdgeStyle.Dotted, dottedTail),
                InlineLabeled("==", EdgeStyle.Thick, LineTail('=')));

        // A link is a contiguous arrow or an inline-labeled one, optionally opened by `<`, `o` or `x` for the
        // two-headed forms (`<-->`, `o--o`, `x--x`, `<-.->`, `<==>`). Contiguous is tried first so `-->`,
        // `-.->`, `==>` etc. never fall through to the slower inline scan.
        var linkParser =
            linkHeadStart.Optional()
                .And(contiguousLink.Or(inlineLabeledLink))
                .Map((start, body) => ToLink(start.ToNullable(), body));

        // Edge label: |text|
        var edgeLabelParser =
            Between(Literals.Char('|'), Literals.NoneOf("|", minSize: 0), Literals.Char('|'))
                .AsString();

        var flowchartDirection =
            OneOf(
                Literals.Text("TB").Then(Direction.TopToBottom),
                Literals.Text("TD").Then(Direction.TopToBottom),
                Literals.Text("BT").Then(Direction.BottomToTop),
                Literals.Text("LR").Then(Direction.LeftToRight),
                Literals.Text("RL").Then(Direction.RightToLeft)
            );

        // Statement: A --> B & C --> D (a chain of node groups joined by links). Each further link is tried
        // as a unit, so whitespace trailing the last node is left for the end of the statement rather than
        // being taken as the start of a link that is not there.
        var linkedGroup =
            CommonParsers.InlineWhitespace
                .SkipAnd(edgeLabelParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(linkParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(edgeLabelParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(nodeGroup)
                .Map((label1, link, label2, nodes) => (nodes, Link: WithPipeLabel(link, label1, label2)));

        var statementParser =
            nodeGroup
                .And(linkedGroup.Many())
                .Map((first, rest) =>
                    new NodeChainStatement(
                        [first, .. rest.Select(_ => _.nodes)],
                        rest.Select(_ => _.Link).ToList()));

        var nonWhitespaceToken =
            Literals.NoneOf(" \t\r\n").AsString();

        // The rest of the line as it is read, and as the string kept from it.
        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine =
            restOfLineText.AsString();

        var inlineGap =
            Literals.AnyOf(" \t");

        // What may follow a statement on its line: an optional `;`, then a comment, the end of the line or,
        // after a `;`, the next statement.
        var endOfLine =
            CommonParsers.Comment.Or(CommonParsers.LineEnd);

        var statementEnd =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        Literals.Char(';')
                            .SkipAnd(CommonParsers.InlineWhitespace)
                            .SkipAnd(endOfLine.ZeroOrOne()),
                        endOfLine));

        // Style directive: `style NodeName fill:#color,stroke:#color` — applies an inline style to one element.
        var styleDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("style"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((id, props) => new StyleStatement(id, props));

        // Class definition: `classDef className fill:#color,...` (className may be a comma-separated list).
        var classDefDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("classDef"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((names, props) => new ClassDefStatement(names, props));

        // Class application: `class nodeId,nodeId2 className`.
        var classDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("class"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(nonWhitespaceToken)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((ids, className) => new ClassAssignStatement(ids, className));

        // Subgraph start: `subgraph id`, `subgraph id [Label]`, `subgraph "Title"` or `subgraph Some Title`.
        // The rest of the line is sorted into id and title by ParseSubgraphHeader.
        var subgraphStart =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("subgraph"))
                .SkipAnd(inlineGap.SkipAnd(restOfLine).Else(""))
                .AndSkip(CommonParsers.LineEnd)
                .Then(ParseSubgraphHeader);

        // Subgraph end: end
        var subgraphEnd =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("end"))
                .SkipAnd(statementEnd)
                .Then(static _ => new SubgraphEndStatement());

        // Direction statement, e.g. `direction LR` (sets the enclosing subgraph's direction, or the chart's).
        var directionStatement =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("direction"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(flowchartDirection)
                .AndSkip(statementEnd)
                .Then(static _ => new DirectionStatement(_));

        // Link style: `linkStyle 0 stroke:#f00`, `linkStyle 1,2 ...` or `linkStyle default ...`. Links are
        // numbered in the order they are written, from zero.
        var linkStyleDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("linkStyle"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace),
                        Literals.Text("default").Or(Literals.Pattern(char.IsDigit).AsString())))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((targets, props) => new LinkStyleStatement(targets.ToList(), props));

        // Directives that are valid Mermaid but change nothing in what is drawn here: interaction (`click`)
        // and the accessibility title/description. Naming them keeps them
        // from failing the parse. Any other line that matches no rule is an error rather than something to
        // skip, so a statement the parser cannot read is reported instead of vanishing from the diagram.
        var ignoredKeyword =
            OneOf(
                Literals.Text("click"),
                Literals.Text("accTitle"),
                Literals.Text("accDescr"));

        var ignoredDirective =
            CommonParsers.InlineWhitespace
                .SkipAnd(ignoredKeyword.WhenFollowedBy(Literals.AnyOf(" \t:", minSize: 1, maxSize: 1)))
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd);

        // `accDescr { ... }` may run over several lines.
        var multiLineDescription =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accDescr"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char('{'))
                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                .SkipAnd(Literals.Char('}'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd);

        // Empty lines, comments and the ignored directives.
        var skipLine =
            OneOf(
                multiLineDescription,
                ignoredDirective,
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline))
            );

        var nodeStatement =
            CommonParsers.InlineWhitespace
                .SkipAnd(statementParser)
                .AndSkip(statementEnd);

        var item = OneOf(
            subgraphStart.Then<FlowStatement?>(static _ => _),
            subgraphEnd.Then<FlowStatement?>(static _ => _),
            directionStatement.Then<FlowStatement?>(static _ => _),
            // classDef before class (the latter is a prefix of the former); both before nodeStatement so a
            // styling line is never mis-parsed as a node named "class"/"style".
            classDefDirective.Then<FlowStatement?>(static _ => _),
            classDirective.Then<FlowStatement?>(static _ => _),
            styleDirective.Then<FlowStatement?>(static _ => _),
            linkStyleDirective.Then<FlowStatement?>(static _ => _),
            nodeStatement.Then<FlowStatement?>(static _ => _),
            skipLine.Then<FlowStatement?>(static _ => null));

        // The statements must account for the whole input. Without the end-of-input check a line no rule
        // matches would just end the list, and everything after it would be dropped without an error.
        var endOfInput =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.End);

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("flowchart").Or(Literals.Text("graph")))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(flowchartDirection.Else(Direction.TopToBottom))
                .AndSkip(statementEnd)
                .And(item.Many())
                .AndSkip(endOfInput)
                .Map((direction, statements) =>
                    BuildModel(direction, statements.OfType<FlowStatement>().ToList()));
    }

    // A node shape: its opening token, its text, its closing token. The text is either a quoted string -
    // the quotes are dropped, and inside them the characters that would otherwise close the shape are
    // just text, as in `A["array[0] (first)"]` - or bare text running up to the first of `bareEnd`.
    static Parser<(string, NodeShape)> Shape(string open, string close, NodeShape shape, string bareEnd) =>
        Literals.Text(open)
            .SkipAnd(
                CommonParsers.InlineWhitespace
                    .SkipAnd(CommonParsers.DoubleQuotedString)
                    .AndSkip(CommonParsers.InlineWhitespace)
                    .WhenFollowedBy(Literals.Text(close))
                    .Or(Literals.NoneOf(bareEnd, minSize: 0).AsString()))
            .AndSkip(Literals.Text(close))
            .Then(_ => (_, shape));

    // An edge label written in quotes is shown without them.
    static string Unquote(string label)
    {
        var text = label.AsSpan().Trim();
        if (IsQuoted(text))
        {
            return text[1..^1].ToString();
        }

        return label;
    }

    // The closing run of a solid (`-`) or thick (`=`) link: `-->`, `--->`, `--o`, `---`, `----`. Each
    // character beyond the shortest spelling asks for one more rank between the two nodes. A link needs
    // two line characters and a head, or three without one.
    static Parser<LinkTail> LineTail(char line) =>
        Literals.AnyOf(line.ToString())
            .And(linkHead.Optional())
            .Map((run, head) => ToLineTail(run.Length, head.ToNullable()))
            .When(static (_, tail) => tail.Length > 0);

    static LinkTail ToLineTail(int run, char? head)
    {
        if (head is null)
        {
            return new(null, run - 2);
        }

        return new(head, run - 1);
    }

    // Inline edge label, e.g. `-- text -->`, `-. text .->`, `== text ==>`. The text runs up to the first
    // place the closing run matches, which also fixes the head and the length.
    static Parser<LinkBody> InlineLabeled(string start, EdgeStyle style, Parser<LinkTail> tail) =>
        Literals.Text(start)
            .SkipAnd(LineBefore(tail))
            .And(tail)
            .Map((text, close) => new LinkBody(style, close, NullIfEmpty(Unquote(text.Span.Trim().ToString())), false));

    static string? NullIfEmpty(string text)
    {
        if (text.Length == 0)
        {
            return null;
        }

        return text;
    }

    static Link ToLink(char? start, LinkBody body)
    {
        var type = EdgeType.Invisible;
        if (!body.Invisible)
        {
            type = LinkType(start, body.Style, body.Tail.Head);
        }

        return new(type, body.Style, body.Tail.Length, body.Label);
    }

    // The head at the far end decides the type; an opening head only counts when it matches that one.
    static EdgeType LinkType(char? start, EdgeStyle style, char? head) =>
        (head, style) switch
        {
            ('>', _) when start == '<' => EdgeType.BiDirectional,
            ('>', EdgeStyle.Dotted) => EdgeType.DottedArrow,
            ('>', EdgeStyle.Thick) => EdgeType.ThickArrow,
            ('>', _) => EdgeType.Arrow,
            ('o', _) when start == 'o' => EdgeType.BiDirectionalCircle,
            ('o', _) => EdgeType.CircleEnd,
            ('x', _) when start == 'x' => EdgeType.BiDirectionalCross,
            ('x', _) => EdgeType.CrossEnd,
            (_, EdgeStyle.Dotted) => EdgeType.Dotted,
            (_, EdgeStyle.Thick) => EdgeType.Thick,
            _ => EdgeType.Open
        };

    // A `|text|` label before the link wins over an inline one, which wins over a `|text|` after it.
    static Link WithPipeLabel(Link link, Option<string> before, Option<string> after)
    {
        if (before.HasValue)
        {
            return link with { Label = Unquote(before.Value) };
        }

        if (link.Label is null && after.HasValue)
        {
            return link with { Label = Unquote(after.Value) };
        }

        return link;
    }

    static Node BuildNode(
        string id,
        Option<(string Label, NodeShape Shape)> shape,
        Option<string> attributes,
        Option<string> styleClass)
    {
        var node = new Node
        {
            Id = id
        };
        if (shape.HasValue)
        {
            node.Label = shape.Value.Label;
            node.Shape = shape.Value.Shape;
        }

        if (attributes.HasValue)
        {
            ApplyShapeAttributes(node, attributes.Value);
        }

        if (styleClass.HasValue)
        {
            node.Classes.Add(styleClass.Value);
        }

        return node;
    }

    // Applies the `shape` and `label` keys of an `@{ shape: cyl, label: "Database" }` body. A shape with no
    // counterpart here falls back to a rectangle, so the node and its label are still drawn.
    static void ApplyShapeAttributes(Node node, string body)
    {
        foreach (var (key, value) in SplitAttributes(body))
        {
            if (key.Equals("label", StringComparison.OrdinalIgnoreCase))
            {
                node.Label = value;
            }
            else if (key.Equals("shape", StringComparison.OrdinalIgnoreCase))
            {
                node.Shape = ShapeFromName(value);
            }
        }
    }

    // Splits `key: value, key: "quoted, value"` on the commas outside quotes.
    static List<(string Key, string Value)> SplitAttributes(string body)
    {
        var attributes = new List<(string Key, string Value)>();
        var start = 0;
        var quoted = false;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length)
            {
                if (body[i] == '"')
                {
                    quoted = !quoted;
                }

                if (quoted || body[i] != ',')
                {
                    continue;
                }
            }

            var part = body.AsSpan(start, i - start);
            start = i + 1;
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var value = part[(colon + 1)..].Trim();
            if (IsQuoted(value))
            {
                value = value[1..^1];
            }

            attributes.Add((part[..colon].Trim().ToString(), value.ToString()));
        }

        return attributes;
    }

    static NodeShape ShapeFromName(string name) =>
        name.ToLowerInvariant() switch
        {
            "rounded" or "event" => NodeShape.RoundedRectangle,
            "stadium" or "pill" or "terminal" => NodeShape.Stadium,
            "subproc" or "subprocess" or "subroutine" or "fr-rect" or "framed-rectangle" => NodeShape.Subroutine,
            "cyl" or "cylinder" or "database" or "db" => NodeShape.Cylinder,
            "circle" or "circ" => NodeShape.Circle,
            "dbl-circ" or "double-circle" => NodeShape.DoubleCircle,
            "odd" => NodeShape.Asymmetric,
            "diam" or "diamond" or "decision" or "question" => NodeShape.Diamond,
            "hex" or "hexagon" or "prepare" => NodeShape.Hexagon,
            "lean-r" or "lean-right" or "in-out" => NodeShape.Parallelogram,
            "lean-l" or "lean-left" or "out-in" => NodeShape.ParallelogramAlt,
            "trap-b" or "trapezoid" or "trapezoid-bottom" or "priority" => NodeShape.Trapezoid,
            "trap-t" or "trapezoid-top" or "inv-trapezoid" or "manual" => NodeShape.TrapezoidAlt,
            "doc" or "document" => NodeShape.Document,
            _ => NodeShape.Rectangle
        };

    // Sorts the text after `subgraph` into an id and a title:
    //   `one`                   id `one`, titled by its id
    //   `one [Label]`           id `one`, title `Label` (quotes around the label are dropped)
    //   `"Title"`, `Two Words`  a title only; BuildModel gives the subgraph a generated id
    static SubgraphStartStatement ParseSubgraphHeader(string header)
    {
        var text = header.AsSpan().Trim().TrimEnd(';').TrimEnd();
        if (IsQuoted(text))
        {
            return new(null, text[1..^1].ToString());
        }

        var idLength = 0;
        while (idLength < text.Length &&
               (char.IsLetterOrDigit(text[idLength]) || text[idLength] is '_' or '-'))
        {
            idLength++;
        }

        if (idLength > 0)
        {
            var id = text[..idLength].ToString();
            var rest = text[idLength..].TrimStart();
            if (rest.Length == 0)
            {
                return new(id, null);
            }

            if (rest[0] == '[' &&
                rest[^1] == ']')
            {
                var label = rest[1..^1].Trim();
                if (IsQuoted(label))
                {
                    label = label[1..^1];
                }

                return new(id, label.ToString());
            }
        }

        return new(null, text.ToString());
    }

    static bool IsQuoted(CharSpan text) =>
        text.Length >= 2 &&
        text[0] == '"' &&
        text[^1] == '"';

    static FlowchartModel BuildModel(Direction direction, List<FlowStatement> statements)
    {
        var model = new FlowchartModel
        {
            Direction = direction
        };

        var nodeDict = new Dictionary<string, Node>();
        var subgraphStack = new Stack<Subgraph>();
        var assignedToSubgraph = new HashSet<string>();

        // Styling is declarative (forward references allowed), so directives are collected here and
        // resolved onto nodes/subgraphs in one pass after all statements are seen.
        var classDefs = new Dictionary<string, NodeStyle>(StringComparer.Ordinal);
        var inlineStyles = new Dictionary<string, NodeStyle>(StringComparer.Ordinal);
        var classAssignments = new List<(string Id, string ClassName)>();
        var subgraphById = new Dictionary<string, Subgraph>(StringComparer.Ordinal);
        var subgraphCount = 0;
        var linkStyles = new List<LinkStyleStatement>();

        // The subgraphs given a `direction` of their own. The rest follow whatever is around them, which
        // is only known once the whole chart has been read.
        var directed = new HashSet<Subgraph>();

        // An id used in a link may name a subgraph (`A --> two`, `one --> two`), and the subgraph may be
        // declared after the link. Such an id must not also become a node, so the declared ids are
        // gathered before any statement is read.
        var subgraphIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            if (statement is SubgraphStartStatement {Id: { } declaredId})
            {
                subgraphIds.Add(declaredId);
            }
        }

        foreach (var statement in statements)
        {
            switch (statement)
            {
                case SubgraphStartStatement start:
                    // A subgraph declared by title alone gets Mermaid's generated id.
                    var subgraph = new Subgraph
                    {
                        Id = start.Id ?? $"subGraph{subgraphCount}",
                        Title = start.Label ?? start.Id,
                        Direction = direction
                    };
                    subgraphCount++;
                    if (subgraphStack.Count > 0)
                    {
                        subgraphStack.Peek().NestedSubgraphs.Add(subgraph);
                    }
                    else
                    {
                        model.Subgraphs.Add(subgraph);
                    }

                    subgraphById[subgraph.Id] = subgraph;
                    subgraphStack.Push(subgraph);
                    break;

                case SubgraphEndStatement:
                    if (subgraphStack.Count > 0)
                    {
                        subgraphStack.Pop();
                    }

                    break;

                case DirectionStatement dirStatement:
                    if (subgraphStack.Count > 0)
                    {
                        subgraphStack.Peek().Direction = dirStatement.Direction;
                        directed.Add(subgraphStack.Peek());
                    }
                    else
                    {
                        model.Direction = dirStatement.Direction;
                    }

                    break;

                case ClassDefStatement classDef:
                    var defStyle = ParseStyleProps(classDef.Props);
                    foreach (var name in SplitList(classDef.Names))
                    {
                        classDefs[name] = classDefs.TryGetValue(name, out var existingDef)
                            ? existingDef.MergedWith(defStyle)
                            : defStyle;
                    }

                    break;

                case ClassAssignStatement classAssign:
                    var className = classAssign.ClassName.TrimEnd(';');
                    foreach (var id in SplitList(classAssign.Ids))
                    {
                        classAssignments.Add((id, className));
                    }

                    break;

                case LinkStyleStatement linkStyle:
                    linkStyles.Add(linkStyle);
                    break;

                case StyleStatement styleStatement:
                    var inline = ParseStyleProps(styleStatement.Props);
                    inlineStyles[styleStatement.Id] = inlineStyles.TryGetValue(styleStatement.Id, out var existingInline)
                        ? existingInline.MergedWith(inline)
                        : inline;
                    break;

                case NodeChainStatement chain:
                    for (var i = 0; i < chain.Groups.Count; i++)
                    {
                        foreach (var node in chain.Groups[i])
                        {
                            AddNode(node);
                        }

                        // Link every node of this group to every node of the next.
                        if (i < chain.Links.Count)
                        {
                            AddEdges(chain.Groups[i], chain.Groups[i + 1], chain.Links[i]);
                        }
                    }

                    break;
            }
        }

        InheritDirections(model.Subgraphs, model.Direction, directed);
        ApplyLinkStyles(model.Edges, linkStyles);

        // Apply deferred class assignments to nodes (preferred) or subgraphs of the same id.
        foreach (var (id, className) in classAssignments)
        {
            if (nodeDict.TryGetValue(id, out var node))
            {
                if (!node.Classes.Contains(className))
                {
                    node.Classes.Add(className);
                }
            }
            else if (subgraphById.TryGetValue(id, out var subgraph) &&
                     !subgraph.Classes.Contains(className))
            {
                subgraph.Classes.Add(className);
            }
        }

        // `classDef default` styles every node; subgraphs only take explicitly assigned classes/styles.
        classDefs.TryGetValue("default", out var defaultStyle);

        foreach (var node in model.Nodes)
        {
            node.Style = Resolve(node.Classes, node.Id, defaultStyle);
        }

        foreach (var subgraph in subgraphById.Values)
        {
            subgraph.Style = Resolve(subgraph.Classes, subgraph.Id, null);
        }

        return model;

        void AddNode(Node node)
        {
            // The end of a link to or from a subgraph: the edge keeps the id, and the layout resolves it.
            if (subgraphIds.Contains(node.Id))
            {
                return;
            }

            if (!nodeDict.TryGetValue(node.Id, out var existingNode))
            {
                nodeDict[node.Id] = node;
                model.AddNode(node);
            }
            else
            {
                if (node.Label != null &&
                    existingNode.Label == null)
                {
                    existingNode.Label = node.Label;
                    existingNode.Shape = node.Shape;
                }

                // Carry `:::class` from any later reference onto the canonical node.
                foreach (var styleClass in node.Classes)
                {
                    if (!existingNode.Classes.Contains(styleClass))
                    {
                        existingNode.Classes.Add(styleClass);
                    }
                }
            }

            // A node belongs to the subgraph it first appears inside,
            // even if it was first referenced outside one.
            if (subgraphStack.Count > 0 &&
                assignedToSubgraph.Add(node.Id))
            {
                subgraphStack.Peek().NodeIds.Add(node.Id);
            }
        }

        void AddEdges(List<Node> sources, List<Node> targets, Link link)
        {
            foreach (var source in sources)
            {
                foreach (var target in targets)
                {
                    model.Edges.Add(
                        new()
                        {
                            SourceId = source.Id,
                            TargetId = target.Id,
                            Type = link.Type,
                            LineStyle = link.Style,
                            Label = link.Label,
                            MinLength = link.Length
                        });
                }
            }
        }

        NodeStyle? Resolve(List<string> classes, string id, NodeStyle? baseStyle)
        {
            var style = baseStyle;
            foreach (var className in classes)
            {
                if (!classDefs.TryGetValue(className, out var classStyle))
                {
                    continue;
                }

                if (style is null)
                {
                    style = classStyle;
                }
                else
                {
                    style = style.MergedWith(classStyle);
                }
            }

            if (inlineStyles.TryGetValue(id, out var inlineStyle))
            {
                if (style is null)
                {
                    style = inlineStyle;
                }
                else
                {
                    style = style.MergedWith(inlineStyle);
                }
            }

            return style;
        }
    }

    static IEnumerable<string> SplitList(string raw) =>
        raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    // Parses a Mermaid style property list, e.g. `fill:#f9f,stroke:#333,stroke-width:2px`.
    static void InheritDirections(List<Subgraph> subgraphs, Direction enclosing, HashSet<Subgraph> directed)
    {
        foreach (var subgraph in subgraphs)
        {
            if (!directed.Contains(subgraph))
            {
                subgraph.Direction = enclosing;
            }

            InheritDirections(subgraph.NestedSubgraphs, subgraph.Direction, directed);
        }
    }

    // `default` styles every link and a number styles the one written in that place, whichever order the
    // directives come in. A number past the last link styles nothing.
    static void ApplyLinkStyles(List<Edge> edges, List<LinkStyleStatement> statements)
    {
        foreach (var pass in new[] { true, false })
        {
            foreach (var statement in statements)
            {
                var style = ParseStyleProps(WithoutInterpolation(statement.Props));
                foreach (var target in statement.Targets)
                {
                    var isDefault = target == "default";
                    if (isDefault != pass)
                    {
                        continue;
                    }

                    if (isDefault)
                    {
                        foreach (var edge in edges)
                        {
                            edge.Style = edge.Style?.MergedWith(style) ?? style;
                        }
                    }
                    else if (int.TryParse(target, NumberStyles.None, CultureInfo.InvariantCulture, out var index) &&
                             index < edges.Count)
                    {
                        edges[index].Style = edges[index].Style?.MergedWith(style) ?? style;
                    }
                }
            }
        }
    }

    // `linkStyle 0 interpolate basis stroke:red` names a curve before the styles. Every link is drawn
    // with the same curve here, so the name is dropped.
    static string WithoutInterpolation(string props)
    {
        var text = props.TrimStart();
        const string keyword = "interpolate";
        if (!text.StartsWith(keyword, StringComparison.Ordinal))
        {
            return props;
        }

        var rest = text[keyword.Length..].TrimStart();
        var end = rest.IndexOfAny([' ', '\t']);
        if (end < 0)
        {
            return "";
        }

        return rest[end..];
    }

    static NodeStyle ParseStyleProps(string raw)
    {
        var style = new NodeStyle();
        foreach (var part in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }
            // A trailing ';' terminates the directive in Mermaid; drop it from the final value.
            var value = part[(colon + 1)..].Trim().TrimEnd(';').Trim();
            if (value.Length == 0)
            {
                continue;
            }

            var key = part[..colon].Trim().ToLowerInvariant();

            switch (key)
            {
                case "fill":
                    style.Fill = value;
                    break;
                case "stroke":
                    style.Stroke = value;
                    break;
                case "stroke-width":
                    if (TryParseWidth(value, out var width))
                    {
                        style.StrokeWidth = width;
                    }

                    break;
                case "color":
                    style.Color = value;
                    break;
                case "stroke-dasharray":
                    style.StrokeDasharray = value;
                    break;
            }
        }

        return style;
    }

    static bool TryParseWidth(CharSpan value, out double width)
    {
        var trimmed = value.EndsWith("px", StringComparison.OrdinalIgnoreCase)
            ? value[..^2].Trim()
            : value.Trim();
        return double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out width);
    }

    public DiagramParseResult<FlowchartModel> Parse(string input) => parser.ParseDiagram(input);

    // The closing run of a link: the head it ends in (`>`, `o`, `x`, or none) and the ranks it spans.
    readonly record struct LinkTail(char? Head, int Length);

    readonly record struct LinkBody(EdgeStyle Style, LinkTail Tail, string? Label, bool Invisible);

    readonly record struct Link(EdgeType Type, EdgeStyle Style, int Length, string? Label);

    abstract record FlowStatement;

    sealed record NodeChainStatement(List<List<Node>> Groups, List<Link> Links) : FlowStatement;

    sealed record SubgraphStartStatement(string? Id, string? Label) : FlowStatement;

    sealed record SubgraphEndStatement : FlowStatement;

    sealed record DirectionStatement(Direction Direction) : FlowStatement;

    sealed record ClassDefStatement(string Names, string Props) : FlowStatement;

    sealed record ClassAssignStatement(string Ids, string ClassName) : FlowStatement;

    sealed record StyleStatement(string Id, string Props) : FlowStatement;

    sealed record LinkStyleStatement(List<string> Targets, string Props) : FlowStatement;
}

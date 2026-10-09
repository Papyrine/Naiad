class FlowchartParser : IDiagramParser<FlowchartModel>
{
    static Parser<char, FlowchartModel> parser;

    // The head that may close a link, and the one that may open a two-headed link.
    static readonly Parser<char, char> linkHead =
        OneOf(Char('>'), Char('o'), Char('x'));

    static readonly Parser<char, char> linkHeadStart =
        OneOf(Char('<'), Char('o'), Char('x'));

    static FlowchartParser()
    {
        // Node shape parsers - returns (label, shape)
        var doubleCircleShape = Shape("(((", ")))", NodeShape.DoubleCircle, _ => _ != ')');
        var circleShape = Shape("((", "))", NodeShape.Circle, _ => _ != ')');
        var stadiumShape = Shape("([", "])", NodeShape.Stadium, _ => _ != ']');
        var subroutineShape = Shape("[[", "]]", NodeShape.Subroutine, _ => _ != ']');
        var cylinderShape = Shape("[(", ")]", NodeShape.Cylinder, _ => _ != ')');
        var hexagonShape = Shape("{{", "}}", NodeShape.Hexagon, _ => _ != '}');
        var diamondShape = Shape("{", "}", NodeShape.Diamond, _ => _ != '}');
        var roundedShape = Shape("(", ")", NodeShape.RoundedRectangle, _ => _ != ')');
        var rectangleShape = Shape("[", "]", NodeShape.Rectangle, _ => _ != ']');
        var asymmetricShape = Shape(">", "]", NodeShape.Asymmetric, _ => _ != ']');

        // Slash/backslash framed shapes. Bare text excludes / \ ] so the closing token is unambiguous; the
        // opener (`[/` vs `[\`) and closer (`/]` vs `\]`) together pick parallelogram vs trapezoid.
        static bool SlashShapeText(char ch) =>
            ch != '/' && ch != '\\' && ch != ']';

        var parallelogramShape = Shape("[/", "/]", NodeShape.Parallelogram, SlashShapeText);
        var trapezoidShape = Shape("[/", "\\]", NodeShape.Trapezoid, SlashShapeText);
        var parallelogramAltShape = Shape("[\\", "\\]", NodeShape.ParallelogramAlt, SlashShapeText);
        var trapezoidAltShape = Shape("[\\", "/]", NodeShape.TrapezoidAlt, SlashShapeText);

        var nodeShapeParser =
            OneOf(
                Try(doubleCircleShape),
                Try(circleShape),
                Try(stadiumShape),
                Try(subroutineShape),
                Try(cylinderShape),
                Try(hexagonShape),
                Try(diamondShape),
                Try(roundedShape),
                Try(asymmetricShape),
                Try(parallelogramShape),
                Try(trapezoidShape),
                Try(parallelogramAltShape),
                Try(trapezoidAltShape),
                rectangleShape
            );

        // Node id: letters, digits and underscores, plus a hyphen that sits between two of them (`my-node`).
        // A hyphen that starts a link is not part of the id, so `A-->B`, `A-.->B` and `A---B` split into
        // node, link, node without needing spaces around the link.
        var idChar =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_');

        var nodeId =
            idChar
                .Then(idChar.Or(Try(Char('-').Before(Lookahead(idChar)))).SkipMany())
                .Slice((span, _) => span.ToString())
                .Labelled("node id");

        // `@{ shape: cyl, label: "Database" }` - the body is handed to ApplyShapeAttributes as text. A
        // quoted value is taken whole, so it may contain a `}`.
        var shapeAttributes =
            String("@{")
                .Then(
                    OneOf(
                            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"')).Select(_ => $"\"{_}\""),
                            Token(_ => _ != '}' && _ != '"').AtLeastOnceString())
                        .ManyString())
                .Before(Char('}'));

        // Node parser: identifier optionally followed by a shape and/or `@{ ... }` attributes, then an
        // optional `:::class` shorthand whose class name is recorded on the node and resolved against
        // `classDef` styles at model-build time.
        var nodeParser =
            from id in nodeId
            from shape in nodeShapeParser.Optional()
            from attributes in Try(shapeAttributes).Optional()
            from _class in Try(String(":::").Then(CommonParsers.Identifier)).Optional()
            select BuildNode(id, shape, attributes, _class);

        // `A & B` - several nodes sharing the links on either side of the group.
        var nodeGroup =
            from first in nodeParser
            from more in Try(
                CommonParsers.InlineWhitespace
                    .Then(Char('&'))
                    .Then(CommonParsers.InlineWhitespace)
                    .Then(nodeParser)).Many()
            select new List<Node>([first, .. more]);

        // A dotted link closes with `.-`, `.->`, `..->` etc. - one dot per rank it should span.
        var dottedTail =
            from dots in Char('.').SkipAtLeastOnce().Slice((span, _) => span.Length)
            from dash in Char('-')
            from head in linkHead.Optional()
            select new LinkTail(ToNullable(head), dots);

        var invisibleTail =
            Char('~').SkipAtLeastOnce().Slice((span, _) => span.Length)
                .Assert(_ => _ >= 3, "an invisible link is at least three tildes")
                .Select(_ => new LinkTail(null, _ - 2));

        // Contiguous links: `-->`, `---`, `--->`, `--o`, `-.->`, `==>`, `====`, `~~~`.
        var contiguousLink =
            OneOf(
                Try(LineTail('-')).Select(_ => new LinkBody(EdgeStyle.Solid, _, null, false)),
                Try(Char('-').Then(dottedTail)).Select(_ => new LinkBody(EdgeStyle.Dotted, _, null, false)),
                Try(LineTail('=')).Select(_ => new LinkBody(EdgeStyle.Thick, _, null, false)),
                invisibleTail.Select(_ => new LinkBody(EdgeStyle.Solid, _, null, true)));

        // Links carrying their label inline: `-- text -->`, `-. text .->`, `== text ==>`.
        var inlineLabeledLink =
            OneOf(
                Try(InlineLabeled("--", EdgeStyle.Solid, LineTail('-'))),
                Try(InlineLabeled("-.", EdgeStyle.Dotted, dottedTail)),
                InlineLabeled("==", EdgeStyle.Thick, LineTail('=')));

        // A link is a contiguous arrow or an inline-labeled one, optionally opened by `<`, `o` or `x` for the
        // two-headed forms (`<-->`, `o--o`, `x--x`, `<-.->`, `<==>`). Contiguous is tried first so `-->`,
        // `-.->`, `==>` etc. never fall through to the slower inline scan.
        var linkParser =
            from start in linkHeadStart.Optional()
            from body in Try(contiguousLink).Or(inlineLabeledLink)
            select ToLink(ToNullable(start), body);

        // Edge label: |text|
        var edgeLabelParser =
            Char('|')
                .Then(Token(_ => _ != '|').ManyString())
                .Before(Char('|'));

        var flowchartDirection =
            OneOf(
                Try(String("TB")).ThenReturn(Direction.TopToBottom),
                Try(String("TD")).ThenReturn(Direction.TopToBottom),
                Try(String("BT")).ThenReturn(Direction.BottomToTop),
                Try(String("LR")).ThenReturn(Direction.LeftToRight),
                String("RL").ThenReturn(Direction.RightToLeft)
            );

        // Statement: A --> B & C --> D (a chain of node groups joined by links). Each further link is tried
        // as a unit, so whitespace trailing the last node is left for the end of the statement rather than
        // being taken as the start of a link that is not there.
        var statementParser =
            from first in nodeGroup
            from rest in Try(
                from _1 in CommonParsers.InlineWhitespace
                from label1 in edgeLabelParser.Optional()
                from _2 in CommonParsers.InlineWhitespace
                from link in linkParser
                from _3 in CommonParsers.InlineWhitespace
                from label2 in edgeLabelParser.Optional()
                from _4 in CommonParsers.InlineWhitespace
                from nodes in nodeGroup
                select (nodes, Link: WithPipeLabel(link, label1, label2))
            ).Many()
            select new NodeChainStatement(
                [first, .. rest.Select(_ => _.nodes)],
                rest.Select(_ => _.Link).ToList());

        var nonWhitespaceToken =
            Token(_ => _ != ' ' && _ != '\t' && _ != '\r' && _ != '\n').AtLeastOnceString();

        var restOfLine =
            Token(_ => _ != '\r' && _ != '\n').ManyString();

        var inlineGap =
            Token(_ => _ is ' ' or '\t').SkipAtLeastOnce();

        // What may follow a statement on its line: an optional `;`, then a comment, the end of the line or,
        // after a `;`, the next statement.
        var endOfLine =
            Try(CommonParsers.Comment).Or(CommonParsers.LineEnd);

        var statementEnd =
            CommonParsers.InlineWhitespace
                .Then(
                    OneOf(
                        Char(';')
                            .Then(CommonParsers.InlineWhitespace)
                            .Then(endOfLine.Optional())
                            .ThenReturn(Unit.Value),
                        endOfLine));

        // Style directive: `style NodeName fill:#color,stroke:#color` — applies an inline style to one element.
        var styleDirective =
            from _ in CommonParsers.InlineWhitespace
            from __ in String("style")
            from ___ in CommonParsers.RequiredWhitespace
            from id in nonWhitespaceToken
            from ____ in CommonParsers.RequiredWhitespace
            from props in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select new StyleStatement(id, props);

        // Class definition: `classDef className fill:#color,...` (className may be a comma-separated list).
        var classDefDirective =
            from _ in CommonParsers.InlineWhitespace
            from __ in String("classDef")
            from ___ in CommonParsers.RequiredWhitespace
            from names in nonWhitespaceToken
            from ____ in CommonParsers.RequiredWhitespace
            from props in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select new ClassDefStatement(names, props);

        // Class application: `class nodeId,nodeId2 className`.
        var classDirective =
            from _ in CommonParsers.InlineWhitespace
            from __ in String("class")
            from ___ in CommonParsers.RequiredWhitespace
            from ids in nonWhitespaceToken
            from ____ in CommonParsers.RequiredWhitespace
            from className in nonWhitespaceToken
            from _____ in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            select new ClassAssignStatement(ids, className);

        // Subgraph start: `subgraph id`, `subgraph id [Label]`, `subgraph "Title"` or `subgraph Some Title`.
        // The rest of the line is sorted into id and title by ParseSubgraphHeader.
        var subgraphStart =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("subgraph")
            from header in inlineGap.Then(restOfLine).Or(Return(""))
            from lineEnd in CommonParsers.LineEnd
            select ParseSubgraphHeader(header);

        // Subgraph end: end
        var subgraphEnd =
            from _ in CommonParsers.InlineWhitespace
            from end in String("end")
            from ___ in statementEnd
            select new SubgraphEndStatement();

        // Direction statement, e.g. `direction LR` (sets the enclosing subgraph's direction, or the chart's).
        var directionStatement =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("direction")
            from __ in CommonParsers.RequiredWhitespace
            from dir in flowchartDirection
            from ___ in statementEnd
            select new DirectionStatement(dir);

        // Directives that are valid Mermaid but change nothing in what is drawn here: interaction (`click`),
        // per-link styling (`linkStyle`) and the accessibility title/description. Naming them keeps them
        // from failing the parse. Any other line that matches no rule is an error rather than something to
        // skip, so a statement the parser cannot read is reported instead of vanishing from the diagram.
        var ignoredKeyword =
            OneOf(
                Try(String("click")),
                Try(String("linkStyle")),
                Try(String("accTitle")),
                String("accDescr"));

        var ignoredDirective =
            from _ in CommonParsers.InlineWhitespace
            from keyword in ignoredKeyword
            from boundary in Lookahead(Token(_ => _ is ' ' or '\t' or ':'))
            from rest in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select Unit.Value;

        // `accDescr { ... }` may run over several lines.
        var multiLineDescription =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("accDescr")
            from __ in CommonParsers.InlineWhitespace
            from open in Char('{')
            from text in Token(_ => _ != '}').SkipMany()
            from close in Char('}')
            from ___ in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            select Unit.Value;

        // Empty lines, comments and the ignored directives.
        var skipLine =
            OneOf(
                Try(multiLineDescription),
                Try(ignoredDirective),
                CommonParsers.InlineWhitespace.Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline))
            );

        var nodeStatement =
            CommonParsers.InlineWhitespace
                .Then(statementParser)
                .Before(statementEnd);

        var item = OneOf(
            Try(subgraphStart.Select<FlowStatement?>(_ => _)),
            Try(subgraphEnd.Select<FlowStatement?>(_ => _)),
            Try(directionStatement.Select<FlowStatement?>(_ =>  _)),
            // classDef before class (the latter is a prefix of the former); both before nodeStatement so a
            // styling line is never mis-parsed as a node named "class"/"style".
            Try(classDefDirective.Select<FlowStatement?>(_ =>  _)),
            Try(classDirective.Select<FlowStatement?>(_ => _)),
            Try(styleDirective.Select<FlowStatement?>(_ => _)),
            Try(nodeStatement.Select<FlowStatement?>(_ => _)),
            skipLine.Select<FlowStatement?>(_ => null));

        // The statements must account for the whole input. Without the end-of-input check a line no rule
        // matches would just end the list, and everything after it would be dropped without an error.
        var endOfInput =
            Try(CommonParsers.InlineWhitespace.Then(End));

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in Try(String("flowchart")).Or(String("graph"))
            from __ in CommonParsers.InlineWhitespace
            from direction in flowchartDirection.Optional()
            from ___ in statementEnd
            from statements in item.ManyThen(endOfInput)
            select BuildModel(
                direction.GetValueOrDefault(Direction.TopToBottom),
                statements.Item1.OfType<FlowStatement>().ToList());
    }

    // A node shape: its opening token, its text, its closing token. The text is either a quoted string -
    // the quotes are dropped, and inside them the characters that would otherwise close the shape are
    // just text, as in `A["array[0] (first)"]` - or bare text running up to the closing token.
    static Parser<char, (string, NodeShape)> Shape(string open, string close, NodeShape shape, Func<char, bool> bare) =>
        String(open)
            .Then(
                Try(
                        CommonParsers.InlineWhitespace
                            .Then(CommonParsers.DoubleQuotedString)
                            .Before(CommonParsers.InlineWhitespace)
                            .Before(Lookahead(Try(String(close)))))
                    .Or(Token(bare).ManyString()))
            .Before(String(close))
            .Select(text => (text, shape));

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

    static char? ToNullable(Maybe<char> value)
    {
        if (value.HasValue)
        {
            return value.Value;
        }

        return null;
    }

    // The closing run of a solid (`-`) or thick (`=`) link: `-->`, `--->`, `--o`, `---`, `----`. Each
    // character beyond the shortest spelling asks for one more rank between the two nodes.
    static Parser<char, LinkTail> LineTail(char line) =>
        (from run in Char(line).SkipAtLeastOnce().Slice((span, _) => span.Length)
            from head in linkHead.Optional()
            select ToLineTail(run, ToNullable(head)))
        .Assert(_ => _.Length > 0, "a link needs two line characters and a head, or three without one");

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
    static Parser<char, LinkBody> InlineLabeled(string start, EdgeStyle style, Parser<char, LinkTail> tail) =>
        from open in String(start)
        from text in Token(_ => _ != '\r' && _ != '\n').Until(Lookahead(Try(tail)))
        from close in tail
        select new LinkBody(style, close, NullIfEmpty(Unquote(new string(text.ToArray()).Trim())), false);

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
    static Link WithPipeLabel(Link link, Maybe<string> before, Maybe<string> after)
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
        Maybe<(string Label, NodeShape Shape)> shape,
        Maybe<string> attributes,
        Maybe<string> styleClass)
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

    public Result<char, FlowchartModel> Parse(string input) => parser.Parse(input);

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
}

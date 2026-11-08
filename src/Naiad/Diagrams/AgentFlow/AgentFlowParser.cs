namespace Naiad.Diagrams.AgentFlow;

/// <summary>
/// Reads Mermaid's <c>agentflow-beta</c> syntax. Its surface is flowchart's - nodes with bracketed labels,
/// chained links, <c>@{ … }</c> metadata - narrowed to three link operators and widened with <c>flow</c>,
/// <c>global</c> and <c>connector</c> blocks.
/// </summary>
class AgentFlowParser : IDiagramParser<AgentFlowModel>
{
    static Parser<AgentFlowModel> parser;

    // Mermaid's agent flow aliases, onto the canonical shape names they stand for.
    static readonly Dictionary<string, string> shapeAliases = new(StringComparer.Ordinal)
    {
        ["task"] = "roundedRect",
        ["tool"] = "subroutine",
        ["input"] = "lean-right",
        ["lean_right"] = "lean-right",
        ["decision"] = "diamond",
        ["refdoc"] = "lin-doc",
        ["action"] = "hexagon",
        ["round"] = "rect"
    };

    static AgentFlowParser()
    {
        // Node id: letters, digits and underscores, plus a hyphen between two of them. A hyphen that starts
        // a link is not part of the id, so `a-->b` splits into node, link, node.
        var idChars =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_');

        var nodeId =
            Capture(idChars.And(ZeroOrMany(Literals.Char('-').And(idChars))))
                .AsString();

        // The bracket forms flowchart offers. Agent flow keeps the square, round, diamond, hexagon,
        // subroutine and lean-right ones; the rest name shapes it removed, which become its default.
        const string slashEnd = "/\\]";
        var bracketShape =
            OneOf(
                Bracket("(((", ")))", "doublecircle", ")"),
                Bracket("((", "))", "circle", ")"),
                Bracket("([", "])", "stadium", "]"),
                Bracket("[[", "]]", "subroutine", "]"),
                Bracket("[(", ")]", "cylinder", ")"),
                Bracket("{{", "}}", "hexagon", "}"),
                Bracket("{", "}", "diamond", "}"),
                Bracket("(", ")", "round", ")"),
                Bracket(">", "]", "odd", "]"),
                Bracket("[/", "/]", "lean_right", slashEnd),
                Bracket("[/", "\\]", "trapezoid", slashEnd),
                Bracket("[\\", "\\]", "lean_left", slashEnd),
                Bracket("[\\", "/]", "inv_trapezoid", slashEnd),
                Bracket("[", "]", "square", "]"));

        // `@{ shape: tool, label: "Fetch" }`, on one line or several. The body is kept as text and read by
        // ReadMetadata; a quoted value is taken whole, so it may hold a `}`.
        var metadata =
            Literals.Text("@{")
                .SkipAnd(
                    Capture(
                        OneOf(
                                Capture(Literals.Char('"').And(Literals.NoneOf("\"", minSize: 0)).And(Literals.Char('"'))),
                                Capture(Literals.Char('\'').And(Literals.NoneOf("'", minSize: 0)).And(Literals.Char('\''))),
                                Literals.NoneOf("}\"'"))
                            .Many()))
                .AndSkip(Literals.Char('}'))
                .AsString();

        var styleClass =
            Literals.Text(":::").SkipAnd(CommonParsers.Identifier);

        // A node, with its bracketed label, then any mix of metadata blocks and `:::class` suffixes.
        var nodeSuffix =
            metadata.Then<(string? Metadata, string? Class)>(static _ => (_, null))
                .Or(styleClass.Then<(string? Metadata, string? Class)>(static _ => (null, _)));

        var node =
            nodeId
                .And(bracketShape.Optional())
                .And(nodeSuffix.Many())
                .Map(static (id, shape, suffixes) =>
                    new NodeReference(
                        id,
                        shape.HasValue ? shape.Value.Label : null,
                        shape.HasValue ? shape.Value.Type : null,
                        suffixes.Where(static _ => _.Metadata is not null).Select(static _ => _.Metadata!).ToList(),
                        suffixes.Where(static _ => _.Class is not null).Select(static _ => _.Class!).ToList()));

        // `a & b`: several nodes sharing the links on either side.
        var nodeGroup =
            node
                .And(
                    CommonParsers.InlineWhitespace
                        .SkipAnd(Literals.Char('&'))
                        .SkipAnd(CommonParsers.InlineWhitespace)
                        .SkipAnd(node)
                        .Many())
                .Map(static (first, more) => new List<NodeReference>([first, .. more]));

        // The three operators, each with a meaning of its own. A longer run of dashes or dots asks for
        // more ranks between the two ends, as in a flowchart.
        var dashes = Literals.AnyOf("-", minSize: 2);
        var closingLink =
            OneOf(
                dashes.AndSkip(Literals.Char('>')).Then(static _ => (Semantic: AgentFlowEdgeSemantic.Sequence, Length: _.Length - 1)),
                dashes.AndSkip(Literals.Char('x')).Then(static _ => (Semantic: AgentFlowEdgeSemantic.Failure, Length: _.Length - 1)));

        var referenceLink =
            Literals.Char('-')
                .SkipAnd(Literals.AnyOf("."))
                .AndSkip(Literals.Char('-'))
                .Then(static _ => (Semantic: AgentFlowEdgeSemantic.Reference, Length: _.Length));

        // `-- text -->` and `-- text --x`: the text runs up to the first closing operator.
        var inlineLabeled =
            Literals.Text("--")
                .SkipAnd(LineBefore(closingLink))
                .And(closingLink)
                .Map(static (text, close) => (close.Semantic, close.Length, Label: NullIfEmpty(Unquote(text.Span.Trim().ToString()))));

        var operatorParser =
            OneOf(
                closingLink.Then(static _ => (_.Semantic, _.Length, Label: (string?) null)),
                referenceLink.Then(static _ => (_.Semantic, _.Length, Label: (string?) null)),
                inlineLabeled);

        // `a e1@--> b` names the edge, so metadata can be attached to it later with `e1@{ … }`.
        var edgeId =
            nodeId.AndSkip(Literals.Char('@')).WhenFollowedBy(Literals.Char('-'));

        var pipeLabel =
            Between(Literals.Char('|'), Literals.NoneOf("|", minSize: 0), Literals.Char('|'))
                .AsString();

        var linkedGroup =
            CommonParsers.InlineWhitespace
                .SkipAnd(edgeId.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(operatorParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(pipeLabel.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(nodeGroup)
                .Map(static (id, link, pipe, nodes) =>
                    (Nodes: nodes,
                        Link: new LinkReference(
                            link.Semantic,
                            Math.Max(1, link.Length),
                            link.Label ?? (pipe.HasValue ? NullIfEmpty(Unquote(pipe.Value)) : null),
                            id.HasValue ? id.Value : null)));

        var chain =
            nodeGroup
                .And(linkedGroup.Many())
                .Map(static (first, rest) =>
                    (Statement) new ChainStatement(
                        [first, .. rest.Select(static _ => _.Nodes)],
                        rest.Select(static _ => _.Link).ToList()));

        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine =
            restOfLineText.AsString();

        var nonWhitespaceToken =
            Literals.NoneOf(" \t\r\n").AsString();

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
                            .AndSkip(endOfLine.Optional()),
                        endOfLine));

        // A keyword only counts as one when a word does not carry on after it, so `endpoint` and `flows`
        // are still node ids.
        static Parser<string> Keyword(string text) =>
            Literals.Text(text)
                .WhenNotFollowedBy(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '-'));

        var label =
            Bracket("[", "]", "square", "]").Then(static _ => _.Label);

        // `flow id["Label"]@{ … }`, every part after the keyword optional.
        var flowStart =
            Keyword("flow")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(nodeId.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(label.Optional())
                .And(metadata.Optional())
                .AndSkip(statementEnd)
                .Map(static (id, title, body) =>
                    (Statement) new FlowStartStatement(
                        id.HasValue ? id.Value : null,
                        title.HasValue ? title.Value : null,
                        body.HasValue ? body.Value : null));

        var globalStart =
            Keyword("global")
                .AndSkip(statementEnd)
                .Then(static _ => (Statement) new GlobalStartStatement());

        var blockEnd =
            Keyword("end")
                .AndSkip(statementEnd)
                .Then(static _ => (Statement) new EndStatement());

        var connector =
            Keyword("connector")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(nodeId)
                .And(label.Optional())
                .And(metadata.Optional())
                .AndSkip(statementEnd)
                .Map(static (id, title, body) =>
                    (Statement) new ConnectorStatement(
                        id,
                        title.HasValue ? title.Value : null,
                        body.HasValue ? body.Value : null));

        var direction =
            Keyword("direction")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.DirectionParser)
                .AndSkip(statementEnd)
                .Then(static _ => (Statement) new DirectionStatement(_));

        var classDef =
            Keyword("classDef")
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (names, properties) => (Statement) new ClassDefStatement(names, properties));

        var classAssign =
            Keyword("class")
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(nonWhitespaceToken)
                .AndSkip(statementEnd)
                .Map(static (ids, className) => (Statement) new ClassAssignStatement(ids, className.TrimEnd(';')));

        var style =
            Keyword("style")
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nonWhitespaceToken)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, properties) => (Statement) new StyleStatement(id, properties));

        var accessibilityTitle =
            Literals.Text("accTitle")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => (Statement) new AccessibilityStatement(_.Trim(), null));

        var accessibilityDescription =
            Literals.Text("accDescr")
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Literals.Char(':')
                        .SkipAnd(restOfLine)
                        .Or(
                            Literals.Char('{')
                                .SkipAnd(Literals.NoneOf("}", minSize: 0).AsString())
                                .AndSkip(Literals.Char('}'))
                                .AndSkip(CommonParsers.InlineWhitespace)))
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => (Statement) new AccessibilityStatement(null, _.Trim()));

        // Valid lines that change nothing in what is drawn here: link styling and interaction.
        var ignored =
            Keyword("linkStyle").Or(Keyword("click"))
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd)
                .Then(static _ => (Statement?) null);

        var skipLine =
            CommonParsers.Comment.Or(CommonParsers.Newline)
                .Then(static _ => (Statement?) null);

        // Keywords before the node chain, so a keyword line is never read as a node named after it.
        var statement =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(
                        skipLine,
                        flowStart.Then(static _ => (Statement?) _),
                        globalStart.Then(static _ => (Statement?) _),
                        blockEnd.Then(static _ => (Statement?) _),
                        connector.Then(static _ => (Statement?) _),
                        direction.Then(static _ => (Statement?) _),
                        classDef.Then(static _ => (Statement?) _),
                        classAssign.Then(static _ => (Statement?) _),
                        style.Then(static _ => (Statement?) _),
                        accessibilityTitle.Then(static _ => (Statement?) _),
                        accessibilityDescription.Then(static _ => (Statement?) _),
                        ignored,
                        chain.AndSkip(statementEnd).Then(static _ => (Statement?) _)));

        // The statements must account for the whole input, so a line no rule matches is reported rather
        // than ending the diagram early.
        var endOfInput =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.End);

        // Mermaid only knows the diagram by its -beta keyword.
        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("agentflow-beta"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.DirectionParser.Else(Direction.TopToBottom))
                .AndSkip(statementEnd)
                .And(
                    statement.Many()
                        .Then(static _ => _.OfType<Statement>().ToList())
                        .Assert(BlocksBalance, "every flow and global block must be closed by exactly one end"))
                .AndSkip(endOfInput)
                .Map(BuildModel);
    }

    public DiagramParseResult<AgentFlowModel> Parse(string input) => parser.ParseDiagram(input);

    // A bracketed label: its opening token, its text, its closing token. Quoted text loses its quotes and
    // may hold the closing characters; bare text runs up to the first of `bareEnd`.
    static Parser<(string Label, string Type)> Bracket(string open, string close, string type, string bareEnd) =>
        Literals.Text(open)
            .SkipAnd(
                CommonParsers.InlineWhitespace
                    .SkipAnd(CommonParsers.DoubleQuotedString)
                    .AndSkip(CommonParsers.InlineWhitespace)
                    .WhenFollowedBy(Literals.Text(close))
                    .Or(Literals.NoneOf(bareEnd, minSize: 0).Then(static _ => _.Span.Trim().ToString())))
            .AndSkip(Literals.Text(close))
            .Then(_ => (_, type));

    static bool BlocksBalance(List<Statement> statements)
    {
        var depth = 0;
        foreach (var statement in statements)
        {
            if (statement is FlowStartStatement or GlobalStartStatement)
            {
                depth++;
            }
            else if (statement is EndStatement &&
                     --depth < 0)
            {
                return false;
            }
        }

        return depth == 0;
    }

    static string Unquote(string text)
    {
        var trimmed = text.AsSpan().Trim();
        if (trimmed.Length >= 2 &&
            trimmed[0] == '"' &&
            trimmed[^1] == '"')
        {
            return trimmed[1..^1].ToString();
        }

        return trimmed.ToString();
    }

    static string? NullIfEmpty(string text) =>
        text.Length == 0 ? null : text;

    /// <summary>
    /// The <c>key: value</c> pairs of a metadata block. Mermaid reads the block as YAML; the pairs here are
    /// split on the commas and line breaks that sit outside quotes and brackets, which covers the single-line
    /// flow form and the multi-line block form alike. A nested value is kept as its text. The keys Mermaid
    /// strips to keep a consumer from polluting a prototype are dropped here too.
    /// </summary>
    internal static List<(string Key, string Value)> ReadMetadata(string body)
    {
        var pairs = new List<(string Key, string Value)>();
        var start = 0;
        char? quote = null;
        var depth = 0;
        for (var i = 0; i <= body.Length; i++)
        {
            if (i < body.Length)
            {
                var ch = body[i];
                if (quote is { } open)
                {
                    if (ch == open)
                    {
                        quote = null;
                    }

                    continue;
                }

                if (ch is '"' or '\'')
                {
                    quote = ch;
                    continue;
                }

                if (ch is '[' or '{')
                {
                    depth++;
                    continue;
                }

                if (ch is ']' or '}')
                {
                    depth = Math.Max(0, depth - 1);
                    continue;
                }

                if (depth > 0 ||
                    ch is not (',' or '\n' or '\r'))
                {
                    continue;
                }
            }

            var part = body.AsSpan(start, i - start).Trim();
            start = i + 1;
            var colon = part.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = part[..colon].Trim().ToString();
            if (key is "__proto__" or "constructor" or "prototype")
            {
                continue;
            }

            var value = part[(colon + 1)..].Trim();
            if (value.Length >= 2 &&
                value[0] is '"' or '\'' &&
                value[^1] == value[0])
            {
                value = value[1..^1];
            }

            pairs.Add((key, value.ToString()));
        }

        return pairs;
    }

    /// <summary>
    /// Mermaid's alias resolution and shape normalisation: an alias becomes the shape it stands for, the
    /// plain rectangle becomes agent flow's rounded default, and a shape agent flow does not draw - one it
    /// removed, or one it never had - falls back to that default rather than failing the diagram.
    /// </summary>
    internal static NodeShape ResolveShape(string? name)
    {
        if (name is not null &&
            shapeAliases.TryGetValue(name, out var canonical))
        {
            name = canonical;
        }

        return name switch
        {
            "subroutine" or "subprocess" or "subproc" or "framed-rectangle" => NodeShape.Subroutine,
            "lean-right" => NodeShape.Parallelogram,
            "diamond" => NodeShape.Diamond,
            "lin-doc" or "lined-document" => NodeShape.LinedDocument,
            "hexagon" or "hex" => NodeShape.Hexagon,
            _ => NodeShape.RoundedRectangle
        };
    }

    static AgentFlowModel BuildModel(Direction direction, List<Statement> statements) =>
        new ModelBuilder(direction, statements).Build();

    sealed class ModelBuilder(Direction direction, List<Statement> statements)
    {
        readonly AgentFlowModel model = new()
        {
            Direction = direction
        };

        readonly Dictionary<string, AgentFlowNode> nodes = new(StringComparer.Ordinal);
        readonly Dictionary<string, string?> shapeNames = new(StringComparer.Ordinal);
        readonly Dictionary<string, AgentFlowContainer> containers = new(StringComparer.Ordinal);
        readonly Dictionary<string, int> containerOrder = new(StringComparer.Ordinal);
        readonly Dictionary<string, AgentFlowEdge> edgesById = new(StringComparer.Ordinal);
        readonly HashSet<string> containerIds = new(StringComparer.Ordinal);
        readonly HashSet<string> globalIds = new(StringComparer.Ordinal);
        readonly HashSet<string> directed = new(StringComparer.Ordinal);
        readonly List<(string ContainerId, List<string> Members)> claims = [];
        readonly List<(string Id, string Body)> containerMetadata = [];
        readonly Stack<Frame> frames = new();
        readonly Dictionary<string, NodeStyle> classDefinitions = new(StringComparer.Ordinal);
        readonly Dictionary<string, NodeStyle> inlineStyles = new(StringComparer.Ordinal);
        readonly List<(string Id, string ClassName)> classAssignments = [];
        int flowCount;
        int startCount;

        public AgentFlowModel Build()
        {
            // An id may be used in a link, or given metadata, before the flow it names is declared, and must
            // not become a node, so the declared flow ids are gathered first.
            foreach (var statement in statements)
            {
                if (statement is FlowStartStatement { Id: { } id })
                {
                    containerIds.Add(id);
                }
            }

            frames.Push(new(null, null, null, 0, false));
            foreach (var statement in statements)
            {
                Apply(statement);
            }

            AssembleContainers();
            foreach (var (id, body) in containerMetadata)
            {
                if (containers.TryGetValue(id, out var container))
                {
                    foreach (var (key, value) in ReadMetadata(body))
                    {
                        container.Metadata[key] = value;
                    }
                }
            }

            foreach (var node in model.AgentNodes)
            {
                node.Shape = ResolveShape(shapeNames.GetValueOrDefault(node.Id));
            }

            ApplyStyles();
            return model;
        }

        void Apply(Statement statement)
        {
            switch (statement)
            {
                case FlowStartStatement start:
                    frames.Push(new(start.Id, start.Label, start.Metadata, startCount++, false));
                    break;

                case GlobalStartStatement:
                    frames.Push(new(null, null, null, 0, true));
                    break;

                case EndStatement:
                    Close(frames.Pop());
                    break;

                case ConnectorStatement connector:
                    var connectorNode = GetOrAddNode(connector.Id);
                    connectorNode.Kind = AgentFlowNodeKind.Connector;
                    if (!string.IsNullOrWhiteSpace(connector.Label))
                    {
                        connectorNode.Label = connector.Label;
                    }

                    if (connector.Metadata is { } connectorBody)
                    {
                        ApplyNodeMetadata(connectorNode, connectorBody);
                    }

                    frames.Peek().Members.Add(connector.Id);
                    break;

                case DirectionStatement { Direction: var value }:
                    var frame = frames.Peek();
                    if (frame.IsGlobal)
                    {
                        break;
                    }

                    if (frames.Count == 1)
                    {
                        model.Direction = value;
                    }
                    else
                    {
                        frame.Direction = value;
                    }

                    break;

                case ChainStatement chain:
                    ApplyChain(chain);
                    break;

                case ClassDefStatement classDef:
                    var definition = FlowchartParser.ParseStyleProps(classDef.Properties);
                    foreach (var name in SplitList(classDef.Names))
                    {
                        classDefinitions[name] = classDefinitions.TryGetValue(name, out var existing)
                            ? existing.MergedWith(definition)
                            : definition;
                    }

                    break;

                case ClassAssignStatement classAssign:
                    foreach (var id in SplitList(classAssign.Ids))
                    {
                        classAssignments.Add((id, classAssign.ClassName));
                    }

                    break;

                case StyleStatement styleStatement:
                    var inline = FlowchartParser.ParseStyleProps(styleStatement.Properties);
                    inlineStyles[styleStatement.Id] = inlineStyles.TryGetValue(styleStatement.Id, out var existingInline)
                        ? existingInline.MergedWith(inline)
                        : inline;
                    break;

                case AccessibilityStatement accessibility:
                    model.AccessibilityTitle = accessibility.Title ?? model.AccessibilityTitle;
                    model.AccessibilityDescription = accessibility.Description ?? model.AccessibilityDescription;
                    break;
            }
        }

        // Mermaid creates a flow when its block closes, so an anonymous flow's generated id and the order in
        // which flows claim their members both follow the order the blocks close in.
        void Close(Frame frame)
        {
            var parent = frames.Peek();
            if (frame.IsGlobal)
            {
                foreach (var member in frame.Members)
                {
                    globalIds.Add(member);
                }

                return;
            }

            var id = frame.Id ?? $"subGraph{flowCount}";
            flowCount++;

            if (!containers.TryGetValue(id, out var container))
            {
                container = new()
                {
                    Id = id
                };
                containers[id] = container;
                containerOrder[id] = frame.Order;
            }

            // A later block with the same id adds to the first, and its label and direction win.
            if (!string.IsNullOrWhiteSpace(frame.Label))
            {
                container.Title = frame.Label;
            }

            if (frame.Direction is { } frameDirection)
            {
                container.Direction = frameDirection;
                directed.Add(id);
            }

            if (frame.Metadata is { } body)
            {
                containerMetadata.Add((id, body));
            }

            claims.Add((id, frame.Members));
            parent.Members.Add(id);
        }

        void ApplyChain(ChainStatement chain)
        {
            var frame = frames.Peek();

            // `x@{ … }` on its own, where x names a flow or an edge, gives that its metadata instead of
            // declaring a node.
            if (chain.Links.Count == 0 &&
                chain.Groups[0] is [{ Label: null, Type: null } lone] &&
                lone.Metadata.Count > 0)
            {
                if (containerIds.Contains(lone.Id))
                {
                    foreach (var body in lone.Metadata)
                    {
                        containerMetadata.Add((lone.Id, body));
                    }

                    frame.Members.Add(lone.Id);
                    return;
                }

                if (!nodes.ContainsKey(lone.Id) &&
                    edgesById.TryGetValue(lone.Id, out var edge))
                {
                    foreach (var body in lone.Metadata)
                    {
                        foreach (var (key, value) in ReadMetadata(body))
                        {
                            edge.Metadata[key] = value;
                        }
                    }

                    frame.Members.Add(lone.Id);
                    return;
                }
            }

            foreach (var group in chain.Groups)
            {
                foreach (var reference in group)
                {
                    ApplyNode(reference);
                    frame.Members.Add(reference.Id);
                }
            }

            for (var i = 0; i < chain.Links.Count; i++)
            {
                var link = chain.Links[i];
                foreach (var source in chain.Groups[i])
                {
                    foreach (var target in chain.Groups[i + 1])
                    {
                        var edge = new AgentFlowEdge
                        {
                            Id = link.Id,
                            SourceId = source.Id,
                            TargetId = target.Id,
                            Semantic = link.Semantic,
                            Label = link.Label,
                            MinLength = link.Length,
                            Type = link.Semantic switch
                            {
                                AgentFlowEdgeSemantic.Reference => EdgeType.Dotted,
                                AgentFlowEdgeSemantic.Failure => EdgeType.CrossEnd,
                                _ => EdgeType.Arrow
                            },
                            LineStyle = link.Semantic == AgentFlowEdgeSemantic.Reference ? EdgeStyle.Dotted : EdgeStyle.Solid
                        };
                        model.AddEdge(edge);
                        if (link.Id is { } edgeId)
                        {
                            edgesById.TryAdd(edgeId, edge);
                        }
                    }
                }
            }
        }

        void ApplyNode(NodeReference reference)
        {
            // A flow's id used as a node is a link to or from the flow; the layout resolves it.
            if (containerIds.Contains(reference.Id))
            {
                foreach (var body in reference.Metadata)
                {
                    containerMetadata.Add((reference.Id, body));
                }

                return;
            }

            var node = GetOrAddNode(reference.Id);
            if (reference.Label is not null)
            {
                node.Label = reference.Label;
            }

            if (reference.Type is not null)
            {
                shapeNames[node.Id] = reference.Type;
            }

            foreach (var body in reference.Metadata)
            {
                ApplyNodeMetadata(node, body);
            }

            foreach (var className in reference.Classes)
            {
                classAssignments.Add((node.Id, className));
            }
        }

        void ApplyNodeMetadata(AgentFlowNode node, string body)
        {
            foreach (var (key, value) in ReadMetadata(body))
            {
                node.Metadata[key] = value;
                if (key == "shape")
                {
                    shapeNames[node.Id] = value;
                }
                else if (key == "label")
                {
                    node.Label = value;
                }
            }
        }

        AgentFlowNode GetOrAddNode(string id)
        {
            if (!nodes.TryGetValue(id, out var node))
            {
                node = new()
                {
                    Id = id,
                    Label = id
                };
                nodes[id] = node;
                model.AddNode(node);
            }

            return node;
        }

        /// <summary>
        /// Each flow claims the ids referenced inside it that no flow closed before it has claimed, so a node
        /// belongs to the innermost flow it is mentioned in, and to the first of two sibling flows. An id in
        /// a <c>global</c> block is never claimed, wherever that block is, and a claim that would put a flow
        /// inside itself is dropped, as Mermaid drops it.
        /// </summary>
        void AssembleContainers()
        {
            var parents = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var (containerId, members) in claims)
            {
                foreach (var member in members)
                {
                    if (member == containerId ||
                        globalIds.Contains(member) ||
                        parents.ContainsKey(member) ||
                        !(nodes.ContainsKey(member) || containers.ContainsKey(member)) ||
                        WouldCycle(member, containerId))
                    {
                        continue;
                    }

                    parents[member] = containerId;
                }
            }

            foreach (var container in containers.Values.OrderBy(_ => containerOrder[_.Id]))
            {
                if (parents.TryGetValue(container.Id, out var parentId))
                {
                    containers[parentId].NestedSubgraphs.Add(container);
                }
                else
                {
                    model.Subgraphs.Add(container);
                }
            }

            InheritDirections(model.Subgraphs, model.Direction);

            foreach (var node in model.Nodes)
            {
                if (parents.TryGetValue(node.Id, out var parentId))
                {
                    containers[parentId].NodeIds.Add(node.Id);
                }
            }

            // A flow without a `direction` of its own follows the one around it.
            void InheritDirections(List<Subgraph> subgraphs, Direction enclosing)
            {
                foreach (var subgraph in subgraphs)
                {
                    if (!directed.Contains(subgraph.Id))
                    {
                        subgraph.Direction = enclosing;
                    }

                    InheritDirections(subgraph.NestedSubgraphs, subgraph.Direction);
                }
            }

            bool WouldCycle(string member, string containerId)
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                for (var current = containerId; current is not null && seen.Add(current); current = parents.GetValueOrDefault(current))
                {
                    if (current == member)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        void ApplyStyles()
        {
            foreach (var (id, className) in classAssignments)
            {
                if (nodes.TryGetValue(id, out var node))
                {
                    if (!node.Classes.Contains(className))
                    {
                        node.Classes.Add(className);
                    }
                }
                else if (containers.TryGetValue(id, out var container) &&
                         !container.Classes.Contains(className))
                {
                    container.Classes.Add(className);
                }
            }

            // `classDef default` styles every node; flows only take the classes and styles given to them.
            classDefinitions.TryGetValue("default", out var defaultStyle);
            foreach (var node in model.Nodes)
            {
                node.Style = Resolve(node.Classes, node.Id, defaultStyle);
            }

            foreach (var container in containers.Values)
            {
                container.Style = Resolve(container.Classes, container.Id, null);
            }
        }

        NodeStyle? Resolve(List<string> classes, string id, NodeStyle? baseStyle)
        {
            var style = baseStyle;
            foreach (var className in classes)
            {
                if (classDefinitions.TryGetValue(className, out var classStyle))
                {
                    style = style is null ? classStyle : style.MergedWith(classStyle);
                }
            }

            if (inlineStyles.TryGetValue(id, out var inlineStyle))
            {
                style = style is null ? inlineStyle : style.MergedWith(inlineStyle);
            }

            return style;
        }

        static IEnumerable<string> SplitList(string raw) =>
            raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        sealed class Frame(string? id, string? label, string? metadata, int order, bool isGlobal)
        {
            public string? Id { get; } = id;
            public string? Label { get; } = label;
            public string? Metadata { get; } = metadata;
            public int Order { get; } = order;
            public bool IsGlobal { get; } = isGlobal;
            public Direction? Direction { get; set; }
            public List<string> Members { get; } = [];
        }
    }

    sealed record NodeReference(string Id, string? Label, string? Type, List<string> Metadata, List<string> Classes);

    readonly record struct LinkReference(AgentFlowEdgeSemantic Semantic, int Length, string? Label, string? Id);

    abstract record Statement;

    sealed record ChainStatement(List<List<NodeReference>> Groups, List<LinkReference> Links) : Statement;

    sealed record FlowStartStatement(string? Id, string? Label, string? Metadata) : Statement;

    sealed record GlobalStartStatement : Statement;

    sealed record EndStatement : Statement;

    sealed record ConnectorStatement(string Id, string? Label, string? Metadata) : Statement;

    sealed record DirectionStatement(Direction Direction) : Statement;

    sealed record ClassDefStatement(string Names, string Properties) : Statement;

    sealed record ClassAssignStatement(string Ids, string ClassName) : Statement;

    sealed record StyleStatement(string Id, string Properties) : Statement;

    sealed record AccessibilityStatement(string? Title, string? Description) : Statement;
}

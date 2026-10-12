class C4Parser : IDiagramParser<C4Model>
{
    static Parser<C4Model> parser;

    // Every element keyword, without the `_Ext` that marks an element as external.
    static readonly Dictionary<string, C4ElementType> elementTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Person"] = C4ElementType.Person,
        ["System"] = C4ElementType.System,
        ["SystemDb"] = C4ElementType.SystemDb,
        ["SystemQueue"] = C4ElementType.SystemQueue,
        ["Container"] = C4ElementType.Container,
        ["ContainerDb"] = C4ElementType.ContainerDb,
        ["ContainerQueue"] = C4ElementType.ContainerQueue,
        ["Component"] = C4ElementType.Component,
        ["ComponentDb"] = C4ElementType.ComponentDb,
        ["ComponentQueue"] = C4ElementType.ComponentQueue
    };

    static readonly Dictionary<string, C4BoundaryType> boundaryTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Boundary"] = C4BoundaryType.Generic,
        ["Enterprise_Boundary"] = C4BoundaryType.Enterprise,
        ["System_Boundary"] = C4BoundaryType.System,
        ["Container_Boundary"] = C4BoundaryType.Container,
        ["Deployment_Node"] = C4BoundaryType.Deployment,
        ["Node"] = C4BoundaryType.Node,
        ["Node_L"] = C4BoundaryType.Node,
        ["Node_R"] = C4BoundaryType.Node
    };

    static readonly Dictionary<string, C4RelationshipDirection> relationshipTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Rel"] = C4RelationshipDirection.Default,
        ["BiRel"] = C4RelationshipDirection.Default,
        ["RelIndex"] = C4RelationshipDirection.Default,
        ["Rel_U"] = C4RelationshipDirection.Up,
        ["Rel_Up"] = C4RelationshipDirection.Up,
        ["Rel_D"] = C4RelationshipDirection.Down,
        ["Rel_Down"] = C4RelationshipDirection.Down,
        ["Rel_L"] = C4RelationshipDirection.Left,
        ["Rel_Left"] = C4RelationshipDirection.Left,
        ["Rel_R"] = C4RelationshipDirection.Right,
        ["Rel_Right"] = C4RelationshipDirection.Right,
        ["Rel_Back"] = C4RelationshipDirection.Back,
        ["Rel_Neighbor"] = C4RelationshipDirection.Neighbor
    };

    // Statements that are valid C4 but change nothing in what is drawn here: the Update*Style and
    // UpdateLayoutConfig calls, tag definitions, and the layout and legend switches.
    static readonly string[] ignoredPrefixes = ["Update", "Add", "LAYOUT_", "Lay_", "SHOW_", "HIDE_"];

    static C4Parser()
    {
        var quotedString =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0);

        // Title: title My Diagram
        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine.AsTrimmedString())
                .AndSkip(CommonParsers.LineEnd);

        // An argument's value: a quoted string, or bare text up to the next comma or the closing bracket.
        var argumentValue =
            quotedString.Or(
                Literals.NoneOf(",)\"\r\n", minSize: 0)
                    .AsTrimmedString());

        // `$tags="v1"` names the argument it sets, wherever it comes in the list.
        var namedArgument =
            Literals.Char('$')
                .SkipAnd(Literals.Pattern(char.IsLetterOrDigit).AsString())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('='))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(argumentValue)
                .Map(static (name, value) => new Argument(name, value));

        var argument =
            CommonParsers.InlineWhitespace
                .SkipAnd(namedArgument.Or(argumentValue.Then(static _ => new Argument(null, _))))
                .AndSkip(CommonParsers.InlineWhitespace);

        // Every C4 statement is a call: Keyword(argument, argument, ...)
        // The indentation before it is left to the rules that use it, so that a keyword they turn down is
        // reported where it starts.
        var call =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_').AsString()
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('('))
                .And(Separated(Literals.Char(','), argument).Optional())
                .AndSkip(Literals.Char(')'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .Map(static (keyword, arguments) => new Call(keyword, arguments.OrSome([]).ToList()));

        // An element, a relationship, or a call that is read and ignored.
        var statement =
            CommonParsers.InlineWhitespace
                .SkipAnd(call.Assert(IsStatement, static _ => $"Unknown C4 statement '{_.Keyword}'"))
                .AndSkip(CommonParsers.LineEnd)
                .Then(ToContent);

        // The accessibility title and description, which are not drawn.
        var accessibilityLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    Literals.Text("accTitle")
                        .Or(Literals.Text("accDescr"))
                        .WhenFollowedBy(Literals.AnyOf(" \t:", minSize: 1, maxSize: 1)))
                .SkipAnd(restOfLine)
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

        // Skip line (comments, empty lines)
        var skipLine =
            OneOf(
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline),
                multiLineDescription,
                accessibilityLine);

        // Boundary opening: Type_Boundary(id, "label") {
        var boundaryOpen =
            CommonParsers.InlineWhitespace
                .SkipAnd(call.When(static (_, opening) => boundaryTypes.ContainsKey(opening.Keyword)))
                // The brace may sit on the line after the call.
                .AndSkip(ZeroOrMore(char.IsWhiteSpace))
                .AndSkip(Literals.Char('{'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd.Optional());

        // Boundary closing: }
        var boundaryClose =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char('}'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd.Optional());

        // Defined just below, once boundaryParser exists: what a boundary holds may itself be a boundary.
        var boundaryContentOrNestedBoundary = Deferred<IC4Content?>();

        // Recursive boundary parser - parses boundary with nested content
        var boundaryParser =
            boundaryOpen
                .And(Not(boundaryClose).SkipAnd(boundaryContentOrNestedBoundary).Many())
                .AndSkip(boundaryClose)
                .Map(static (open, content) => new BoundaryItem(ToBoundary(open), content.ToList()));

        // Content inside boundary: either nested boundary or regular element
        boundaryContentOrNestedBoundary.Parser =
            OneOf(
                boundaryParser.Then<IC4Content?>(static _ => _),
                statement,
                skipLine.Then<IC4Content?>(static _ => null)
            );

        // Content item (top level)
        var contentItem =
            OneOf(
                titleParser.Then<IC4Content?>(static _ => new TitleItem(_)),
                boundaryParser.Then<IC4Content?>(static _ => _),
                statement,
                skipLine.Then<IC4Content?>(static _ => null)
            );

        // Diagram type header
        var diagramTypeParser =
            OneOf(
                TextIgnoreCase("C4Context").Then(C4DiagramType.Context),
                TextIgnoreCase("C4Container").Then(C4DiagramType.Container),
                TextIgnoreCase("C4Component").Then(C4DiagramType.Component),
                TextIgnoreCase("C4Dynamic").Then(C4DiagramType.Dynamic),
                TextIgnoreCase("C4Deployment").Then(C4DiagramType.Deployment)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(diagramTypeParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .And(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Map(BuildModel);
    }

    static bool IsExternal(string keyword) =>
        keyword.EndsWith("_Ext", StringComparison.OrdinalIgnoreCase);

    static bool TryGetElementType(string keyword, out C4ElementType type)
    {
        if (IsExternal(keyword))
        {
            keyword = keyword[..^4];
        }

        return elementTypes.TryGetValue(keyword, out type);
    }

    static bool IsIgnored(string keyword)
    {
        foreach (var prefix in ignoredPrefixes)
        {
            if (keyword.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    static bool IsStatement(Call call) =>
        TryGetElementType(call.Keyword, out _) ||
        relationshipTypes.ContainsKey(call.Keyword) ||
        IsIgnored(call.Keyword);

    static IC4Content? ToContent(Call call)
    {
        if (TryGetElementType(call.Keyword, out var elementType))
        {
            return new ElementItem(ToElement(call, elementType));
        }

        if (relationshipTypes.TryGetValue(call.Keyword, out var direction))
        {
            return new RelItem(ToRelationship(call, direction));
        }

        return null;
    }

    // Person and System take (id, label, description); Container and Component put the technology
    // between the label and the description.
    static C4Element ToElement(Call call, C4ElementType type)
    {
        var id = call.Positional(0) ?? "";
        var hasTechnology = type is not (C4ElementType.Person
            or C4ElementType.System
            or C4ElementType.SystemDb
            or C4ElementType.SystemQueue);

        var element = new C4Element
        {
            Id = id,
            Label = call.Positional(1) ?? id,
            Type = type,
            IsExternal = IsExternal(call.Keyword)
        };

        if (hasTechnology)
        {
            element.Technology = call.Positional(2) ?? call.Named("techn");
            element.Description = call.Positional(3) ?? call.Named("descr");
        }
        else
        {
            element.Description = call.Positional(2) ?? call.Named("descr");
        }

        return element;
    }

    // Rel(from, to, label, technology). RelIndex puts the step number first, which is read and dropped.
    static C4Relationship ToRelationship(Call call, C4RelationshipDirection direction)
    {
        var first = 0;
        if (call.Keyword.Equals("RelIndex", StringComparison.OrdinalIgnoreCase))
        {
            first = 1;
        }

        return new()
        {
            From = call.Positional(first) ?? "",
            To = call.Positional(first + 1) ?? "",
            Label = call.Positional(first + 2),
            Technology = call.Positional(first + 3) ?? call.Named("techn"),
            Direction = direction,
            IsBidirectional = call.Keyword.Equals("BiRel", StringComparison.OrdinalIgnoreCase)
        };
    }

    // Boundary, Deployment_Node and Node take a type after the label, shown in the caption in place of
    // the default. The other boundaries' kind is already in their keyword.
    static C4Boundary ToBoundary(Call call)
    {
        var id = call.Positional(0) ?? "";
        var type = boundaryTypes[call.Keyword];
        var boundary = new C4Boundary
        {
            Id = id,
            Label = call.Positional(1) ?? id,
            Type = type
        };

        if (type is C4BoundaryType.Generic or C4BoundaryType.Deployment or C4BoundaryType.Node)
        {
            boundary.TypeLabel = call.Positional(2) ?? call.Named("type");
        }

        return boundary;
    }

    static C4Model BuildModel(C4DiagramType type, IEnumerable<IC4Content?> content)
    {
        var model = new C4Model { Type = type };
        ProcessContent(model, content, null);
        return model;
    }

    static void ProcessContent(C4Model model, IEnumerable<IC4Content?> content, string? parentBoundaryId)
    {
        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case ElementItem element:
                    element.Element.BoundaryId = parentBoundaryId;
                    model.Elements.Add(element.Element);
                    break;

                case RelItem rel:
                    model.Relationships.Add(rel.Value);
                    break;

                case BoundaryItem(var boundary, var c4Contents):
                    boundary.ElementIds.Clear();
                    boundary.ChildBoundaryIds.Clear();
                    boundary.ParentBoundaryId = parentBoundaryId;
                    model.Boundaries.Add(boundary);

                    // Add this boundary as child of parent
                    if (parentBoundaryId is not null)
                    {
                        var parent = model.Boundaries.FirstOrDefault(_ => _.Id == parentBoundaryId);
                        parent?.ChildBoundaryIds.Add(boundary.Id);
                    }

                    // Process nested content with this boundary as parent
                    ProcessContent(model, c4Contents, boundary.Id);

                    // Collect direct element IDs that belong to this boundary (not nested)
                    foreach (var el in model.Elements.Where(_ => _.BoundaryId == boundary.Id))
                    {
                        boundary.ElementIds.Add(el.Id);
                    }
                    break;
            }
        }
    }

    public DiagramParseResult<C4Model> Parse(string input) => parser.ParseDiagram(input);

    readonly record struct Argument(string? Name, string Value);

    sealed record Call(string Keyword, List<Argument> Arguments)
    {
        // The value in the given place among the unnamed arguments. A place left empty, as in
        // `Container(api, "API", "", "Serves the app")`, counts as not given.
        public string? Positional(int index)
        {
            foreach (var argument in Arguments)
            {
                if (argument.Name != null)
                {
                    continue;
                }

                if (index == 0)
                {
                    return NullIfEmpty(argument.Value);
                }

                index--;
            }

            return null;
        }

        public string? Named(string name)
        {
            foreach (var argument in Arguments)
            {
                if (name.Equals(argument.Name, StringComparison.OrdinalIgnoreCase))
                {
                    return NullIfEmpty(argument.Value);
                }
            }

            return null;
        }

        static string? NullIfEmpty(string value)
        {
            if (value.Length == 0)
            {
                return null;
            }

            return value;
        }
    }

    internal interface IC4Content;
    internal readonly record struct TitleItem(string Value) : IC4Content;
    internal readonly record struct ElementItem(C4Element Element) : IC4Content;
    internal readonly record struct RelItem(C4Relationship Value) : IC4Content;

    internal readonly record struct BoundaryItem(C4Boundary Boundary, List<IC4Content?> Content) : IC4Content;
}

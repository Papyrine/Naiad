class C4Parser : IDiagramParser<C4Model>
{
    static Parser<char, C4Model> parser;

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
            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"'));

        var restOfLine =
            Token(_ => _ != '\r' && _ != '\n').ManyString();

        // Title: title My Diagram
        var titleParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("title")
            from ___ in CommonParsers.RequiredWhitespace
            from title in restOfLine
            from ____ in CommonParsers.LineEnd
            select title.Trim();

        // An argument's value: a quoted string, or bare text up to the next comma or the closing bracket.
        var argumentValue =
            quotedString.Or(
                Token(_ => _ is not (',' or ')' or '"' or '\r' or '\n'))
                    .ManyString()
                    .Select(_ => _.Trim()));

        // `$tags="v1"` names the argument it sets, wherever it comes in the list.
        var namedArgument =
            from dollar in Char('$')
            from name in Token(char.IsLetterOrDigit).AtLeastOnceString()
            from _ in CommonParsers.InlineWhitespace
            from assign in Char('=')
            from __ in CommonParsers.InlineWhitespace
            from value in argumentValue
            select new Argument(name, value);

        var argument =
            CommonParsers.InlineWhitespace
                .Then(Try(namedArgument).Or(argumentValue.Select(_ => new Argument(null, _))))
                .Before(CommonParsers.InlineWhitespace);

        // Every C4 statement is a call: Keyword(argument, argument, ...)
        var call =
            from _ in CommonParsers.InlineWhitespace
            from keyword in Token(_ => char.IsLetterOrDigit(_) || _ == '_').AtLeastOnceString()
            from __ in CommonParsers.InlineWhitespace
            from open in Char('(')
            from arguments in argument.Separated(Char(','))
            from close in Char(')')
            from ___ in CommonParsers.InlineWhitespace
            select new Call(keyword, arguments.ToList());

        // An element, a relationship, or a call that is read and ignored.
        var statement =
            call
                .Assert(IsStatement, _ => $"Unknown C4 statement '{_.Keyword}'")
                .Before(CommonParsers.LineEnd)
                .Select(ToContent);

        // The accessibility title and description, which are not drawn.
        var accessibilityLine =
            from _ in CommonParsers.InlineWhitespace
            from keyword in Try(String("accTitle")).Or(String("accDescr"))
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

        // Skip line (comments, empty lines)
        var skipLine =
            OneOf(
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment)),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)),
                Try(multiLineDescription),
                Try(accessibilityLine));

        // Boundary opening: Type_Boundary(id, "label") {
        var boundaryOpen =
            from opening in call.Assert(_ => boundaryTypes.ContainsKey(_.Keyword), _ => $"Unknown C4 boundary '{_.Keyword}'")
            // The brace may sit on the line after the call.
            from _ in SkipWhitespaces.Then(Char('{'))
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd.Optional()
            select opening;

        // Boundary closing: }
        var boundaryClose =
            from _ in CommonParsers.InlineWhitespace
            from __ in Char('}')
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd.Optional()
            select Unit.Value;

        // Assigned just below; boundaryParser captures the variable and only dereferences it at
        // parse time, by which point the real parser is in place.
        Parser<char, IC4Content?> boundaryContentOrNestedBoundary = null!;

        // Recursive boundary parser - parses boundary with nested content
        var boundaryParser =
            from open in boundaryOpen
            from content in boundaryContentOrNestedBoundary.Until(Lookahead(Try(boundaryClose)))
            from close in boundaryClose
            select new BoundaryItem(ToBoundary(open), content.ToList());

        // Content inside boundary: either nested boundary or regular element
        boundaryContentOrNestedBoundary =
            OneOf(
                Try(boundaryParser.Select<IC4Content?>(_ => _)),
                Try(statement),
                skipLine.ThenReturn<IC4Content?>(null)
            );

        // Content item (top level)
        var contentItem =
            OneOf(
                Try(titleParser.Select<IC4Content?>(_ => new TitleItem(_))),
                Try(boundaryParser.Select<IC4Content?>(_ => _)),
                Try(statement),
                skipLine.ThenReturn<IC4Content?>(null)
            );

        // Diagram type header
        var diagramTypeParser =
            OneOf(
                Try(CIString("C4Context")).ThenReturn(C4DiagramType.Context),
                Try(CIString("C4Container")).ThenReturn(C4DiagramType.Container),
                Try(CIString("C4Component")).ThenReturn(C4DiagramType.Component),
                Try(CIString("C4Dynamic")).ThenReturn(C4DiagramType.Dynamic),
                Try(CIString("C4Deployment")).ThenReturn(C4DiagramType.Deployment)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from type in diagramTypeParser
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(type, result.Item1);
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

    public Result<char, C4Model> Parse(string input) => parser.Parse(input);

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

class RequirementParser : IDiagramParser<RequirementModel>
{
    static Parser<char, RequirementModel> parser;

    static RequirementParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        // `test_req:::important` attaches style classes to a node wherever it is named. They are read
        // and dropped: styling is not drawn, but the node it sits on still is.
        var styleClasses =
            String(":::")
                .Then(Token(_ => char.IsLetterOrDigit(_) || _ is '_' or '-' or ',').SkipAtLeastOnce());

        // A requirement or element name: a bare word, or anything at all inside double quotes.
        var nodeName =
            CommonParsers.DoubleQuotedString
                .Or(identifier)
                .Before(Try(styleClasses).Optional());

        var restOfLine =
            Token(_ => _ != '\r' && _ != '\n').ManyString();

        var requirementTypeParser =
            OneOf(
                Try(CIString("functionalRequirement")).ThenReturn(RequirementType.FunctionalRequirement),
                Try(CIString("interfaceRequirement")).ThenReturn(RequirementType.InterfaceRequirement),
                Try(CIString("performanceRequirement")).ThenReturn(RequirementType.PerformanceRequirement),
                Try(CIString("physicalRequirement")).ThenReturn(RequirementType.PhysicalRequirement),
                Try(CIString("designConstraint")).ThenReturn(RequirementType.DesignConstraint),
                CIString("requirement").ThenReturn(RequirementType.Requirement)
            );

        // Property: key: value
        var propertyParser =
            from _ in CommonParsers.InlineWhitespace
            from key in identifier
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char(':')
            from ____ in CommonParsers.InlineWhitespace
            from value in restOfLine
            from _____ in CommonParsers.LineEnd
            select (key.ToLowerInvariant(), Unquote(value.Trim()));

        // A block's lines: properties, with blank lines and comments between them. Each is tried and
        // given back whole, so the indentation in front of the closing brace is not taken for the start
        // of another property.
        var blockBody =
            OneOf(
                Try(propertyParser.Select<(string, string)?>(_ => _)),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment)).ThenReturn<(string, string)?>(null),
                Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)).ThenReturn<(string, string)?>(null)
            ).Many()
            .Select(_ => _.Where(_ => _ is not null).Select(_ => _!.Value).ToList());

        var requirementBlockParser =
            from _ in CommonParsers.InlineWhitespace
            from type in requirementTypeParser
            from __ in CommonParsers.RequiredWhitespace
            from name in nodeName
            from ___ in CommonParsers.InlineWhitespace
            from ____ in Char('{')
            from _____ in CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd)
            from props in blockBody
            from ______ in CommonParsers.InlineWhitespace
            from _______ in Char('}')
            from ________ in CommonParsers.InlineWhitespace
            from _________ in CommonParsers.LineEnd
            select BuildRequirement(name, type, props);

        var elementBlockParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("element")
            from ___ in CommonParsers.RequiredWhitespace
            from name in nodeName
            from ____ in CommonParsers.InlineWhitespace
            from _____ in Char('{')
            from ______ in CommonParsers.InlineWhitespace.Then(CommonParsers.LineEnd)
            from props in blockBody
            from _______ in CommonParsers.InlineWhitespace
            from ________ in Char('}')
            from _________ in CommonParsers.InlineWhitespace
            from __________ in CommonParsers.LineEnd
            select BuildElement(name, props);

        var relationTypeParser =
            OneOf(
                Try(CIString("contains")).ThenReturn(RelationType.Contains),
                Try(CIString("copies")).ThenReturn(RelationType.Copies),
                Try(CIString("derives")).ThenReturn(RelationType.Derives),
                Try(CIString("satisfies")).ThenReturn(RelationType.Satisfies),
                Try(CIString("verifies")).ThenReturn(RelationType.Verifies),
                Try(CIString("refines")).ThenReturn(RelationType.Refines),
                CIString("traces").ThenReturn(RelationType.Traces)
            );

        // Relation: source - type -> target
        var relationParser =
            from _ in CommonParsers.InlineWhitespace
            from source in nodeName
            from __ in CommonParsers.InlineWhitespace
            from ___ in Char('-')
            from ____ in CommonParsers.InlineWhitespace
            from relType in relationTypeParser
            from _____ in CommonParsers.InlineWhitespace
            from ______ in String("->")
            from _______ in CommonParsers.InlineWhitespace
            from target in nodeName
            from ________ in CommonParsers.InlineWhitespace
            from _________ in CommonParsers.LineEnd
            select new RequirementRelation
            {
                Source = source,
                Target = target,
                Type = relType
            };

        // The same relation written the other way round: target <- type - source
        var reverseRelationParser =
            from _ in CommonParsers.InlineWhitespace
            from target in nodeName
            from __ in CommonParsers.InlineWhitespace
            from ___ in String("<-")
            from ____ in CommonParsers.InlineWhitespace
            from relType in relationTypeParser
            from _____ in CommonParsers.InlineWhitespace
            from ______ in Char('-')
            from _______ in CommonParsers.InlineWhitespace
            from source in nodeName
            from ________ in CommonParsers.InlineWhitespace
            from _________ in CommonParsers.LineEnd
            select new RequirementRelation
            {
                Source = source,
                Target = target,
                Type = relType
            };

        // Lines that are valid Mermaid but change nothing in what is drawn here: the direction (the
        // layout is fixed), styling, and the accessibility title and description.
        var ignoredLine =
            from _ in CommonParsers.InlineWhitespace
            from keyword in OneOf(
                Try(String("direction")),
                Try(String("classDef")),
                Try(String("class")),
                Try(String("style")),
                Try(String("accTitle")),
                String("accDescr"))
            from boundary in Lookahead(Token(_ => _ is ' ' or '\t' or ':'))
            from rest in restOfLine
            from lineEnd in CommonParsers.LineEnd
            select Unit.Value;

        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        var contentItem =
            OneOf(
                Try(requirementBlockParser.Select<IRequirementContent?>(_ => new RequirementBlockItem(_))),
                Try(elementBlockParser.Select<IRequirementContent?>(_ => new ElementBlockItem(_))),
                Try(ignoredLine.ThenReturn<IRequirementContent?>(null)),
                Try(relationParser.Select<IRequirementContent?>(_ => new RelationItem(_))),
                Try(reverseRelationParser.Select<IRequirementContent?>(_ => new RelationItem(_))),
                skipLine.ThenReturn<IRequirementContent?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("requirementDiagram")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1);
    }

    // A quoted value's quotes are delimiters, not part of the value.
    static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            value[0] == '"' &&
            value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }

    static Requirement BuildRequirement(string name, RequirementType type, List<(string key, string value)> props)
    {
        var req = new Requirement { Name = name, Type = type };
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "id": req.Id = value; break;
                case "text": req.Text = value; break;
                case "risk":
                    req.Risk = value.ToLowerInvariant() switch
                    {
                        "low" => RiskLevel.Low,
                        "high" => RiskLevel.High,
                        _ => RiskLevel.Medium
                    };
                    break;
                case "verifymethod":
                    req.VerifyMethod = value.ToLowerInvariant() switch
                    {
                        "analysis" => VerifyMethod.Analysis,
                        "demonstration" => VerifyMethod.Demonstration,
                        "inspection" => VerifyMethod.Inspection,
                        _ => VerifyMethod.Test
                    };
                    break;
            }
        }
        return req;
    }

    static RequirementElement BuildElement(string name, List<(string key, string value)> props)
    {
        var elem = new RequirementElement { Id = name, Name = name };
        foreach (var (key, value) in props)
        {
            switch (key)
            {
                case "type": elem.Type = value; break;
                case "docref": elem.DocRef = value; break;
            }
        }
        return elem;
    }

    static RequirementModel BuildModel(IEnumerable<IRequirementContent?> content)
    {
        var model = new RequirementModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case RequirementBlockItem req:
                    model.Requirements.Add(req.Value);
                    break;

                case ElementBlockItem elem:
                    model.Elements.Add(elem.Value);
                    break;

                case RelationItem rel:
                    model.Relations.Add(rel.Value);
                    break;
            }
        }

        return model;
    }

    public Result<char, RequirementModel> Parse(string input) => parser.Parse(input);

    internal interface IRequirementContent;
    readonly record struct RequirementBlockItem(Requirement Value) : IRequirementContent;
    readonly record struct ElementBlockItem(RequirementElement Value) : IRequirementContent;
    readonly record struct RelationItem(RequirementRelation Value) : IRequirementContent;
}

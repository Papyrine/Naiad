class RequirementParser : IDiagramParser<RequirementModel>
{
    static Parser<RequirementModel> parser;

    static RequirementParser()
    {
        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString();

        // `test_req:::important` attaches style classes to a node wherever it is named. They are read
        // and dropped: styling is not drawn, but the node it sits on still is.
        var styleClasses =
            Literals.Text(":::")
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ is '_' or '-' or ','));

        // A requirement or element name: a bare word, or anything at all inside double quotes.
        var nodeName =
            CommonParsers.DoubleQuotedString
                .Or(identifier)
                .AndSkip(styleClasses.Optional());

        var restOfLineText =
            Literals.NoneOf("\r\n", minSize: 0);

        var restOfLine =
            restOfLineText.AsString();

        var requirementTypeParser =
            OneOf(
                TextIgnoreCase("functionalRequirement").Then(RequirementType.FunctionalRequirement),
                TextIgnoreCase("interfaceRequirement").Then(RequirementType.InterfaceRequirement),
                TextIgnoreCase("performanceRequirement").Then(RequirementType.PerformanceRequirement),
                TextIgnoreCase("physicalRequirement").Then(RequirementType.PhysicalRequirement),
                TextIgnoreCase("designConstraint").Then(RequirementType.DesignConstraint),
                TextIgnoreCase("requirement").Then(RequirementType.Requirement)
            );

        // Property: key: value
        var propertyParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(identifier)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (key, value) => (key.ToLowerInvariant(), Unquote(value.Trim())));

        // A block's lines: properties, with blank lines and comments between them. Each is tried and
        // given back whole, so the indentation in front of the closing brace is not taken for the start
        // of another property.
        var blockBody =
            OneOf(
                propertyParser.Then<(string, string)?>(static _ => _),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment).Then<(string, string)?>(static _ => null),
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline).Then<(string, string)?>(static _ => null)
            ).Many()
            .Then(static _ => _.Where(static _ => _ is not null).Select(static _ => _!.Value).ToList());

        var requirementBlockParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(requirementTypeParser)
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.LineEnd))
                .And(blockBody)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('}'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (type, name, props) => BuildRequirement(name, type, props));

        var elementBlockParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("element"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('{'))
                .AndSkip(CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.LineEnd))
                .And(blockBody)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('}'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(BuildElement);

        var relationTypeParser =
            OneOf(
                TextIgnoreCase("contains").Then(RelationType.Contains),
                TextIgnoreCase("copies").Then(RelationType.Copies),
                TextIgnoreCase("derives").Then(RelationType.Derives),
                TextIgnoreCase("satisfies").Then(RelationType.Satisfies),
                TextIgnoreCase("verifies").Then(RelationType.Verifies),
                TextIgnoreCase("refines").Then(RelationType.Refines),
                TextIgnoreCase("traces").Then(RelationType.Traces)
            );

        // Relation: source - type -> target
        var relationParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('-'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(relationTypeParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Text("->"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (source, relType, target) =>
                    new RequirementRelation
                    {
                        Source = source,
                        Target = target,
                        Type = relType
                    });

        // The same relation written the other way round: target <- type - source
        var reverseRelationParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Text("<-"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(relationTypeParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('-'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(nodeName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (target, relType, source) =>
                    new RequirementRelation
                    {
                        Source = source,
                        Target = target,
                        Type = relType
                    });

        // Lines that are valid Mermaid but change nothing in what is drawn here: the direction (the
        // layout is fixed), styling, and the accessibility title and description.
        var ignoredKeyword =
            OneOf(
                Literals.Text("direction"),
                Literals.Text("classDef"),
                Literals.Text("class"),
                Literals.Text("style"),
                Literals.Text("accTitle"),
                Literals.Text("accDescr"));

        var ignoredLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(ignoredKeyword.WhenFollowedBy(Literals.AnyOf(" \t:", minSize: 1, maxSize: 1)))
                .SkipAnd(restOfLineText)
                .SkipAnd(CommonParsers.LineEnd);

        var skipLine =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment)
                .Or(CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline));

        var contentItem =
            OneOf(
                requirementBlockParser.Then<IRequirementContent?>(static _ => new RequirementBlockItem(_)),
                elementBlockParser.Then<IRequirementContent?>(static _ => new ElementBlockItem(_)),
                ignoredLine.Then<IRequirementContent?>(static _ => null),
                relationParser.Then<IRequirementContent?>(static _ => new RelationItem(_)),
                reverseRelationParser.Then<IRequirementContent?>(static _ => new RelationItem(_)),
                skipLine.Then<IRequirementContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("requirementDiagram"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    // A quoted value's quotes are delimiters, not part of the value.
    static string Unquote(string value)
    {
        if (value is ['"', _, ..] &&
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

    public DiagramParseResult<RequirementModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IRequirementContent;
    readonly record struct RequirementBlockItem(Requirement Value) : IRequirementContent;
    readonly record struct ElementBlockItem(RequirementElement Value) : IRequirementContent;
    readonly record struct RelationItem(RequirementRelation Value) : IRequirementContent;
}

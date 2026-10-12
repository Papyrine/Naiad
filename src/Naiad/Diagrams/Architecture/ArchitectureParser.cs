class ArchitectureParser : IDiagramParser<ArchitectureModel>
{
    static Parser<ArchitectureModel> parser;

    static ArchitectureParser()
    {
        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString();

        var iconParser =
            Between(Literals.Char('('), Literals.NoneOf(")", minSize: 0), Literals.Char(')')).AsString();

        var labelParser =
            Between(Literals.Char('['), Literals.NoneOf("]", minSize: 0), Literals.Char(']')).AsString();

        var parentParser =
            CommonParsers.RequiredWhitespace
                .SkipAnd(TextIgnoreCase("in"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(identifier);

        // Direction: L, R, T, B
        var directionParser =
            OneOf(
                Literals.Char('L').Then(EdgeDirection.Left),
                Literals.Char('R').Then(EdgeDirection.Right),
                Literals.Char('T').Then(EdgeDirection.Top),
                Literals.Char('B').Then(EdgeDirection.Bottom)
            );

        // Group: group id(icon)[label] in parent
        var groupParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("group"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(identifier)
                .And(iconParser.Optional())
                .And(labelParser.Optional())
                .And(parentParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, icon, label, parent) =>
                    new ArchitectureGroup
                    {
                        Id = id,
                        Icon = icon.OrNull(),
                        Label = label.OrNull(),
                        Parent = parent.OrNull()
                    });

        // Service: service id(icon)[label] in parent
        var serviceParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("service"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(identifier)
                .And(iconParser.Optional())
                .And(labelParser.Optional())
                .And(parentParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, icon, label, parent) =>
                    new ArchitectureService
                    {
                        Id = id,
                        Icon = icon.OrNull(),
                        Label = label.OrNull(),
                        Parent = parent.OrNull()
                    });

        // Junction: junction id in parent
        var junctionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("junction"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(identifier)
                .And(parentParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (id, parent) =>
                    new ArchitectureJunction
                    {
                        Id = id,
                        Parent = parent.OrNull()
                    });

        // Group reference: {groupId}
        var groupRef =
            Between(Literals.Char('{'), identifier, Literals.Char('}'));

        // Source side: id{group}?:direction. The arrowhead belongs on the line (`db:L <-- R:server`),
        // which is how Mermaid writes it; one in front of the id is accepted too.
        var sourceSideParser =
            Literals.Char('<').Optional()
                .And(identifier)
                .And(groupRef.Optional())
                .AndSkip(Literals.Char(':'))
                .And(directionParser)
                .Map(static (arw, nodeId, grp, dir) => (id: nodeId, grp: grp.OrNull(), dir, arrow: arw.HasValue));

        // Target side: direction:id{group}?, likewise with an arrowhead accepted after the direction.
        var targetSideParser =
            directionParser
                .And(Literals.Char('>').Optional())
                .AndSkip(Literals.Char(':'))
                .And(identifier)
                .And(groupRef.Optional())
                .Map(static (dir, arw, nodeId, grp) => (id: nodeId, grp: grp.OrNull(), dir, arrow: arw.HasValue));

        // The line itself: -- or -[Label]-, with an arrowhead at either end.
        var lineParser =
            Literals.Char('<').Optional()
                .And(
                    Literals.Text("--").Then<string?>(static _ => null)
                        .Or(Between(Literals.Text("-["), Literals.NoneOf("]\r\n", minSize: 0), Literals.Text("]-"))
                            .Then<string?>(static _ => _.Span.Trim().ToString())))
                .And(Literals.Char('>').Optional())
                .Map(static (sourceArrow, label, targetArrow) =>
                    (sourceArrow: sourceArrow.HasValue, targetArrow: targetArrow.HasValue, label));

        // Edge: source:side <--> side:target
        var edgeParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(sourceSideParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(lineParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(targetSideParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (source, line, target) =>
                    BuildEdge(
                        source with { arrow = source.arrow || line.sourceArrow },
                        target with { arrow = target.arrow || line.targetArrow },
                        line.label));

        var skipLine =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment)
                .Or(CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline));

        var contentItem =
            OneOf(
                groupParser.Then<IArchitectureContent?>(static _ => new GroupItem(_)),
                serviceParser.Then<IArchitectureContent?>(static _ => new ServiceItem(_)),
                junctionParser.Then<IArchitectureContent?>(static _ => new JunctionItem(_)),
                edgeParser.Then<IArchitectureContent?>(static _ => new EdgeItem(_)),
                skipLine.Then<IArchitectureContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("architecture"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static ArchitectureEdge BuildEdge(
        (string id, string? grp, EdgeDirection dir, bool arrow) source,
        (string id, string? grp, EdgeDirection dir, bool arrow) target,
        string? label) => new()
    {
        Label = string.IsNullOrEmpty(label) ? null : label,
        SourceId = source.id,
        SourceGroup = source.grp,
        SourceSide = source.dir,
        SourceArrow = source.arrow,
        TargetId = target.id,
        TargetGroup = target.grp,
        TargetSide = target.dir,
        TargetArrow = target.arrow
    };

    static ArchitectureModel BuildModel(IEnumerable<IArchitectureContent?> content)
    {
        var model = new ArchitectureModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case GroupItem group:
                    model.Groups.Add(group.Value);
                    break;

                case ServiceItem service:
                    model.Services.Add(service.Value);
                    break;

                case JunctionItem junction:
                    model.Junctions.Add(junction.Value);
                    break;

                case EdgeItem edge:
                    model.Edges.Add(edge.Value);
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<ArchitectureModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IArchitectureContent;
    readonly record struct GroupItem(ArchitectureGroup Value) : IArchitectureContent;
    readonly record struct ServiceItem(ArchitectureService Value) : IArchitectureContent;
    readonly record struct JunctionItem(ArchitectureJunction Value) : IArchitectureContent;
    readonly record struct EdgeItem(ArchitectureEdge Value) : IArchitectureContent;
}

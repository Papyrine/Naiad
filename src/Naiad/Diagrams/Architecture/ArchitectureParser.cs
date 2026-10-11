class ArchitectureParser : IDiagramParser<ArchitectureModel>
{
    static Parser<char, ArchitectureModel> parser;

    static ArchitectureParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        var iconParser =
            Char('(').Then(Token(_ => _ != ')').ManyString()).Before(Char(')'));

        var labelParser =
            Char('[').Then(Token(_ => _ != ']').ManyString()).Before(Char(']'));

        var parentParser =
            Try(
                CommonParsers.RequiredWhitespace
                    .Then(CIString("in"))
                    .Then(CommonParsers.RequiredWhitespace)
                    .Then(identifier)
            );

        // Direction: L, R, T, B
        var directionParser =
            OneOf(
                Char('L').ThenReturn(EdgeDirection.Left),
                Char('R').ThenReturn(EdgeDirection.Right),
                Char('T').ThenReturn(EdgeDirection.Top),
                Char('B').ThenReturn(EdgeDirection.Bottom)
            );

        // Group: group id(icon)[label] in parent
        var groupParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("group")
            from ___ in CommonParsers.RequiredWhitespace
            from id in identifier
            from icon in iconParser.Optional()
            from label in labelParser.Optional()
            from parent in parentParser.Optional()
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new ArchitectureGroup
            {
                Id = id,
                Icon = icon.GetValueOrDefault(),
                Label = label.GetValueOrDefault(),
                Parent = parent.GetValueOrDefault()
            };

        // Service: service id(icon)[label] in parent
        var serviceParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("service")
            from ___ in CommonParsers.RequiredWhitespace
            from id in identifier
            from icon in iconParser.Optional()
            from label in labelParser.Optional()
            from parent in parentParser.Optional()
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new ArchitectureService
            {
                Id = id,
                Icon = icon.GetValueOrDefault(),
                Label = label.GetValueOrDefault(),
                Parent = parent.GetValueOrDefault()
            };

        // Junction: junction id in parent
        var junctionParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("junction")
            from ___ in CommonParsers.RequiredWhitespace
            from id in identifier
            from parent in parentParser.Optional()
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new ArchitectureJunction
            {
                Id = id,
                Parent = parent.GetValueOrDefault()
            };

        // Group reference: {groupId}
        var groupRef =
            Char('{').Then(identifier).Before(Char('}'));

        // Source side: id{group}?:direction. The arrowhead belongs on the line (`db:L <-- R:server`),
        // which is how Mermaid writes it; one in front of the id is accepted too.
        var sourceSideParser =
            from arw in Char('<').Optional()
            from nodeId in identifier
            from grp in groupRef.Optional()
            from colon in Char(':')
            from dir in directionParser
            select (id: nodeId, grp: grp.GetValueOrDefault(), dir, arrow: arw.HasValue);

        // Target side: direction:id{group}?, likewise with an arrowhead accepted after the direction.
        var targetSideParser =
            from dir in directionParser
            from arw in Char('>').Optional()
            from colon in Char(':')
            from nodeId in identifier
            from grp in groupRef.Optional()
            select (id: nodeId, grp: grp.GetValueOrDefault(), dir, arrow: arw.HasValue);

        // The line itself: -- or -[Label]-, with an arrowhead at either end.
        var lineParser =
            from sourceArrow in Char('<').Optional()
            from label in Try(String("--").ThenReturn((string?)null))
                .Or(String("-[")
                    .Then(Token(_ => _ != ']' && _ != '\r' && _ != '\n').ManyString())
                    .Before(String("]-"))
                    .Select(_ => (string?)_.Trim()))
            from targetArrow in Char('>').Optional()
            select (sourceArrow: sourceArrow.HasValue, targetArrow: targetArrow.HasValue, label);

        // Edge: source:side <--> side:target
        var edgeParser =
            from _ in CommonParsers.InlineWhitespace
            from source in sourceSideParser
            from __ in CommonParsers.InlineWhitespace
            from line in lineParser
            from ____ in CommonParsers.InlineWhitespace
            from target in targetSideParser
            from _____ in CommonParsers.InlineWhitespace
            from ______ in CommonParsers.LineEnd
            select BuildEdge(
                source with { arrow = source.arrow || line.sourceArrow },
                target with { arrow = target.arrow || line.targetArrow },
                line.label);

        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        var contentItem =
            OneOf(
                Try(groupParser.Select<IArchitectureContent?>(_ => new GroupItem(_))),
                Try(serviceParser.Select<IArchitectureContent?>(_ => new ServiceItem(_))),
                Try(junctionParser.Select<IArchitectureContent?>(_ => new JunctionItem(_))),
                Try(edgeParser.Select<IArchitectureContent?>(_ => new EdgeItem(_))),
                skipLine.ThenReturn<IArchitectureContent?>(null)
            );

        parser =
            from inlineWhitespace in CommonParsers.InlineWhitespace
            from architecture in CommonParsers.BetaKeyword("architecture")
            from innerInlineWhitespace in CommonParsers.InlineWhitespace
            from lineEnd in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1);
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

    public Result<char, ArchitectureModel> Parse(string input) => parser.Parse(input);

    internal interface IArchitectureContent;
    readonly record struct GroupItem(ArchitectureGroup Value) : IArchitectureContent;
    readonly record struct ServiceItem(ArchitectureService Value) : IArchitectureContent;
    readonly record struct JunctionItem(ArchitectureJunction Value) : IArchitectureContent;
    readonly record struct EdgeItem(ArchitectureEdge Value) : IArchitectureContent;
}

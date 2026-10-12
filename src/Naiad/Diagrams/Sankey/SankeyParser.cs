class SankeyParser : IDiagramParser<SankeyModel>
{
    static Parser<SankeyModel> parser;

    static SankeyParser()
    {
        var numberParser = CommonParsers.SignedDecimal;

        var quotedString =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        // Unquoted name (no commas or newlines)
        var unquotedName =
            Literals.NoneOf(",\r\n").AsTrimmedString();

        // Name (quoted or unquoted). A name that opens with a quote is a quoted one: left unclosed, it is
        // not read as an unquoted name instead.
        var name =
            quotedString.Or(Not(Literals.Char('"')).SkipAnd(unquotedName));

        // Link: source,target,value
        var linkParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(name)
                .AndSkip(Literals.Char(','))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(name)
                .AndSkip(Literals.Char(','))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(numberParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((source, target, value) =>
                    new SankeyLink
                    {
                        Source = source,
                        Target = target,
                        Value = value
                    });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                linkParser.Then<SankeyLink?>(static _ => _),
                skipLine.Then<SankeyLink?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("sankey"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.OfType<SankeyLink>().ToList()));
    }

    static SankeyModel BuildModel(List<SankeyLink> links)
    {
        var model = new SankeyModel();
        model.Links.AddRange(links);
        return model;
    }

    public DiagramParseResult<SankeyModel> Parse(string input) => parser.ParseDiagram(input);
}

class SankeyParser : IDiagramParser<SankeyModel>
{
    static Parser<char, SankeyModel> parser;

    static SankeyParser()
    {
        var numberParser = CommonParsers.SignedDecimal;

        var quotedString =
            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"'));

        // Unquoted name (no commas or newlines)
        var unquotedName =
            Token(_ => _ != ',' && _ != '\r' && _ != '\n').AtLeastOnceString()
                .Select(_ => _.Trim());

        // Name (quoted or unquoted)
        var name =
            quotedString.Or(unquotedName);

        // Link: source,target,value
        var linkParser =
            from _ in CommonParsers.InlineWhitespace
            from source in name
            from __ in Char(',')
            from ___ in CommonParsers.InlineWhitespace
            from target in name
            from ____ in Char(',')
            from _____ in CommonParsers.InlineWhitespace
            from value in numberParser
            from ______ in CommonParsers.InlineWhitespace
            from _______ in CommonParsers.LineEnd
            select new SankeyLink
            {
                Source = source,
                Target = target,
                Value = value
            };

        // Skip line (comments, empty lines)
        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        var contentItem =
            OneOf(
                Try(linkParser.Select<SankeyLink?>(_ => _)),
                skipLine.ThenReturn<SankeyLink?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.BetaKeyword("sankey")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1.Where(_ => _ != null).ToList());
    }

    static SankeyModel BuildModel(List<SankeyLink> links)
    {
        var model = new SankeyModel();
        model.Links.AddRange(links);
        return model;
    }

    public Result<char, SankeyModel> Parse(string input) => parser.Parse(input);
}

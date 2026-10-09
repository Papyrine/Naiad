class PieParser : IDiagramParser<PieModel>
{
    static Parser<char, PieModel> parser;

    static PieParser()
    {
        var sectionParser =
            from _ in CommonParsers.InlineWhitespace
            from label in CommonParsers.QuotedString
            from __ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from ___ in CommonParsers.InlineWhitespace
            from value in CommonParsers.Number
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select new PieSection { Label = label, Value = value };

        var titleLine =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("title")
            from __ in CommonParsers.RequiredWhitespace
            from title in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from ___ in CommonParsers.LineEnd
            select title;

        var showDataParser =
            Try(String("showData")).ThenReturn(true).Or(Return(false));

        var skipLine =
            CommonParsers.InlineWhitespace
                .Then(Try(CommonParsers.Comment).Or(CommonParsers.Newline));

        // Inline title: pie title My Title (on same line)
        var inlineTitleParser =
            from keyword in String("title")
            from _ in CommonParsers.RequiredWhitespace
            from title in Token(_ => _ != '\r' && _ != '\n').ManyString()
            select title;

        var parseContent =
            from lines in Try(titleLine.Select(_ => (title: (string?)_, section: (PieSection?)null)))
                .Or(Try(sectionParser.Select(_ => (title: (string?)null, section: (PieSection?)_))))
                .Or(skipLine.ThenReturn((title: (string?)null, section: (PieSection?)null))).Many()
            select (
                title: lines.FirstOrDefault(_ => _.title != null).title,
                sections: lines.Where(_ => _.section != null).Select(_ => _.section!).ToList()
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from keyword in String("pie")
            from __ in CommonParsers.InlineWhitespace
            from showData in showDataParser
            from ___ in CommonParsers.InlineWhitespace
            from inlineTitle in Try(inlineTitleParser).Optional()
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            from content in parseContent
            // Nothing may be left over: without this a line no rule matches ends the list quietly and the
            // rest of the diagram is dropped instead of being reported.
            from end in End
            select BuildModel(showData, inlineTitle.HasValue ? inlineTitle.Value : content.title, content.sections);
    }

    static PieModel BuildModel(bool showData, string? title, List<PieSection> sections)
    {
        var model = new PieModel
        {
            ShowData = showData,
            Title = title
        };
        model.Sections.AddRange(sections);
        return model;
    }

    public Result<char, PieModel> Parse(string input) => parser.Parse(input);
}

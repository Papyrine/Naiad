class PieParser : IDiagramParser<PieModel>
{
    static Parser<PieModel> parser;

    static PieParser()
    {
        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsString();

        var sectionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.QuotedString)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(CommonParsers.Number)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((label, value) => new PieSection { Label = label, Value = value });

        var titleLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        var showDataParser =
            Literals.Text("showData").Then(true).Else(false);

        // The accessibility title and description, which change nothing that is drawn.
        var accessibilityLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(
                    OneOf(Literals.Text("accTitle"), Literals.Text("accDescr"))
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

        var skipLine =
            OneOf(
                multiLineDescription,
                accessibilityLine,
                CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline)));

        // Inline title: pie title My Title (on same line)
        var inlineTitleParser =
            Literals.Text("title")
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine);

        var line =
            OneOf(
                titleLine.Then(static _ => (title: (string?)_, section: (PieSection?)null)),
                sectionParser.Then(static _ => (title: (string?)null, section: (PieSection?)_)),
                skipLine.Then((title: (string?)null, section: (PieSection?)null)));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("pie"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(showDataParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(inlineTitleParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .And(line.Many())
                // Nothing may be left over: without this a line no rule matches ends the list quietly and the
                // rest of the diagram is dropped instead of being reported.
                .AndSkip(CommonParsers.End)
                .Map((showData, inlineTitle, lines) =>
                    BuildModel(
                        showData,
                        inlineTitle.OrNull() ?? lines.FirstOrDefault(_ => _.title != null).title,
                        lines.Where(_ => _.section != null).Select(_ => _.section!)));
    }

    static PieModel BuildModel(bool showData, string? title, IEnumerable<PieSection> sections)
    {
        var model = new PieModel
        {
            ShowData = showData,
            Title = title
        };
        model.Sections.AddRange(sections);
        return model;
    }

    public DiagramParseResult<PieModel> Parse(string input) => parser.ParseDiagram(input);
}

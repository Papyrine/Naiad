namespace Naiad.Diagrams.Info;

/// <summary>
/// Reads Mermaid's info diagram: the <c>info</c> keyword, an optional <c>showInfo</c> line, then an
/// optional title and accessibility title and description, with blank lines and comments anywhere.
/// </summary>
class InfoParser : IDiagramParser<InfoModel>
{
    static Parser<InfoModel> parser;

    static InfoParser()
    {
        var restOfLine = Literals.NoneOf("\r\n", minSize: 0);

        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var skipLines = skipLine.Many();

        // Mermaid's grammar allows showInfo after any blank lines but before the title lines. It may share
        // the keyword's line, since Langium's whitespace is not line-sensitive between the two tokens.
        var showInfo =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("showInfo"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.Comment.Or(CommonParsers.LineEnd))
                .Then(static _ => true);

        var title =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(Literals.AnyOf(" \t").SkipAnd(restOfLine).Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Then<string?>(static _ => _.HasValue ? _.Value.Span.Trim().ToString() : "");

        // The accessibility text has nothing to label in a drawing of one line of text, so it is accepted
        // and dropped, as other diagrams here do.
        var accessibilityTitle =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accTitle"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<string?>(static _ => null);

        var accessibilityDescription =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accDescr"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Literals.Char(':').SkipAnd(restOfLine)
                        .Or(
                            // `accDescr { ... }` may run over several lines.
                            Literals.Char('{')
                                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                                .AndSkip(Literals.Char('}'))
                                .AndSkip(CommonParsers.InlineWhitespace)))
                .AndSkip(CommonParsers.LineEnd)
                .Then<string?>(static _ => null);

        var headerLine =
            OneOf(
                title,
                accessibilityTitle,
                accessibilityDescription,
                skipLine.Then<string?>(static _ => null));

        // The keyword must be followed by whitespace or the end of its line, so that a word merely starting
        // with "info" is not taken for it.
        var keyword =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("info"))
                .AndSkip(Not(Literals.NoneOf(" \t\r\n", maxSize: 1)));

        parser =
            keyword
                .SkipAnd(showInfo.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.Comment.Or(CommonParsers.LineEnd).Optional())
                .AndSkip(skipLines)
                .And(showInfo.Optional())
                .And(headerLine.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ =>
                {
                    var (sameLineShowInfo, ownLineShowInfo, lines) = _;
                    var titleText = lines.LastOrDefault(static line => line is not null);
                    return new InfoModel
                    {
                        ShowInfo = sameLineShowInfo.HasValue || ownLineShowInfo.HasValue,
                        Title = string.IsNullOrEmpty(titleText) ? null : titleText
                    };
                });
    }

    public DiagramParseResult<InfoModel> Parse(string input) => parser.ParseDiagram(input);
}

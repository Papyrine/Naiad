class QuadrantParser : IDiagramParser<QuadrantModel>
{
    static Parser<QuadrantModel> parser;

    static QuadrantParser()
    {
        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsTrimmedString();

        // Title: title My Chart
        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        // X-axis: x-axis Low --> High
        var xAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("x-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(
                    Literals.NoneOf("-").AsString()
                        .When(static (_, text) => !text.TrimEnd().EndsWith('-'))
                        .Or(Literals.NoneOf("\r\n-", minSize: 0).AsString()))
                .AndSkip(Literals.Text("-->"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((left, right) => (left: left.Trim().TrimEnd('-').Trim(), right));

        // Y-axis: y-axis Low --> High
        var yAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("y-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(
                    Literals.NoneOf("-").AsString()
                        .When(static (_, text) => !text.TrimEnd().EndsWith('-'))
                        .Or(Literals.NoneOf("\r\n-", minSize: 0).AsString()))
                .AndSkip(Literals.Text("-->"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((bottom, top) => (bottom: bottom.Trim().TrimEnd('-').Trim(), top));

        // Quadrant labels: quadrant-1 Label
        var quadrantLabelParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("quadrant-"))
                .SkipAnd(Literals.Pattern(char.IsDigit, minSize: 1, maxSize: 1))
                .AndSkip(CommonParsers.RequiredWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((digit, label) => (quadrant: digit.Span[0] - '0', label));

        // Coordinate value. Mirrors Mermaid's lexer rule (1)|(0(\.\d+)?): either a bare 1, or a 0 with
        // an optional fraction. Anything else (1.0, 1.5, 2, -3, ...) fails to parse, exactly as mermaid.js
        // rejects it with a lexical error — so a parsed value is always within [0, 1].
        var coordinateParser =
            Literals.Char('1').Then(1d)
                .Or(
                    // Parse the matched 0 / 0.x span directly instead of building a "0."+frac string.
                    Capture(Literals.Char('0').And(Literals.Char('.').And(Literals.AnyOf("0123456789")).Optional()))
                        .Then(static _ => double.Parse(_.Span, NumberStyles.Float, CultureInfo.InvariantCulture)));

        // Point: Name: [0.5, 0.7]
        var pointParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.NoneOf(":\r\n").AsTrimmedString())
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char('['))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(coordinateParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(','))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(coordinateParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(']'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((name, x, y) =>
                    new QuadrantPoint
                    {
                        Name = name,
                        X = x,
                        Y = y
                    });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                titleParser.Then<IQuadrantContent?>(static _ => new TitleItem(_)),
                xAxisParser.Then<IQuadrantContent?>(static _ => new XAxisItem(_.left, _.right)),
                yAxisParser.Then<IQuadrantContent?>(static _ => new YAxisItem(_.bottom, _.top)),
                quadrantLabelParser.Then<IQuadrantContent?>(static _ => new QuadrantLabelItem(_.quadrant, _.label)),
                pointParser.Then<IQuadrantContent?>(static _ => new PointItem(_)),
                skipLine.Then<IQuadrantContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("quadrantChart"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static QuadrantModel BuildModel(IEnumerable<IQuadrantContent?> content)
    {
        var model = new QuadrantModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case XAxisItem xAxis:
                    model.XAxisLeft = xAxis.Left;
                    model.XAxisRight = xAxis.Right;
                    break;

                case YAxisItem yAxis:
                    model.YAxisBottom = yAxis.Bottom;
                    model.YAxisTop = yAxis.Top;
                    break;

                case QuadrantLabelItem label:
                    switch (label.Quadrant)
                    {
                        case 1: model.Quadrant1Label = label.Label; break;
                        case 2: model.Quadrant2Label = label.Label; break;
                        case 3: model.Quadrant3Label = label.Label; break;
                        case 4: model.Quadrant4Label = label.Label; break;
                    }
                    break;

                case PointItem point:
                    model.Points.Add(point.Point);
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<QuadrantModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IQuadrantContent;
    readonly record struct TitleItem(string Value) : IQuadrantContent;
    readonly record struct XAxisItem(string Left, string Right) : IQuadrantContent;
    readonly record struct YAxisItem(string Bottom, string Top) : IQuadrantContent;
    readonly record struct QuadrantLabelItem(int Quadrant, string Label) : IQuadrantContent;
    readonly record struct PointItem(QuadrantPoint Point) : IQuadrantContent;
}

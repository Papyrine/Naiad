class XYChartParser : IDiagramParser<XYChartModel>
{
    static readonly Parser<XYChartModel> parser;

    static XYChartParser()
    {
        // Rest of line (for text content)
        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsString();

        // Quoted string
        var quotedString =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        // Text that opens with a quote is a quoted string: left unclosed, it is not read as plain text
        // instead.
        var unquoted =
            Not(Literals.Char('"'));

        // Title: title "My Chart" or title My Chart
        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.Or(unquoted.SkipAnd(restOfLine)))
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => _.Trim());

        // Number parser
        var numberParser = CommonParsers.SignedDecimal;

        // Category item (unquoted or quoted)
        var categoryItem =
            quotedString.Or(
                unquoted.SkipAnd(Literals.NoneOf(",]\r\n").AsTrimmedString()));

        // Category list: [jan, feb, mar] or ["Jan", "Feb", "Mar"]. Blanks may surround a comma, but the
        // closing bracket comes straight after the last item. An unquoted item takes the blanks after it
        // as its own, a quoted one does not: `["Jan", "Feb" ]` is not read.
        var categoryListParser =
            Literals.Char('[')
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace),
                        categoryItem))
                .AndSkip(Literals.Char(']'))
                .Then(static _ => _.ToList());

        // X-axis: x-axis [cat1, cat2] or x-axis "Label" [cat1, cat2]
        var xAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("x-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.RequiredWhitespace).Optional())
                .And(categoryListParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((label, categories) => (label: label.OrSome(""), categories));

        // Y-axis: y-axis "Label" min --> max or y-axis min --> max
        var yAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("y-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.RequiredWhitespace).Optional())
                .And(
                    numberParser
                        .AndSkip(CommonParsers.InlineWhitespace)
                        .AndSkip(Literals.Text("-->"))
                        .AndSkip(CommonParsers.InlineWhitespace)
                        .And(numberParser)
                        .Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((label, range) =>
                    (label: label.OrSome(""),
                        min: range.HasValue ? range.Value.Item1 : 0,
                        max: range.HasValue ? range.Value.Item2 : 100));

        // Data list: [100, 200, 300]. As in a category list, the closing bracket comes straight after
        // the last number: `[100, 200 ]` is not read.
        var dataListParser =
            Literals.Char('[')
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace),
                        numberParser))
                .AndSkip(Literals.Char(']'))
                .Then(static _ => _.ToList());

        // Bar series: bar [100, 200, 300]
        var barParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("bar"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(dataListParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => new ChartSeries { Type = ChartSeriesType.Bar, Data = _ });

        // Line series: line [100, 200, 300]
        var lineParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("line"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(dataListParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => new ChartSeries { Type = ChartSeriesType.Line, Data = _ });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                titleParser.Then<IXYContent?>(static _ => new TitleItem(_)),
                xAxisParser.Then<IXYContent?>(static _ => new XAxisItem(_.label, _.categories)),
                yAxisParser.Then<IXYContent?>(static _ => new YAxisItem(_.label, _.min, _.max)),
                barParser.Then<IXYContent?>(static _ => new SeriesItem(_)),
                lineParser.Then<IXYContent?>(static _ => new SeriesItem(_)),
                skipLine.Then<IXYContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("xychart"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static XYChartModel BuildModel(IEnumerable<IXYContent?> content)
    {
        var model = new XYChartModel();

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case XAxisItem xAxis:
                    model.XAxisLabel = string.IsNullOrEmpty(xAxis.Label) ? null : xAxis.Label;
                    model.XAxisCategories.AddRange(xAxis.Categories);
                    break;

                case YAxisItem yAxis:
                    model.YAxisLabel = string.IsNullOrEmpty(yAxis.Label) ? null : yAxis.Label;
                    model.YAxisMin = yAxis.Min;
                    model.YAxisMax = yAxis.Max;
                    break;

                case SeriesItem series:
                    model.Series.Add(series.Series);
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<XYChartModel> Parse(string input) => parser.ParseDiagram(input);

    interface IXYContent;
    readonly record struct TitleItem(string Value) : IXYContent;
    readonly record struct XAxisItem(string Label, List<string> Categories) : IXYContent;
    readonly record struct YAxisItem(string Label, double Min, double Max) : IXYContent;
    readonly record struct SeriesItem(ChartSeries Series) : IXYContent;
}

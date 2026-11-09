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

        // Category item (unquoted or quoted)
        var categoryItem =
            quotedString.Or(
                unquoted.SkipAnd(Literals.NoneOf(",]\r\n").AsTrimmedString()));

        // Category list: [jan, feb, mar] or ["Jan", "Feb", "Mar"]. Blanks may surround a comma and the
        // brackets.
        var categoryListParser =
            Literals.Char('[')
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace),
                        categoryItem))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(']'))
                .Then(static _ => _.ToList());

        // Number parser
        var numberParser = CommonParsers.SignedDecimal;

        // A numeric axis range: 0 --> 100
        var rangeParser =
            numberParser
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Text("-->"))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(numberParser);

        // X-axis: categories (x-axis [cat1, cat2]), a numeric range (x-axis 1 --> 12) or neither, each
        // optionally after a title (x-axis "Label" [cat1, cat2]).
        var xAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("x-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(
                    categoryListParser.Then(static _ => new XAxisItem("", _, null, null))
                        .Or(rangeParser.Then(static _ => new XAxisItem("", [], _.Item1, _.Item2)))
                        .Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (label, axis) =>
                    axis.OrSome(new XAxisItem("", [], null, null)) with {Label = label.OrSome("")});

        // Y-axis: y-axis "Label" min --> max or y-axis min --> max. Without a range the axis spans the
        // data, as Mermaid's does.
        var yAxisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("y-axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(rangeParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((label, range) =>
                    (label: label.OrSome(""),
                        min: range.HasValue ? range.Value.Item1 : (double?)null,
                        max: range.HasValue ? range.Value.Item2 : (double?)null));

        // Data list: [100, 200, 300]. As in a category list, blanks may surround a comma and the brackets.
        var dataListParser =
            Literals.Char('[')
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Separated(
                        CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace),
                        numberParser))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(']'))
                .Then(static _ => _.ToList());

        // Bar series: bar [100, 200, 300] or bar "Sales" [100, 200, 300]
        var barParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("bar"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(dataListParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (name, data) => new ChartSeries { Type = ChartSeriesType.Bar, Data = data, Name = name.OrNull() });

        // Line series: line [100, 200, 300] or line "Target" [100, 200, 300]
        var lineParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("line"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(quotedString.AndSkip(CommonParsers.InlineWhitespace).Optional())
                .And(dataListParser)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map(static (name, data) => new ChartSeries { Type = ChartSeriesType.Line, Data = data, Name = name.OrNull() });

        // The chart's orientation, after its keyword: bars stand up by default and lie down when horizontal.
        var orientationParser =
            TextIgnoreCase("horizontal").Then(true)
                .Or(TextIgnoreCase("vertical").Then(false));

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                titleParser.Then<IXYContent?>(static _ => new TitleItem(_)),
                xAxisParser.Then<IXYContent?>(static _ => _),
                yAxisParser.Then<IXYContent?>(static _ => new YAxisItem(_.label, _.min, _.max)),
                barParser.Then<IXYContent?>(static _ => new SeriesItem(_)),
                lineParser.Then<IXYContent?>(static _ => new SeriesItem(_)),
                skipLine.Then<IXYContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("xychart"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(orientationParser.Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .And(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Map(static (horizontal, content) => BuildModel(horizontal.OrSome(false), content));
    }

    static XYChartModel BuildModel(bool horizontal, IEnumerable<IXYContent?> content)
    {
        var model = new XYChartModel
        {
            Horizontal = horizontal
        };

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
                    model.XAxisMin = xAxis.Min;
                    model.XAxisMax = xAxis.Max;
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
    readonly record struct XAxisItem(string Label, List<string> Categories, double? Min, double? Max) : IXYContent;
    readonly record struct YAxisItem(string Label, double? Min, double? Max) : IXYContent;
    readonly record struct SeriesItem(ChartSeries Series) : IXYContent;
}

class RadarParser : IDiagramParser<RadarModel>
{
    static Parser<RadarModel> parser;

    static RadarParser()
    {
        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString();

        var number = CommonParsers.SignedDecimal;

        var listSeparator =
            CommonParsers.InlineWhitespace.SkipAnd(Literals.Char(',')).SkipAnd(CommonParsers.InlineWhitespace);

        // Label: ["label"] or [label]. An opening quote makes it the quoted form: one that is never
        // closed is not read as plain text instead.
        var label =
            Between(
                    Literals.Char('['),
                    CommonParsers.DoubleQuotedString
                        .Or(Not(Literals.Char('"')).SkipAnd(Literals.NoneOf("]\r\n", minSize: 0)).AsString()),
                    Literals.Char(']'))
                .Then(static _ => _.Trim());

        // Axis: id or id["label"]
        var axisItemParser =
            identifier
                .And(CommonParsers.InlineWhitespace.SkipAnd(label).Optional())
                .Map(static (id, text) =>
                    new RadarAxis
                    {
                        Id = id,
                        Label = text.OrNull() ?? id
                    });

        // Axis list: axis id1, id2["label"], id3
        var axisParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("axis"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(Separated(listSeparator, axisItemParser))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => _.ToList());

        // A value, optionally named for the axis it belongs to: 85 or math: 85
        var entryParser =
            identifier.AndSkip(CommonParsers.InlineWhitespace).AndSkip(Literals.Char(':')).Optional()
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(number)
                .Map(static (axis, value) => new Entry(axis.OrNull(), value));

        // Value list: {1, 2, 3} or {math: 1, art: 2}. It may run over several lines.
        var valueList =
            Literals.Char('{')
                .SkipAnd(ZeroOrMore(char.IsWhiteSpace))
                .SkipAnd(
                    Separated(
                        ZeroOrMore(char.IsWhiteSpace).SkipAnd(Literals.Char(',')).SkipAnd(ZeroOrMore(char.IsWhiteSpace)),
                        entryParser))
                .AndSkip(ZeroOrMore(char.IsWhiteSpace))
                .AndSkip(Literals.Char('}'))
                .Then(static _ => _.ToList());

        // Curve definition: curve id["label"]{1, 2, 3}
        var curveItemParser =
            identifier
                .And(CommonParsers.InlineWhitespace.SkipAnd(label).Optional())
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(valueList)
                .Map(static (id, text, values) => new CurveSpec(id, text.OrNull() ?? id, values));

        // Curve line: curve id1["label"]{1, 2, 3}, id2{4, 5, 6}
        var curveLineParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("curve"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(Separated(listSeparator, curveItemParser))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => _.ToList());

        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(Literals.NoneOf("\r\n", minSize: 0))
                .AndSkip(CommonParsers.LineEnd)
                .AsTrimmedString();

        // Options: max 100, min 0, ticks 5, graticule polygon, showLegend false
        var optionValue =
            OneOf(
                TextIgnoreCase("max").SkipAnd(CommonParsers.RequiredWhitespace).SkipAnd(number)
                    .Then<Action<RadarModel>>(static _ => model => model.Max = _),
                TextIgnoreCase("min").SkipAnd(CommonParsers.RequiredWhitespace).SkipAnd(number)
                    .Then<Action<RadarModel>>(static _ => model => model.Min = _),
                TextIgnoreCase("ticks").SkipAnd(CommonParsers.RequiredWhitespace).SkipAnd(CommonParsers.UnsignedInt)
                    .Then<Action<RadarModel>>(static _ => model => model.Ticks = Math.Max(1, _)),
                TextIgnoreCase("graticule").SkipAnd(CommonParsers.RequiredWhitespace).SkipAnd(
                        TextIgnoreCase("polygon").Then(GraticuleType.Polygon)
                            .Or(TextIgnoreCase("circle").Then(GraticuleType.Circle)))
                    .Then<Action<RadarModel>>(static _ => model => model.Graticule = _),
                TextIgnoreCase("showLegend").SkipAnd(CommonParsers.RequiredWhitespace).SkipAnd(
                        TextIgnoreCase("true").Then(true)
                            .Or(TextIgnoreCase("false").Then(false)))
                    .Then<Action<RadarModel>>(static _ => model => model.ShowLegend = _));

        var optionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(optionValue)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd);

        var skipLine =
            CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Comment)
                .Or(CommonParsers.InlineWhitespace.SkipAnd(CommonParsers.Newline));

        // Content item
        var contentItem =
            OneOf(
                titleParser.Then<IRadarContent?>(static _ => new TitleItem(_)),
                axisParser.Then<IRadarContent?>(static _ => new AxisItem(_)),
                curveLineParser.Then<IRadarContent?>(static _ => new CurveItem(_)),
                optionParser.Then<IRadarContent?>(static _ => new OptionItem(_)),
                skipLine.Then<IRadarContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("radar"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static RadarModel BuildModel(IEnumerable<IRadarContent?> content)
    {
        var model = new RadarModel();
        var curves = new List<CurveSpec>();

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case AxisItem axis:
                    foreach (var a in axis.Axes)
                        model.Axes.Add(a);
                    break;

                case CurveItem curve:
                    curves.AddRange(curve.Curves);
                    break;

                case OptionItem option:
                    option.Apply(model);
                    break;
            }
        }

        // Named values can only be put in order once every axis is known, and an axis line may follow
        // the curve that names it.
        foreach (var spec in curves)
        {
            var curve = new RadarCurve
            {
                Id = spec.Id,
                Label = spec.Label
            };
            curve.Values.AddRange(OrderValues(spec.Entries, model.Axes));
            model.Curves.Add(curve);
        }

        return model;
    }

    // Values given by position are taken as written. Values named for their axis are put in axis order,
    // with zero for an axis the curve leaves out.
    static IEnumerable<double> OrderValues(List<Entry> entries, List<RadarAxis> axes)
    {
        if (entries.All(_ => _.Axis is null))
        {
            return entries.Select(_ => _.Value);
        }

        var named = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry.Axis is not null)
            {
                named[entry.Axis] = entry.Value;
            }
        }

        return axes.Select(_ => named.GetValueOrDefault(_.Id));
    }

    public DiagramParseResult<RadarModel> Parse(string input) => parser.ParseDiagram(input);

    readonly record struct Entry(string? Axis, double Value);
    sealed record CurveSpec(string Id, string Label, List<Entry> Entries);

    interface IRadarContent;
    readonly record struct TitleItem(string Value) : IRadarContent;
    readonly record struct AxisItem(List<RadarAxis> Axes) : IRadarContent;
    readonly record struct CurveItem(List<CurveSpec> Curves) : IRadarContent;
    readonly record struct OptionItem(Action<RadarModel> Apply) : IRadarContent;
}

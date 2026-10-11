class RadarParser : IDiagramParser<RadarModel>
{
    static Parser<char, RadarModel> parser;

    static RadarParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        var number = CommonParsers.SignedDecimal;

        var listSeparator =
            CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace);

        // Label: ["label"] or [label]
        var label =
            Char('[')
                .Then(CommonParsers.DoubleQuotedString.Or(Token(_ => _ != ']' && _ != '\r' && _ != '\n').ManyString()))
                .Before(Char(']'))
                .Select(_ => _.Trim());

        // Axis: id or id["label"]
        var axisItemParser =
            from id in identifier
            from text in Try(CommonParsers.InlineWhitespace.Then(label)).Optional()
            select new RadarAxis
            {
                Id = id,
                Label = text.GetValueOrDefault() ?? id
            };

        // Axis list: axis id1, id2["label"], id3
        var axisParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("axis")
            from ___ in CommonParsers.RequiredWhitespace
            from axes in axisItemParser.SeparatedAtLeastOnce(Try(listSeparator))
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select axes.ToList();

        // A value, optionally named for the axis it belongs to: 85 or math: 85
        var entryParser =
            from axis in Try(identifier.Before(CommonParsers.InlineWhitespace).Before(Char(':'))).Optional()
            from _ in CommonParsers.InlineWhitespace
            from value in number
            select new Entry(axis.GetValueOrDefault(), value);

        // Value list: {1, 2, 3} or {math: 1, art: 2}. It may run over several lines.
        var valueList =
            Char('{')
                .Then(SkipWhitespaces)
                .Then(entryParser.SeparatedAtLeastOnce(Try(SkipWhitespaces.Then(Char(',')).Then(SkipWhitespaces))))
                .Before(SkipWhitespaces)
                .Before(Char('}'))
                .Select(_ => _.ToList());

        // Curve definition: curve id["label"]{1, 2, 3}
        var curveItemParser =
            from id in identifier
            from text in Try(CommonParsers.InlineWhitespace.Then(label)).Optional()
            from _ in CommonParsers.InlineWhitespace
            from values in valueList
            select new CurveSpec(id, text.GetValueOrDefault() ?? id, values);

        // Curve line: curve id1["label"]{1, 2, 3}, id2{4, 5, 6}
        var curveLineParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("curve")
            from ___ in CommonParsers.RequiredWhitespace
            from curves in curveItemParser.SeparatedAtLeastOnce(Try(listSeparator))
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select curves.ToList();

        var titleParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("title")
            from ___ in CommonParsers.RequiredWhitespace
            from title in Token(_ => _ != '\r' && _ != '\n').ManyString()
            from ____ in CommonParsers.LineEnd
            select title.Trim();

        // Options: max 100, min 0, ticks 5, graticule polygon, showLegend false
        var optionValue =
            OneOf(
                Try(CIString("max").Then(CommonParsers.RequiredWhitespace).Then(number))
                    .Select<Action<RadarModel>>(value => model => model.Max = value),
                Try(CIString("min").Then(CommonParsers.RequiredWhitespace).Then(number))
                    .Select<Action<RadarModel>>(value => model => model.Min = value),
                Try(CIString("ticks").Then(CommonParsers.RequiredWhitespace).Then(CommonParsers.UnsignedInt))
                    .Select<Action<RadarModel>>(value => model => model.Ticks = Math.Max(1, value)),
                Try(CIString("graticule").Then(CommonParsers.RequiredWhitespace).Then(
                        Try(CIString("polygon")).ThenReturn(GraticuleType.Polygon)
                            .Or(CIString("circle").ThenReturn(GraticuleType.Circle))))
                    .Select<Action<RadarModel>>(value => model => model.Graticule = value),
                CIString("showLegend").Then(CommonParsers.RequiredWhitespace).Then(
                        Try(CIString("true")).ThenReturn(true)
                            .Or(CIString("false").ThenReturn(false)))
                    .Select<Action<RadarModel>>(value => model => model.ShowLegend = value));

        var optionParser =
            from _ in CommonParsers.InlineWhitespace
            from option in optionValue
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            select option;

        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        // Content item
        var contentItem =
            OneOf(
                Try(titleParser.Select<IRadarContent?>(_ => new TitleItem(_))),
                Try(axisParser.Select<IRadarContent?>(_ => new AxisItem(_))),
                Try(curveLineParser.Select<IRadarContent?>(_ => new CurveItem(_))),
                Try(optionParser.Select<IRadarContent?>(_ => new OptionItem(_))),
                skipLine.ThenReturn<IRadarContent?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.BetaKeyword("radar")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1);
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

    public Result<char, RadarModel> Parse(string input) => parser.Parse(input);

    readonly record struct Entry(string? Axis, double Value);
    sealed record CurveSpec(string Id, string Label, List<Entry> Entries);

    interface IRadarContent;
    readonly record struct TitleItem(string Value) : IRadarContent;
    readonly record struct AxisItem(List<RadarAxis> Axes) : IRadarContent;
    readonly record struct CurveItem(List<CurveSpec> Curves) : IRadarContent;
    readonly record struct OptionItem(Action<RadarModel> Apply) : IRadarContent;
}

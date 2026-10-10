class RadarParser : IDiagramParser<RadarModel>
{
    static Parser<char, RadarModel> parser;

    static RadarParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        var number = CommonParsers.SignedDecimal;

        // Quoted label: ["label"]
        var quotedLabel =
            Char('[').Then(Char('"')).Then(Token(_ => _ != '"').ManyString()).Before(Char('"')).Before(Char(']'));

        // Axis list: axis id1, id2, id3
        var axisParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("axis")
            from ___ in CommonParsers.RequiredWhitespace
            from axes in identifier.SeparatedAtLeastOnce(
                CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace))
            from ____ in CommonParsers.InlineWhitespace
            from _____ in CommonParsers.LineEnd
            select axes.Select(_ => new RadarAxis { Id = _, Label = _ }).ToList();

        // Value list: {1, 2, 3}
        var valueList =
            Char('{')
                .Then(CommonParsers.InlineWhitespace)
                .Then(number.SeparatedAtLeastOnce(
                    CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace)))
                .Before(CommonParsers.InlineWhitespace)
                .Before(Char('}'))
                .Select(_ => _.ToList());

        // Curve definition: curve id["label"]{1, 2, 3}
        var curveItemParser =
            from id in identifier
            from label in quotedLabel.Optional()
            from values in valueList
            select new RadarCurve
            {
                Id = id,
                Label = label.GetValueOrDefault() ?? id
            }.WithValues(values);

        // Curve line: curve id1["label"]{1, 2, 3}, id2{4, 5, 6}
        var curveLineParser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("curve")
            from ___ in CommonParsers.RequiredWhitespace
            from curves in curveItemParser.SeparatedAtLeastOnce(
                CommonParsers.InlineWhitespace.Then(Char(',')).Then(CommonParsers.InlineWhitespace))
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

        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        // Content item
        var contentItem =
            OneOf(
                Try(titleParser.Select<IRadarContent?>(_ => new TitleItem(_))),
                Try(axisParser.Select<IRadarContent?>(_ => new AxisItem(_))),
                Try(curveLineParser.Select<IRadarContent?>(_ => new CurveItem(_))),
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
                    foreach (var c in curve.Curves)
                        model.Curves.Add(c);
                    break;
            }
        }

        return model;
    }

    public Result<char, RadarModel> Parse(string input) => parser.Parse(input);

    interface IRadarContent;
    readonly record struct TitleItem(string Value) : IRadarContent;
    readonly record struct AxisItem(List<RadarAxis> Axes) : IRadarContent;
    readonly record struct CurveItem(List<RadarCurve> Curves) : IRadarContent;
}
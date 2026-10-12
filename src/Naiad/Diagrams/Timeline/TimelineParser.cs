class TimelineParser : IDiagramParser<TimelineModel>
{
    static readonly Parser<TimelineModel> parser;

    static TimelineParser()
    {
        // Rest of line (for text content)
        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsTrimmedString();

        // Title: title My Timeline
        var titleParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        // Section: section Section Name
        var sectionParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("section"))
                .SkipAnd(CommonParsers.RequiredWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        // Period with event: 2020 : Event description
        var periodEventParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.NoneOf(":\r\n").AsTrimmedString())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Map((period, eventText) => new PeriodItem(period, eventText));

        // Continuation event: : Another event (no period, just event)
        var continuationEventParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd);

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Content item
        var contentItem =
            OneOf(
                titleParser.Then<ITimelineContent?>(static _ => new TitleItem(_)),
                sectionParser.Then<ITimelineContent?>(static _ => new SectionItem(_)),
                periodEventParser.Then<ITimelineContent?>(static _ => _),
                continuationEventParser.Then<ITimelineContent?>(static _ => new ContinuationItem(_)),
                skipLine.Then<ITimelineContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("timeline"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static TimelineModel BuildModel(IEnumerable<ITimelineContent?> content)
    {
        var model = new TimelineModel();
        TimelineSection? currentSection = null;
        TimePeriod? currentPeriod = null;

        foreach (var item in content)
        {
            switch (item)
            {
                case TitleItem title:
                    model.Title = title.Value;
                    break;

                case SectionItem section:
                    currentSection = new() { Name = section.Name };
                    model.Sections.Add(currentSection);
                    currentPeriod = null;
                    break;

                case PeriodItem period:
                    if (currentSection == null)
                    {
                        currentSection = new();
                        model.Sections.Add(currentSection);
                    }
                    currentPeriod = new()
                    {
                        Label = period.Period
                    };
                    currentPeriod.Events.AddRange(SplitEvents(period.EventText));
                    currentSection.Periods.Add(currentPeriod);
                    break;

                case ContinuationItem continuation:
                    currentPeriod?.Events.AddRange(SplitEvents(continuation.EventText));
                    break;
            }
        }

        return model;
    }

    /// <summary>
    /// Everything past a period's colon is itself a colon-separated list of events, so
    /// <c>2004 : Facebook : Gmail</c> hangs two events off 2004 rather than one reading
    /// "Facebook : Gmail". Blank entries — a trailing colon, or a period declared with no event at
    /// all — contribute nothing.
    /// </summary>
    static IEnumerable<string> SplitEvents(string text) =>
        text.Split(':')
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0);

    public DiagramParseResult<TimelineModel> Parse(string input) => parser.ParseDiagram(input);

    interface ITimelineContent;
    readonly record struct TitleItem(string Value) : ITimelineContent;
    readonly record struct SectionItem(string Name) : ITimelineContent;
    readonly record struct PeriodItem(string Period, string EventText) : ITimelineContent;
    readonly record struct ContinuationItem(string EventText) : ITimelineContent;
}

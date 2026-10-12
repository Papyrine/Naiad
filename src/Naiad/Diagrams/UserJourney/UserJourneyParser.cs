class UserJourneyParser : IDiagramParser<UserJourneyModel>
{
    static readonly Parser<UserJourneyModel> parser;

    static UserJourneyParser()
    {
        // Rest of line (for text content)
        var restOfLine =
            Literals.NoneOf("\r\n", minSize: 0).AsTrimmedString();

        // Title: title My Journey
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

        // Actor list: Me, Cat, Dog
        var actorListParser =
            Separated(
                    Literals.Char(',').SkipAnd(CommonParsers.InlineWhitespace),
                    Literals.NoneOf(",\r\n").AsTrimmedString())
                .Then(static _ => _.ToList());

        // Task: Task Name: 5: Me, Cat
        var taskParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.NoneOf(":\r\n").AsTrimmedString())
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(CommonParsers.UnsignedInt)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(actorListParser)
                .AndSkip(CommonParsers.LineEnd)
                .Map((name, score, actors) =>
                    new JourneyTask
                    {
                        Name = name,
                        Score = score,
                        Actors = actors
                    });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Content item
        var contentItem =
            OneOf(
                titleParser.Then<IUserJourneyContent?>(static _ => new TitleItem(_)),
                sectionParser.Then<IUserJourneyContent?>(static _ => new SectionItem(_)),
                taskParser.Then<IUserJourneyContent?>(static _ => new TaskItem(_)),
                skipLine.Then<IUserJourneyContent?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("journey"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static UserJourneyModel BuildModel(IEnumerable<IUserJourneyContent?> content)
    {
        var model = new UserJourneyModel();
        JourneySection? currentSection = null;

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
                    break;

                case TaskItem taskItem:
                    if (currentSection == null)
                    {
                        currentSection = new();
                        model.Sections.Add(currentSection);
                    }
                    currentSection.Tasks.Add(taskItem.Task);
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<UserJourneyModel> Parse(string input) => parser.ParseDiagram(input);

    internal interface IUserJourneyContent;
    readonly record struct TitleItem(string Value) : IUserJourneyContent;
    readonly record struct SectionItem(string Name) : IUserJourneyContent;
    readonly record struct TaskItem(JourneyTask Task) : IUserJourneyContent;
}

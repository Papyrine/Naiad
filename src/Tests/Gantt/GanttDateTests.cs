using Naiad.Diagrams.Gantt;

public class GanttDateTests
{
    static List<GanttTask> Schedule(string input)
    {
        var model = new GanttParser().Parse(input).Value;
        new GanttRenderer().Render(model, RenderOptions.Default);
        return model.Sections.SelectMany(_ => _.Tasks).ToList();
    }

    static DateTime Day(int year, int month, int day) => new(year, month, day);

    // The first example in Mermaid's gantt documentation. "another task" has no start of its own; it used
    // to be put at today's date, which stretched the chart from 2014 to the present.
    [Test]
    public async Task TaskWithoutAStart_BeginsWhereThePreviousOneEnds()
    {
        const string input =
            """
            gantt
                title A Gantt Diagram
                dateFormat YYYY-MM-DD
                section Section
                    A task          :a1, 2014-01-01, 30d
                    Another task    :after a1, 20d
                section Another
                    Task in Another :2014-01-12, 12d
                    another task    :24d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[1].ComputedStart).IsEqualTo(Day(2014, 1, 31));
        await Assert.That(tasks[2].ComputedStart).IsEqualTo(Day(2014, 1, 12));
        await Assert.That(tasks[2].ComputedEnd).IsEqualTo(Day(2014, 1, 24));
        await Assert.That(tasks[3].ComputedStart).IsEqualTo(Day(2014, 1, 24));
        await Assert.That(tasks[3].ComputedEnd).IsEqualTo(Day(2014, 2, 17));
    }

    [Test]
    public async Task After_SeveralTasks_StartsWhenTheLastOfThemEnds()
    {
        const string input =
            """
            gantt
                Short :a1, 2024-01-01, 5d
                Long  :b1, 2024-01-01, 20d
                Both  :c1, after a1 b1, 3d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[2].ComputedStart).IsEqualTo(Day(2024, 1, 21));
        await Assert.That(tasks[2].ComputedEnd).IsEqualTo(Day(2024, 1, 24));
    }

    [Test]
    public async Task After_ATaskDeclaredLater_IsResolved()
    {
        const string input =
            """
            gantt
                Third  :c1, after b1, 2d
                Second :b1, after a1, 2d
                First  :a1, 2024-03-01, 2d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[1].ComputedStart).IsEqualTo(Day(2024, 3, 3));
        await Assert.That(tasks[0].ComputedStart).IsEqualTo(Day(2024, 3, 5));
        await Assert.That(tasks[0].ComputedEnd).IsEqualTo(Day(2024, 3, 7));
    }

    // An unknown id used to leave the task in year 0001, and the chart millions of pixels wide.
    [Test]
    public async Task After_AnUnknownTask_StartsToday()
    {
        const string input =
            """
            gantt
                Task A :a1, 2024-01-01, 10d
                Task B :b1, after nosuch, 5d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[1].ComputedStart).IsEqualTo(DateTime.Today);
        await Assert.That(tasks[1].ComputedEnd).IsEqualTo(DateTime.Today.AddDays(5));
    }

    [Test]
    public async Task After_InACycle_StartsToday()
    {
        const string input =
            """
            gantt
                Task A :a1, after b1, 2d
                Task B :b1, after a1, 3d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[0].ComputedStart).IsEqualTo(DateTime.Today);
        await Assert.That(tasks[1].ComputedStart).IsEqualTo(DateTime.Today.AddDays(2));
    }

    [Test]
    public async Task DateFormat_IsUsedToReadTaskDates()
    {
        const string input =
            """
            gantt
                dateFormat DD-MM-YYYY
                Task A :a1, 01-02-2024, 10d
                Task B :b1, 05-03-2024, 20-03-2024
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[0].ComputedStart).IsEqualTo(Day(2024, 2, 1));
        await Assert.That(tasks[0].ComputedEnd).IsEqualTo(Day(2024, 2, 11));
        await Assert.That(tasks[1].ComputedStart).IsEqualTo(Day(2024, 3, 5));
        await Assert.That(tasks[1].ComputedEnd).IsEqualTo(Day(2024, 3, 20));
    }

    [Test]
    public async Task DateFormat_DeclaredAfterTheTasks_StillApplies()
    {
        const string input =
            """
            gantt
                Task A :a1, 15/02/24, 2d
                dateFormat DD/MM/YY
            """;

        await Assert.That(Schedule(input)[0].ComputedStart).IsEqualTo(Day(2024, 2, 15));
    }

    // Issue #20: bare years are the dates here, not ids.
    [Test]
    public async Task DateFormat_YearOnly()
    {
        const string input =
            """
            gantt
                dateFormat YYYY
                section Test
                Task A: 2025, 2025
                Task B: 2025, 2026
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[0].ComputedStart).IsEqualTo(Day(2025, 1, 1));
        await Assert.That(tasks[1].ComputedStart).IsEqualTo(Day(2025, 1, 1));
        await Assert.That(tasks[1].ComputedEnd).IsEqualTo(Day(2026, 1, 1));
    }

    [Test]
    public async Task DateFormat_TimeOfDay()
    {
        const string input =
            """
            gantt
                dateFormat HH:mm
                Standup :a1, 09:00, 90m
                Review  :b1, after a1, 2h
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[0].ComputedStart.TimeOfDay).IsEqualTo(new TimeSpan(9, 0, 0));
        await Assert.That(tasks[0].ComputedEnd.TimeOfDay).IsEqualTo(new TimeSpan(10, 30, 0));
        await Assert.That(tasks[1].ComputedEnd.TimeOfDay).IsEqualTo(new TimeSpan(12, 30, 0));
    }

    [Test]
    [Arguments("YYYY-MM-DD", "yyyy-MM-dd")]
    [Arguments("DD/MM/YY", "dd\\/MM\\/yy")]
    [Arguments("D.M.YYYY", "d.M.yyyy")]
    [Arguments("YYYY-MM-DD HH:mm:ss", "yyyy-MM-dd HH\\:mm\\:ss")]
    [Arguments("hh:mm A", "hh\\:mm tt")]
    [Arguments("YYYY-MM-DDTHH:mm:ss.SSS", "yyyy-MM-dd\\THH\\:mm\\:ss.fff")]
    [Arguments("YYYY [Q]M", "yyyy 'Q'M")]
    [Arguments("D", "%d")]
    public async Task DateFormat_IsTranslatedToDotNet(string mermaid, string dotNet) =>
        await Assert.That(GanttParser.ToNetDateFormat(mermaid)).IsEqualTo(dotNet);

    // `90m` used to be read as 90 days: any unit other than d, w and h fell through to days, and a
    // fraction was cut off.
    [Test]
    [Arguments("500ms", 0, 0.5)]
    [Arguments("30s", 0, 30)]
    [Arguments("90m", 0, 5400)]
    [Arguments("2h", 0, 7200)]
    [Arguments("3d", 0, 259200)]
    [Arguments("1.5d", 0, 129600)]
    [Arguments("2w", 0, 1209600)]
    [Arguments("2M", 2, 0)]
    [Arguments("1y", 12, 0)]
    public async Task Duration_UnitsAndFractions(string duration, int months, double seconds)
    {
        var tasks = Schedule($"gantt\n    Task A :a1, 2024-01-31, {duration}");

        var expected = new DateTime(2024, 1, 31).AddMonths(months).AddSeconds(seconds);
        await Assert.That(tasks[0].ComputedEnd).IsEqualTo(expected);
    }

    [Test]
    public async Task Positional_IdStartEnd_AndStatusTagsAnywhere()
    {
        const string input =
            """
            gantt
                Tagged first :done, crit, a1, 2024-01-01, 2024-01-05
                Tagged last  :b1, 2024-01-05, 3d, active
                Id and end   :c1, 4d
                Milestone    :milestone, m1, after c1, 0d
            """;

        var tasks = Schedule(input);

        await Assert.That(tasks[0].Id).IsEqualTo("a1");
        await Assert.That(tasks[0].Status).IsEqualTo(GanttTaskStatus.Done);
        await Assert.That(tasks[0].IsCritical).IsTrue();
        await Assert.That(tasks[0].ComputedEnd).IsEqualTo(Day(2024, 1, 5));
        await Assert.That(tasks[1].Status).IsEqualTo(GanttTaskStatus.Active);
        await Assert.That(tasks[1].ComputedEnd).IsEqualTo(Day(2024, 1, 8));
        await Assert.That(tasks[2].Id).IsEqualTo("c1");
        await Assert.That(tasks[2].ComputedStart).IsEqualTo(Day(2024, 1, 8));
        await Assert.That(tasks[2].ComputedEnd).IsEqualTo(Day(2024, 1, 12));
        await Assert.That(tasks[3].IsMilestone).IsTrue();
        await Assert.That(tasks[3].ComputedStart).IsEqualTo(Day(2024, 1, 12));
        await Assert.That(tasks[3].ComputedEnd).IsEqualTo(Day(2024, 1, 12));
    }
}

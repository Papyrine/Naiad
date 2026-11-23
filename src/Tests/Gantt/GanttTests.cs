public class GanttTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            gantt
                title Simple Gantt
                Task A :a1, 2024-01-01, 30d
                Task B :b1, 2024-01-15, 20d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TaskWithDependency()
    {
        const string input =
            """
            gantt
                title Dependent Tasks
                Task A :a1, 2024-01-01, 10d
                Task B :b1, after a1, 15d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Sections()
    {
        const string input =
            """
            gantt
                title Project Timeline
                section Planning
                    Research :a1, 2024-01-01, 7d
                    Design :a2, after a1, 14d
                section Development
                    Coding :b1, after a2, 30d
                    Testing :b2, after b1, 14d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Statuses()
    {
        const string input =
            """
            gantt
                title Task Statuses
                Done Task :done, d1, 2024-01-01, 10d
                Active Task :active, a1, 2024-01-11, 10d
                Normal Task :n1, 2024-01-21, 10d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Critical()
    {
        const string input =
            """
            gantt
                title Critical Path
                Normal :n1, 2024-01-01, 10d
                Critical :crit, c1, 2024-01-11, 10d
                Also Critical :crit, c2, after c1, 10d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Milestones()
    {
        const string input =
            """
            gantt
                title With Milestones
                Development :d1, 2024-01-01, 30d
                Release :milestone, m1, 2024-01-31, 0d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Complex()
    {
        const string input =
            """
            gantt
                title Complete Project
                dateFormat YYYY-MM-DD
                section Phase 1
                    Planning :done, p1, 2024-01-01, 7d
                    Design :done, p2, after p1, 14d
                section Phase 2
                    Development :active, d1, after p2, 30d
                    Code Review :crit, d2, after d1, 7d
                section Phase 3
                    Testing :t1, after d2, 14d
                    Deployment :t2, after t1, 3d
                    Go Live :milestone, m1, after t2, 0d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task WeeklyDuration()
    {
        const string input =
            """
            gantt
                title Weekly Tasks
                Week Task :w1, 2024-01-01, 2w
                Day Task :d1, after w1, 5d
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task DuplicateIds()
    {
        // Two tasks share the id "a1"; a third depends on it via "after a1".
        // The duplicate must not crash, and as in Mermaid the dependency resolves to the last task with
        // that id, so Task C starts after Task B.
        const string input =
            """
            gantt
                title Duplicate Ids
                Task A :a1, 2024-01-01, 10d
                Task B :a1, 2024-01-15, 10d
                Task C :c1, after a1, 5d
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task DuplicateIdsDoesNotThrow()
    {
        // Exact repro from issue #20. The bare years were once taken for ids, and duplicate ones at that;
        // under `dateFormat YYYY` they are the tasks' dates (see GanttDateTests.DateFormat_YearOnly).
        const string input =
            """
            gantt
                dateFormat YYYY
                section Test
                Task A: 2025, 2025
                Task B: 2025, 2026
            """;

        var svg = Mermaid.Render(input);

        await Assert.That(svg).Contains("<svg");
    }

    // Mermaid's docs example: tasks of a section that do not overlap share a row. a3 starts after a1 ends,
    // so it goes on a1's row; a2 overlaps a1 and takes the next one.
    [Test]
    public Task CompactDisplayMode()
    {
        const string input =
            """
            ---
            displayMode: compact
            ---
            gantt
                title A Gantt Diagram
                dateFormat  YYYY-MM-DD

                section Section
                A task           :a1, 2014-01-01, 30d
                Another task     :a2, 2014-01-20, 25d
                Another one      :a3, 2014-02-10, 20d
            """;

        return VerifySvg(input);
    }

    [Test]
    public async Task CompactDisplayMode_FromConfig()
    {
        const string body =
            """
            gantt
                dateFormat YYYY-MM-DD
                section Section
                A task       :a1, 2014-01-01, 30d
                Another one  :a3, 2014-02-10, 20d
            """;

        await Assert.That(Mermaid.Render("---\nconfig:\n  gantt:\n    displayMode: compact\n---\n" + body))
            .IsEqualTo(Mermaid.Render("---\ndisplayMode: compact\n---\n" + body));
    }

    // The axis is written in the diagram's `axisFormat`, and labels longer than the default month/day ones
    // are spaced so they do not run into each other.
    [Test]
    public Task AxisFormat()
    {
        const string input =
            """
            gantt
                title Release Plan
                dateFormat YYYY-MM-DD
                axisFormat %Y-%m-%d
                section Build
                    Design :a1, 2024-01-01, 4d
                    Implement :a2, after a1, 6d
            """;

        return VerifySvg(input);
    }

    // A label on the last tick that reaches past the end of the chart widens the drawing to hold it.
    [Test]
    public Task AxisFormat_LongLabels()
    {
        const string input =
            """
            gantt
                dateFormat YYYY-MM-DD
                axisFormat %A, %B %e
                Kickoff :a1, 2024-09-02, 10d
                Review :a2, after a1, 18d
            """;

        return VerifySvg(input);
    }

    // Mermaid takes the `gantt.axisFormat` setting when the diagram has no `axisFormat` of its own, and the
    // diagram's own when it has one.
    [Test]
    public async Task AxisFormat_FromConfig()
    {
        const string config = "---\nconfig:\n  gantt:\n    axisFormat: \"%d %b\"\n---\n";
        const string header = "gantt\n    dateFormat YYYY-MM-DD\n";
        const string task = "    Task :a1, 2024-03-01, 5d\n";

        await Assert.That(Mermaid.Render(header + task)).Contains(">03/01</text>");
        await Assert.That(Mermaid.Render(config + header + task)).Contains(">01 Mar</text>");
        await Assert.That(Mermaid.Render(config + header + "    axisFormat %b %-d\n" + task)).Contains(">Mar 1</text>");
    }
}

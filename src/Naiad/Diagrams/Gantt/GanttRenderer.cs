namespace Naiad.Diagrams.Gantt;

public class GanttRenderer : IDiagramRenderer<GanttModel>
{
    // The longest span of days a chart is drawn over: a hundred years.
    const int maxDays = 36_525;

    const double rowHeight = 30;
    const double taskBarHeight = 20;
    const double sectionHeaderHeight = 25;
    const double axisHeight = 40;
    const double titleHeight = 30;
    const double leftMargin = 150;
    const double dayWidth = 20;
    const double milestoneSize = 12;

    // Without an `axisFormat` the axis keeps Naiad's month/day labels, which its tick spacing is set for.
    const string defaultAxisFormat = "%m/%d";
    const int defaultAxisLabelLength = 5;
    const double axisCharWidthFactor = 0.6;
    const double axisLabelGap = 8;

    const string taskColor = "#4CAF50";
    const string taskDoneColor = "#808080";
    const string taskActiveColor = "#2196F3";
    const string taskCritColor = "#F44336";
    const string sectionColor = "#ECECFF";
    const string milestoneColor = "#FF9800";

    public SvgDocument Render(GanttModel model, RenderOptions options)
    {
        // Compute task dates
        var tasks = ComputeTaskDates(model);

        if (tasks.Count == 0)
        {
            // Empty chart
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "No tasks",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        // Calculate date range
        var minDate = tasks.Min(_ => _.ComputedStart);
        var maxDate = tasks.Max(_ => _.ComputedEnd);
        var totalDays = (maxDate - minDate).Days + 1;

        // The chart is drawn a day at a time, so tasks dated centuries apart would have it step through
        // millions of days, and draw a line for every week of them.
        if (totalDays > maxDays)
        {
            throw new MermaidException(
                string.Create(CultureInfo.InvariantCulture, $"Failed to render gantt: the tasks span {totalDays:N0} days, more than the 100 years a chart is drawn over"));
        }

        // `gantt.displayMode: compact` (or a front matter `displayMode: compact`) lets tasks of a section
        // that do not overlap share a row. The rows then hold several tasks, so their names are only on
        // the bars, not down the left.
        var compact = model.Config.GetString("gantt", "displayMode") == "compact";
        var taskRows = new Dictionary<GanttTask, int>();
        var sectionRows = new Dictionary<GanttSection, int>();
        foreach (var section in model.Sections)
        {
            sectionRows[section] = AssignRows(section.Tasks, compact, taskRows);
        }

        // Calculate dimensions
        var totalRows = 0;
        foreach (var section in model.Sections)
        {
            if (!string.IsNullOrEmpty(section.Name))
            {
                totalRows++; // Section header
            }

            totalRows += sectionRows[section];
        }

        var hasTitle = !string.IsNullOrEmpty(model.Title);

        // `axisFormat` writes the axis's dates in d3-time-format specifiers, as Mermaid does, and the
        // `gantt.axisFormat` setting does so for a diagram that does not say.
        var axisFormat = model.AxisFormat ?? model.Config.GetString("gantt", "axisFormat") ?? defaultAxisFormat;
        var axisLabelFontSize = options.FontSize - 2;
        var ticks = AxisTicks(minDate, totalDays, axisFormat, axisLabelFontSize);

        var chartWidth = totalDays * dayWidth;
        // The title occupies its own band above the axis; include it so the bottom row is not clipped.
        var chartHeight = totalRows * rowHeight + axisHeight + (hasTitle ? titleHeight : 0);

        // A long label on the last tick can reach past the end of the chart; the drawing widens to hold it.
        var axisReach = ticks
            .Select(_ => _.Day * dayWidth + AxisLabelWidth(_.Label, axisLabelFontSize) / 2)
            .DefaultIfEmpty(0)
            .Max();
        var width = leftMargin + Math.Max(chartWidth, axisReach) + options.Padding * 2;
        var height = chartHeight + options.Padding * 2;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        var offsetX = options.Padding + leftMargin;
        var offsetY = options.Padding;

        // Draw title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                width / 2,
                offsetY + titleHeight / 2,
                model.Title,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize + 2,
                fontFamily: options.FontFamily);
            offsetY += titleHeight;
        }

        // Draw axis
        DrawAxis(builder, ticks, offsetX, offsetY, chartWidth, axisLabelFontSize, options);
        offsetY += axisHeight;

        // Draw grid lines
        DrawGridLines(builder, minDate, totalDays, offsetX, offsetY, chartWidth, totalRows * rowHeight);

        // Draw sections and tasks
        var currentRow = 0;
        foreach (var section in model.Sections)
        {
            // Section header
            if (!string.IsNullOrEmpty(section.Name))
            {
                var sectionY = offsetY + currentRow * rowHeight;
                builder.AddRect(
                    options.Padding,
                    sectionY,
                    leftMargin + chartWidth,
                    sectionHeaderHeight,
                    fill: sectionColor,
                    stroke: "none");
                builder.AddText(
                    options.Padding + 10,
                    sectionY + sectionHeaderHeight / 2,
                    section.Name,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily,
                    fontWeight: "bold");
                currentRow++;
            }

            // Tasks
            foreach (var task in section.Tasks)
            {
                DrawTask(builder, task, minDate, currentRow + taskRows[task], offsetX, offsetY, chartWidth, !compact, options);
            }

            currentRow += sectionRows[section];
        }

        return builder.Build();
    }

    /// <summary>
    /// Gives each task of a section its row within the section, and returns how many rows that takes: one
    /// per task, or in compact mode as Mermaid lays it out, taking the tasks by start (then as declared)
    /// and putting each on the first row whose last task has ended by then.
    /// </summary>
    static int AssignRows(List<GanttTask> sectionTasks, bool compact, Dictionary<GanttTask, int> rows)
    {
        if (!compact)
        {
            for (var index = 0; index < sectionTasks.Count; index++)
            {
                rows[sectionTasks[index]] = index;
            }

            return sectionTasks.Count;
        }

        var rowEnds = new List<DateTime>();
        var byStart = sectionTasks
            .Select((task, index) => (task, index))
            .OrderBy(_ => _.task.ComputedStart)
            .ThenBy(_ => _.index);
        foreach (var (task, _) in byStart)
        {
            var row = rowEnds.FindIndex(_ => task.ComputedStart >= _);
            if (row < 0)
            {
                row = rowEnds.Count;
                rowEnds.Add(task.ComputedEnd);
            }
            else
            {
                rowEnds[row] = task.ComputedEnd;
            }

            rows[task] = row;
        }

        return rowEnds.Count;
    }

    /// <summary>
    /// The days the axis labels, and what each says. Naiad's tick spacing is set for a month/day label; a
    /// format that writes longer labels has them spaced at least their own width apart, so they do not run
    /// into each other.
    /// </summary>
    static List<(int Day, string Label)> AxisTicks(DateTime startDate, int totalDays, string axisFormat, double fontSize)
    {
        var interval = totalDays > 30 ? 7 : totalDays > 14 ? 3 : 1;

        var widest = "";
        for (var day = 0; day < totalDays; day++)
        {
            var label = TimeFormat.Format(axisFormat, startDate.AddDays(day));
            if (label.Length > widest.Length)
            {
                widest = label;
            }
        }

        if (widest.Length > defaultAxisLabelLength)
        {
            var labelDays = (int) Math.Ceiling((AxisLabelWidth(widest, fontSize) + axisLabelGap) / dayWidth);
            interval = Math.Max(interval, labelDays);
        }

        var ticks = new List<(int Day, string Label)>();
        for (var day = 0; day < totalDays; day += interval)
        {
            ticks.Add((day, TimeFormat.Format(axisFormat, startDate.AddDays(day))));
        }

        return ticks;
    }

    static double AxisLabelWidth(string label, double fontSize) =>
        label.Length * fontSize * axisCharWidthFactor;

    static void DrawAxis(
        SvgBuilder builder,
        List<(int Day, string Label)> ticks,
        double offsetX,
        double offsetY,
        double chartWidth,
        double fontSize,
        RenderOptions options)
    {
        // Axis line
        builder.AddLine(
            offsetX,
            offsetY + axisHeight - 5,
            offsetX + chartWidth,
            offsetY + axisHeight - 5,
            stroke: "#333", strokeWidth: 1);

        foreach (var (day, label) in ticks)
        {
            var x = offsetX + day * dayWidth;

            // Tick mark
            builder.AddLine(
                x,
                offsetY + axisHeight - 10,
                x,
                offsetY + axisHeight - 5,
                stroke: "#333",
                strokeWidth: 1);

            builder.AddText(
                x,
                offsetY + axisHeight - 20,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: "#666");
        }
    }

    static void DrawGridLines(SvgBuilder builder, DateTime startDate, int totalDays, double offsetX, double offsetY,
        double chartWidth, double chartHeight)
    {
        // Vertical grid lines (weekly)
        for (var i = 0; i < totalDays; i++)
        {
            var date = startDate.AddDays(i);
            if (date.DayOfWeek != DayOfWeek.Monday && i != 0)
            {
                continue;
            }

            var x = offsetX + i * dayWidth;
            builder.AddLine(x, offsetY, x, offsetY + chartHeight, stroke: "#ddd", strokeWidth: 1);
        }

        // Horizontal grid lines
        var numRows = (int) (chartHeight / rowHeight);
        for (var i = 0; i <= numRows; i++)
        {
            var y = offsetY + i * rowHeight;
            builder.AddLine(
                offsetX,
                y,
                offsetX + chartWidth,
                y,
                stroke: "#eee",
                strokeWidth: 1);
        }
    }

    static void DrawTask(
        SvgBuilder builder,
        GanttTask task,
        DateTime startDate,
        int row,
        double offsetX,
        double offsetY,
        double chartWidth,
        bool nameOnLeft,
        RenderOptions options)
    {
        var y = offsetY + row * rowHeight;
        var startDays = (task.ComputedStart - startDate).Days;
        var durationDays = Math.Max(1, (task.ComputedEnd - task.ComputedStart).Days);

        var taskX = offsetX + startDays * dayWidth;
        var taskWidth = durationDays * dayWidth;

        // Task name on the left
        if (nameOnLeft)
        {
            builder.AddText(
                options.Padding + 10,
                y + rowHeight / 2,
                task.Name,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
        }

        // Determine task color
        var color = task.Status switch
        {
            GanttTaskStatus.Done => taskDoneColor,
            GanttTaskStatus.Active => taskActiveColor,
            _ => task.IsCritical ? taskCritColor : taskColor
        };

        if (task.IsMilestone)
        {
            // Draw milestone as diamond
            var cy = y + rowHeight / 2;
            var path = string.Create(
                CultureInfo.InvariantCulture,
                $"M {taskX:0.##} {cy - milestoneSize:0.##} L {taskX + milestoneSize:0.##} {cy:0.##} L {taskX:0.##} {cy + milestoneSize:0.##} L {taskX - milestoneSize:0.##} {cy:0.##} Z");
            builder.AddPath(path, fill: milestoneColor, stroke: "#333", strokeWidth: 1);

            DrawTaskLabel(builder, task.Name, taskX - milestoneSize, milestoneSize * 2, cy, offsetX, chartWidth, options);
        }
        else
        {
            // Draw task bar
            var barY = y + (rowHeight - taskBarHeight) / 2;
            builder.AddRect(
                taskX,
                barY,
                taskWidth,
                taskBarHeight,
                rx: 3,
                fill: color,
                stroke: "#333",
                strokeWidth: 1);

            DrawTaskLabel(builder, task.Name, taskX, taskWidth, barY + taskBarHeight / 2, offsetX, chartWidth, options);
        }
    }

    /// <summary>
    /// Labels a bar or milestone with the task's name, as Mermaid does: centred inside when it fits, and
    /// otherwise just past the shape - to its right, or to its left when the chart has no room on the right.
    /// The internal task id was shown here instead, which Mermaid never displays.
    /// </summary>
    static void DrawTaskLabel(
        SvgBuilder builder,
        string name,
        double shapeX,
        double shapeWidth,
        double centerY,
        double chartLeft,
        double chartWidth,
        RenderOptions options)
    {
        const double gap = 6;
        var fontSize = options.FontSize - 2;
        var textWidth = MeasureText(name, fontSize);

        if (textWidth + gap * 2 <= shapeWidth)
        {
            builder.AddText(
                shapeX + shapeWidth / 2,
                centerY,
                name,
                anchor: "middle",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: "#fff");
            return;
        }

        // Outside the shape the text sits on the chart background, so it needs the dark fill.
        if (shapeX + shapeWidth + gap + textWidth <= chartLeft + chartWidth)
        {
            builder.AddText(
                shapeX + shapeWidth + gap,
                centerY,
                name,
                anchor: "start",
                baseline: "middle",
                fontSize: fontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
            return;
        }

        builder.AddText(
            shapeX - gap,
            centerY,
            name,
            anchor: "end",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: "#333");
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;

    /// <summary>
    /// Works out when every task starts and ends. A task starts on its own date, or when the latest of the
    /// tasks it comes <c>after</c> ends, or - given neither - when the task before it ends. As in Mermaid,
    /// today stands in where there is nothing to go on: for the first task if it has no date, and for an
    /// <c>after</c> that names no known task.
    /// </summary>
    static List<GanttTask> ComputeTaskDates(GanttModel model)
    {
        var allTasks = model.Sections.SelectMany(_ => _.Tasks).ToList();
        // Task ids are not guaranteed unique (a user may reuse an id, or the parser may derive the
        // same id from repeated tokens). As in Mermaid, whose index of tasks each later task overwrites,
        // a reused id names the last task given it.
        var taskMap = new Dictionary<string, GanttTask>();
        foreach (var task in allTasks)
        {
            if (task.Id != null)
            {
                taskMap[task.Id] = task;
            }
        }

        var today = DateTime.Today;
        var placed = new HashSet<GanttTask>();

        // A task can come after one declared further down, so keep sweeping until a pass places nothing.
        var progress = true;
        while (progress && placed.Count < allTasks.Count)
        {
            progress = false;
            for (var i = 0; i < allTasks.Count; i++)
            {
                if (placed.Contains(allTasks[i]) ||
                    !TryGetStart(i, out var start))
                {
                    continue;
                }

                Place(allTasks[i], start);
                progress = true;
            }
        }

        // What is left waits on itself, directly or through other tasks. Each starts today unless the
        // tasks it waits on have been placed by now.
        for (var i = 0; i < allTasks.Count; i++)
        {
            if (placed.Contains(allTasks[i]))
            {
                continue;
            }

            if (!TryGetStart(i, out var start))
            {
                start = today;
            }

            Place(allTasks[i], start);
        }

        return allTasks;

        bool TryGetStart(int index, out DateTime start)
        {
            var task = allTasks[index];
            if (task.StartDate.HasValue)
            {
                start = task.StartDate.Value;
                return true;
            }

            if (!string.IsNullOrEmpty(task.AfterTaskId))
            {
                DateTime? latest = null;
                foreach (var id in task.AfterTaskId.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!taskMap.TryGetValue(id, out var dependsOn))
                    {
                        continue;
                    }

                    if (!placed.Contains(dependsOn))
                    {
                        start = default;
                        return false;
                    }

                    if (latest is null || dependsOn.ComputedEnd > latest)
                    {
                        latest = dependsOn.ComputedEnd;
                    }
                }

                start = latest ?? today;
                return true;
            }

            if (index == 0)
            {
                start = today;
                return true;
            }

            var previous = allTasks[index - 1];
            start = previous.ComputedEnd;
            return placed.Contains(previous);
        }

        void Place(GanttTask task, DateTime start)
        {
            task.ComputedStart = start;
            if (task.EndDate.HasValue)
            {
                task.ComputedEnd = task.EndDate.Value;
            }
            else if (task.Duration.HasValue || task.DurationMonths != 0)
            {
                // Tasks that follow one another can each be short enough and still run past the last date
                // there is.
                try
                {
                    task.ComputedEnd = start.AddMonths(task.DurationMonths).Add(task.Duration ?? TimeSpan.Zero);
                }
                catch (ArgumentOutOfRangeException)
                {
                    throw new MermaidException($"Failed to render gantt: task '{task.Name}' would end after the year 9999");
                }
            }
            else
            {
                task.ComputedEnd = start.AddDays(1);
            }

            placed.Add(task);
        }
    }
}

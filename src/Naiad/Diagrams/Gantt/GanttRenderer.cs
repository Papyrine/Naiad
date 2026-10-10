namespace Naiad.Diagrams.Gantt;

public class GanttRenderer : IDiagramRenderer<GanttModel>
{
    const double rowHeight = 30;
    const double taskBarHeight = 20;
    const double sectionHeaderHeight = 25;
    const double axisHeight = 40;
    const double titleHeight = 30;
    const double leftMargin = 150;
    const double dayWidth = 20;
    const double milestoneSize = 12;

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

        // Calculate dimensions
        var totalRows = 0;
        foreach (var section in model.Sections)
        {
            if (!string.IsNullOrEmpty(section.Name))
            {
                totalRows++; // Section header
            }

            totalRows += section.Tasks.Count;
        }

        var hasTitle = !string.IsNullOrEmpty(model.Title);

        var chartWidth = totalDays * dayWidth;
        // The title occupies its own band above the axis; include it so the bottom row is not clipped.
        var chartHeight = totalRows * rowHeight + axisHeight + (hasTitle ? titleHeight : 0);

        var width = leftMargin + chartWidth + options.Padding * 2;
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
        DrawAxis(builder, minDate, totalDays, offsetX, offsetY, chartWidth, options);
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
                DrawTask(builder, task, minDate, currentRow, offsetX, offsetY, chartWidth, options);
                currentRow++;
            }
        }

        return builder.Build();
    }

    static void DrawAxis(SvgBuilder builder, DateTime startDate, int totalDays, double offsetX, double offsetY,
        double chartWidth, RenderOptions options)
    {
        // Axis line
        builder.AddLine(
            offsetX,
            offsetY + axisHeight - 5,
            offsetX + chartWidth,
            offsetY + axisHeight - 5,
            stroke: "#333", strokeWidth: 1);

        // Date labels (show every few days based on scale)
        var interval = totalDays > 30 ? 7 : totalDays > 14 ? 3 : 1;
        for (var i = 0; i < totalDays; i += interval)
        {
            var x = offsetX + i * dayWidth;
            var date = startDate.AddDays(i);

            // Tick mark
            builder.AddLine(
                x,
                offsetY + axisHeight - 10,
                x,
                offsetY + axisHeight - 5,
                stroke: "#333",
                strokeWidth: 1);

            // Date label
            var label = date.ToString("MM/dd", CultureInfo.InvariantCulture);
            builder.AddText(
                x,
                offsetY + axisHeight - 20,
                label,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
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
        RenderOptions options)
    {
        var y = offsetY + row * rowHeight;
        var startDays = (task.ComputedStart - startDate).Days;
        var durationDays = Math.Max(1, (task.ComputedEnd - task.ComputedStart).Days);

        var taskX = offsetX + startDays * dayWidth;
        var taskWidth = durationDays * dayWidth;

        // Task name on the left
        builder.AddText(
            options.Padding + 10,
            y + rowHeight / 2,
            task.Name,
            anchor: "start",
            baseline: "middle",
            fontSize: options.FontSize,
            fontFamily: options.FontFamily);

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
        // same id from repeated tokens); group so duplicates resolve to the first task instead of throwing.
        var taskMap = allTasks.Where(_ => _.Id != null)
            .DistinctBy(_ => _.Id!)
            .ToDictionary(_ => _.Id!);

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
                task.ComputedEnd = start.AddMonths(task.DurationMonths).Add(task.Duration ?? TimeSpan.Zero);
            }
            else
            {
                task.ComputedEnd = start.AddDays(1);
            }

            placed.Add(task);
        }
    }
}

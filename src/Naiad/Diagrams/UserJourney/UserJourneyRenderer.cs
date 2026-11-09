namespace Naiad.Diagrams.UserJourney;

public class UserJourneyRenderer : IDiagramRenderer<UserJourneyModel>
{
    const double leftMargin = 150;
    const double taskWidth = 150;
    const double taskGap = 50;
    const double taskHeight = 55;
    const double cornerRadius = 5;
    const double sectionBarHeight = 38;
    const double sectionTaskGap = 8;
    const double titleHeight = 40;
    const double axisGap = 35;
    const double faceGap = 60;
    const double faceStep = 38;
    const double faceRadius = 15;
    const double rightMargin = 40;

    // Per-section pastel fills for the header bars.
    static string[] sectionFills =
    [
        "#DCE3F5",
        "#FBF6C6",
        "#FBE2EC",
        "#DDEFD2",
        "#FCE5CB",
        "#E7DCF4",
        "#D3EFEF"
    ];

    // Softer section-tinted borders for the white task cards.
    static string[] sectionBorders =
    [
        "#9AA7D6",
        "#CFC65E",
        "#DD9CBB",
        "#93C277",
        "#E0AE73",
        "#B097D2",
        "#79C7C7"
    ];

    static string[] actorColors =
    [
        "#66BB6A",
        "#42A5F5",
        "#FFCA28",
        "#EF5350",
        "#AB47BC",
        "#26C6DA",
        "#FFA726",
        "#26A69A"
    ];

    const string taskFill = "#FFFFFF";
    const string faceFill = "#F0EAD0";
    const string faceStroke = "#8C8455";
    const string faceFeature = "#3F3F3F";

    public SvgDocument Render(UserJourneyModel model, RenderOptions options)
    {
        var sections = model.Sections.Where(_ => _.Tasks.Count > 0).ToList();
        if (sections.Count == 0)
        {
            var emptyBuilder = new SvgBuilder();
            emptyBuilder.Size(200, 100);
            emptyBuilder.AddText(
                100,
                50,
                "Empty journey",
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily);
            return emptyBuilder.Build();
        }

        var tasks = sections.SelectMany(_ => _.Tasks).ToList();
        var taskCount = tasks.Count;

        var actors = tasks
            .SelectMany(_ => _.Actors)
            .Distinct()
            .ToList();

        var titleOffset = string.IsNullOrEmpty(model.Title) ? 0 : titleHeight;

        var sectionBarY = options.Padding + titleOffset;
        var taskY = sectionBarY + sectionBarHeight + sectionTaskGap;
        var taskBottom = taskY + taskHeight;
        var axisY = taskBottom + axisGap;

        var lastTaskRight = TaskX(taskCount - 1) + taskWidth;
        // The top of the face scale is a 5, or a higher score where one was given, so that every face
        // hangs below the axis while still ranking above the lower ones.
        var topScore = Math.Max(5, tasks.Max(_ => _.Score));
        var lowestFaceY = FaceCenterY(axisY, tasks.Min(_ => _.Score), topScore);

        var width = lastTaskRight + rightMargin;
        var height = lowestFaceY + faceRadius + options.Padding;

        var builder = new SvgBuilder();
        builder.Size(width, height);
        builder.AddArrowMarker("journey-arrow");

        // Title
        if (!string.IsNullOrEmpty(model.Title))
        {
            builder.AddText(
                leftMargin,
                options.Padding + titleHeight / 2,
                model.Title,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize + 6,
                fontFamily: options.FontFamily,
                fontWeight: "bold",
                fill: "#333");
        }

        // Actor legend (left margin)
        for (var actorIndex = 0; actorIndex < actors.Count; actorIndex++)
        {
            var legendY = sectionBarY + sectionBarHeight / 2 + actorIndex * 24;
            var actorColor = actorColors[actorIndex % actorColors.Length];
            builder.AddCircle(24, legendY, 7, fill: actorColor, stroke: "#333", strokeWidth: 1);
            builder.AddText(
                40,
                legendY,
                actors[actorIndex],
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize,
                fontFamily: options.FontFamily,
                fill: "#333");
        }

        // Sections: header bar spans its tasks, then the task boxes and drop-lines below.
        var globalIndex = 0;
        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            var fill = sectionFills[sectionIndex % sectionFills.Length];
            var border = sectionBorders[sectionIndex % sectionBorders.Length];

            var firstTaskX = TaskX(globalIndex);
            var sectionRight = TaskX(globalIndex + section.Tasks.Count - 1) + taskWidth;
            var sectionWidth = sectionRight - firstTaskX;

            builder.AddRect(
                firstTaskX,
                sectionBarY,
                sectionWidth,
                sectionBarHeight,
                rx: cornerRadius,
                fill: fill,
                stroke: "none");

            if (!string.IsNullOrEmpty(section.Name))
            {
                builder.AddText(
                    firstTaskX + sectionWidth / 2,
                    sectionBarY + sectionBarHeight / 2,
                    section.Name,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize,
                    fontFamily: options.FontFamily,
                    fontWeight: "bold",
                    fill: "#333");
            }

            foreach (var task in section.Tasks)
            {
                var taskX = TaskX(globalIndex);
                var centerX = taskX + taskWidth / 2;

                builder.AddRect(
                    taskX,
                    taskY,
                    taskWidth,
                    taskHeight,
                    rx: cornerRadius,
                    fill: taskFill,
                    stroke: border,
                    strokeWidth: 2);

                builder.AddText(
                    centerX,
                    taskY + taskHeight / 2,
                    task.Name,
                    anchor: "middle",
                    baseline: "middle",
                    fontSize: options.FontSize - 1,
                    fontFamily: options.FontFamily,
                    fill: "#333");

                // Actor dots along the top edge, from the top-left corner.
                for (var taskActorIndex = 0; taskActorIndex < task.Actors.Count; taskActorIndex++)
                {
                    var actorColor = actorColors[actors.IndexOf(task.Actors[taskActorIndex]) % actorColors.Length];
                    builder.AddCircle(
                        taskX + 14 + taskActorIndex * 15,
                        taskY,
                        6,
                        fill: actorColor,
                        stroke: "#333",
                        strokeWidth: 1);
                }

                // Dashed drop-line from the task down past the axis to its score face.
                var faceCenterY = FaceCenterY(axisY, task.Score, topScore);
                builder.AddLine(
                    centerX,
                    taskBottom,
                    centerX,
                    faceCenterY - faceRadius,
                    stroke: "#999",
                    strokeWidth: 1,
                    strokeDasharray: "3,3");

                globalIndex++;
            }
        }

        // Timeline axis, drawn over the drop-lines.
        builder.AddPath(
            string.Create(
                CultureInfo.InvariantCulture,
                $"M {leftMargin:0.##} {axisY:0.##} L {lastTaskRight + 25:0.##} {axisY:0.##}"),
            fill: "none",
            stroke: "#333",
            strokeWidth: 3,
            markerEnd: "url(#journey-arrow)");

        // Score faces, drawn last so they sit above the axis and drop-lines.
        for (var index = 0; index < tasks.Count; index++)
        {
            var centerX = TaskX(index) + taskWidth / 2;
            DrawFace(builder, centerX, FaceCenterY(axisY, tasks[index].Score, topScore), tasks[index].Score);
        }

        return builder.Build();
    }

    static double TaskX(int index) => leftMargin + index * (taskWidth + taskGap);

    static double FaceCenterY(double axisY, int score, int topScore) =>
        axisY + faceGap + (topScore - score) * faceStep;

    static void DrawFace(SvgBuilder builder, double cx, double cy, int score)
    {
        builder.AddCircle(cx, cy, faceRadius, fill: faceFill, stroke: faceStroke, strokeWidth: 1);

        builder.AddCircle(cx - 5, cy - 3, 1.6, fill: faceFeature);
        builder.AddCircle(cx + 5, cy - 3, 1.6, fill: faceFeature);

        // Mouth tracks the score: smile (>= 4), flat (3), frown (<= 2).
        var mouth = score switch
        {
            >= 4 => string.Create(
                CultureInfo.InvariantCulture,
                $"M {cx - 6:0.##} {cy + 2:0.##} Q {cx:0.##} {cy + 8:0.##} {cx + 6:0.##} {cy + 2:0.##}"),
            <= 2 => string.Create(
                CultureInfo.InvariantCulture,
                $"M {cx - 6:0.##} {cy + 6:0.##} Q {cx:0.##} {cy:0.##} {cx + 6:0.##} {cy + 6:0.##}"),
            _ => string.Create(
                CultureInfo.InvariantCulture,
                $"M {cx - 6:0.##} {cy + 4:0.##} L {cx + 6:0.##} {cy + 4:0.##}")
        };

        builder.AddPath(mouth, fill: "none", stroke: faceFeature, strokeWidth: 1.5);
    }
}

namespace Naiad.Diagrams.GitGraph;

public class GitGraphRenderer : IDiagramRenderer<GitGraphModel>
{
    const double commitRadius = 12;
    const double commitSpacingX = 60;
    const double commitSpacingY = 50;
    const double tagHeight = 20;
    const double tagPadding = 5;

    // Commit captions: a chip under the commit, rather than text laid over the circle where anything
    // longer than the diameter spills onto the page background and disappears.
    const double labelGap = 6;
    const double labelHeight = 16;
    const double labelPadding = 5;
    const double labelSpacing = 12;

    static string[] branchColors =
    [
        "#4CAF50", // green - main
        "#2196F3", // blue
        "#FF9800", // orange
        "#9C27B0", // purple
        "#F44336", // red
        "#00BCD4", // cyan
        "#795548", // brown
        "#607D8B"  // blue-grey
    ];

    public SvgDocument Render(GitGraphModel model, RenderOptions options)
    {
        // Compute the actual git graph from operations
        var computed = ComputeGraph(model);

        if (model.Direction is Direction.TopToBottom or Direction.BottomToTop)
        {
            return RenderVertical(computed, model.Direction == Direction.BottomToTop, options);
        }

        // Calculate dimensions
        var maxRow = computed.Commits.Count > 0 ? computed.Commits.Max(_ => _.Row) : 0;
        var maxColumn = computed.Branches.Count > 0 ? computed.Branches.Max(_ => _.Column) : 0;

        var labelFontSize = options.FontSize - 4;
        var maxLabelWidth = computed.Commits.Count > 0
            ? computed.Commits.Max(_ => LabelWidth(_.DisplayLabel, labelFontSize))
            : 0;

        // Captions sit under their commit, so the lane has to be at least as wide as the widest one for
        // neighbouring captions not to run together.
        var spacingX = Math.Max(commitSpacingX, maxLabelWidth + labelSpacing);

        var labelsWidth = Math.Max(80, computed.Branches.Count > 0
            ? computed.Branches.Max(_ => MeasureText(_.Name, options.FontSize - 2)) + 10
            : 0);

        // Reserve whatever the tags above and the captions below actually need, so neither is clipped.
        var hasTag = computed.Commits.Any(_ => !string.IsNullOrEmpty(_.Tag));
        var topPad = Math.Max(
            commitSpacingY / 2,
            hasTag ? commitRadius + tagHeight + 5 : commitRadius);
        var bottomPad = Math.Max(commitSpacingY / 2, commitRadius + labelGap + labelHeight);

        var offsetX = options.Padding + labelsWidth;
        var offsetY = options.Padding + topPad;

        var width = offsetX + maxRow * spacingX +
                    Math.Max(commitRadius, maxLabelWidth / 2) + options.Padding;
        var height = options.Padding * 2 + topPad + maxColumn * commitSpacingY + bottomPad;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        var placement = new Placement(false, (row, column) => (offsetX + row * spacingX, offsetY + column * commitSpacingY));

        // Draw branch labels
        foreach (var branch in computed.Branches)
        {
            var y = offsetY + branch.Column * commitSpacingY;
            var color = branch.Color ?? branchColors[branch.Column % branchColors.Length];

            builder.AddText(
                options.Padding + 5, y, branch.Name,
                anchor: "start",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: color,
                fontWeight: "bold");
        }

        // Draw branch lines
        foreach (var branch in computed.Branches)
        {
            if (branch.Commits.Count == 0)
            {
                continue;
            }

            var y = offsetY + branch.Column * commitSpacingY;
            var color = branch.Color ?? branchColors[branch.Column % branchColors.Length];

            var firstCommit = branch.Commits.MinBy(_ => _.Row)!;
            var lastCommit = branch.Commits.MaxBy(_ => _.Row)!;

            var startX = offsetX + firstCommit.Row * spacingX;
            var endX = offsetX + lastCommit.Row * spacingX;

            builder.AddLine(
                startX,
                y,
                endX,
                y,
                stroke: color,
                strokeWidth: 2);
        }

        // Draw connections between commits (parent-child relationships)
        foreach (var commit in computed.Commits)
        {
            foreach (var parentId in commit.Parents)
            {
                if (computed.CommitMap.TryGetValue(parentId, out var parent))
                {
                    DrawConnection(builder, parent, commit, computed, placement);
                }
            }
        }

        // Draw commits
        foreach (var commit in computed.Commits)
        {
            DrawCommit(builder, commit, computed, placement, options);
        }

        return builder.Build();
    }

    // Where a commit sits, given its row (its place in time) and its branch's column (its lane), and
    // whether time runs down or up the page rather than across it.
    sealed record Placement(bool Vertical, Func<int, int, (double x, double y)> Point);

    /// <summary>
    /// The <c>TB</c> and <c>BT</c> layouts: each branch is a vertical lane with its name at the end the
    /// history starts from, and commits step down the page (or up it, for <c>BT</c>). Captions stay
    /// under their commit and tags above it, so a row is tall enough for both and a lane wide enough for
    /// the widest of them.
    /// </summary>
    static SvgDocument RenderVertical(ComputedGitGraph computed, bool bottomToTop, RenderOptions options)
    {
        const double branchBand = 28;

        var maxRow = computed.Commits.Count > 0 ? computed.Commits.Max(_ => _.Row) : 0;
        var maxColumn = computed.Branches.Count > 0 ? computed.Branches.Max(_ => _.Column) : 0;

        var labelFontSize = options.FontSize - 4;
        var widest = 0.0;
        foreach (var commit in computed.Commits)
        {
            widest = Math.Max(widest, LabelWidth(commit.DisplayLabel, labelFontSize));
            if (!string.IsNullOrEmpty(commit.Tag))
            {
                widest = Math.Max(widest, MeasureText(commit.Tag, options.FontSize - 2) + tagPadding * 2);
            }
        }

        foreach (var branch in computed.Branches)
        {
            widest = Math.Max(widest, MeasureText(branch.Name, options.FontSize - 2) + 4);
        }

        var hasTag = computed.Commits.Any(_ => !string.IsNullOrEmpty(_.Tag));
        var above = hasTag ? commitRadius + tagHeight + 5 : commitRadius;
        var below = commitRadius + labelGap + labelHeight;

        var laneSpacing = Math.Max(commitSpacingX, widest + labelSpacing);
        var rowSpacing = above + below + labelSpacing;

        var offsetX = options.Padding + Math.Max(commitRadius, widest / 2);
        var firstRowY = options.Padding + above + (bottomToTop ? 0 : branchBand);
        var lastRowY = firstRowY + maxRow * rowSpacing;

        var width = offsetX * 2 + maxColumn * laneSpacing;
        var height = lastRowY + below + options.Padding + (bottomToTop ? branchBand : 0);

        var placement = new Placement(
            true,
            (row, column) =>
            {
                var step = bottomToTop ? maxRow - row : row;
                return (offsetX + column * laneSpacing, firstRowY + step * rowSpacing);
            });

        var builder = new SvgBuilder();
        builder.Size(width, height);

        // Branch names sit where the history starts: above the lanes, or below them when it runs upwards.
        var bandY = bottomToTop
            ? lastRowY + below + branchBand / 2
            : options.Padding + branchBand / 2;
        foreach (var branch in computed.Branches)
        {
            builder.AddText(
                offsetX + branch.Column * laneSpacing,
                bandY,
                branch.Name,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: branch.Color ?? branchColors[branch.Column % branchColors.Length],
                fontWeight: "bold");
        }

        // Draw branch lines
        foreach (var branch in computed.Branches)
        {
            if (branch.Commits.Count == 0)
            {
                continue;
            }

            var (x, startY) = placement.Point(branch.Commits.Min(_ => _.Row), branch.Column);
            var (_, endY) = placement.Point(branch.Commits.Max(_ => _.Row), branch.Column);

            builder.AddLine(
                x,
                startY,
                x,
                endY,
                stroke: branch.Color ?? branchColors[branch.Column % branchColors.Length],
                strokeWidth: 2);
        }

        foreach (var commit in computed.Commits)
        {
            foreach (var parentId in commit.Parents)
            {
                if (computed.CommitMap.TryGetValue(parentId, out var parent))
                {
                    DrawConnection(builder, parent, commit, computed, placement);
                }
            }
        }

        foreach (var commit in computed.Commits)
        {
            DrawCommit(builder, commit, computed, placement, options);
        }

        return builder.Build();
    }

    static void DrawConnection(
        SvgBuilder builder,
        GitCommit from,
        GitCommit to,
        ComputedGitGraph graph,
        Placement placement)
    {
        var fromBranch = graph.FindBranch(from.Branch);
        var toBranch = graph.FindBranch(to.Branch);

        if (fromBranch == null || toBranch == null)
        {
            return;
        }

        var (fromX, fromY) = placement.Point(from.Row, fromBranch.Column);
        var (toX, toY) = placement.Point(to.Row, toBranch.Column);

        var toColor = toBranch.Color ?? branchColors[toBranch.Column % branchColors.Length];

        if (from.Branch == to.Branch)
        {
            // Same branch - straight line (already drawn as branch line)
            return;
        }

        // Different branches - draw curved connection (merge or branch point)
        if (placement.Vertical)
        {
            // Leave and arrive along the lanes, crossing between them half-way.
            var midY = (fromY + toY) / 2;
            var verticalPath = string.Create(
                CultureInfo.InvariantCulture,
                $"""
                 M {fromX:0.##} {fromY:0.##}
                 C {fromX:0.##} {midY:0.##}, {toX:0.##} {midY:0.##}, {toX:0.##} {toY:0.##}
                 """);

            builder.AddPath(verticalPath, stroke: toColor, strokeWidth: 2, fill: "none");
            return;
        }

        // Use a simple path with control points
        var midX = (fromX + toX) / 2;

        var path = string.Create(
            CultureInfo.InvariantCulture,
            $"""
             M {fromX:0.##} {fromY:0.##}
             C {midX:0.##} {fromY:0.##}, {midX:0.##} {toY:0.##}, {toX:0.##} {toY:0.##}
             """);

        builder.AddPath(path, stroke: toColor, strokeWidth: 2, fill: "none");
    }

    static void DrawCommit(SvgBuilder builder, GitCommit commit, ComputedGitGraph graph,
        Placement placement, RenderOptions options)
    {
        var branch = graph.FindBranch(commit.Branch);
        if (branch == null)
        {
            return;
        }

        var (x, y) = placement.Point(commit.Row, branch.Column);
        var color = branch.Color ?? branchColors[branch.Column % branchColors.Length];

        DrawCommitGlyph(builder, commit, x, y, color);
        DrawCommitLabel(builder, commit.DisplayLabel, x, y, options);

        // Tag
        if (!string.IsNullOrEmpty(commit.Tag))
        {
            var tagWidth = MeasureText(commit.Tag, options.FontSize - 2) + tagPadding * 2;
            var tagX = x - tagWidth / 2;
            var tagY = y - commitRadius - tagHeight - 5;

            builder.AddRect(
                tagX,
                tagY,
                tagWidth,
                tagHeight,
                rx: 3,
                fill: "#FFF9C4",
                stroke: "#FBC02D",
                strokeWidth: 1);

            builder.AddText(
                x,
                tagY + tagHeight / 2,
                commit.Tag,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: "#333");
        }
    }

    /// <summary>
    /// Draws the commit itself. How it came about wins over its declared type, so a merge still reads as a
    /// merge whatever <c>type:</c> says, and the fill carries the type on top of that.
    /// </summary>
    static void DrawCommitGlyph(SvgBuilder builder, GitCommit commit, double x, double y, string color)
    {
        var fill = commit.Type switch
        {
            CommitType.Reverse => "#fff",
            CommitType.Highlight => "#FFD700",
            _ => color
        };

        if (commit.IsCherryPick)
        {
            // A pair of cherries: two dots on a white face.
            builder.AddCircle(x, y, commitRadius, fill: "#fff", stroke: color, strokeWidth: 2);
            builder.AddCircle(x - 4, y + 2, 3.5, fill: color, stroke: color, strokeWidth: 1);
            builder.AddCircle(x + 4, y + 2, 3.5, fill: color, stroke: color, strokeWidth: 1);
            return;
        }

        if (commit.IsMerge)
        {
            // Double circle, distinguishing a merge from an ordinary commit.
            builder.AddCircle(x, y, commitRadius, fill: fill, stroke: color, strokeWidth: 2);
            builder.AddCircle(x, y, commitRadius - 5, fill: "#fff", stroke: color, strokeWidth: 1.5);
            return;
        }

        if (commit.Type == CommitType.Reverse)
        {
            // Crossed circle.
            builder.AddCircle(x, y, commitRadius, fill: fill, stroke: color, strokeWidth: 3);

            var arm = commitRadius * 0.55;
            builder.AddLine(x - arm, y - arm, x + arm, y + arm, stroke: color, strokeWidth: 2.5);
            builder.AddLine(x - arm, y + arm, x + arm, y - arm, stroke: color, strokeWidth: 2.5);
            return;
        }

        if (commit.Type == CommitType.Highlight)
        {
            // Mermaid marks a highlighted commit with a block rather than a circle.
            builder.AddRect(
                x - commitRadius,
                y - commitRadius,
                commitRadius * 2,
                commitRadius * 2,
                rx: 3,
                fill: fill,
                stroke: color,
                strokeWidth: 3);
            return;
        }

        builder.AddCircle(x, y, commitRadius, fill: fill, stroke: color, strokeWidth: 2);
    }

    static void DrawCommitLabel(SvgBuilder builder, string label, double x, double y, RenderOptions options)
    {
        if (string.IsNullOrEmpty(label))
        {
            return;
        }

        var fontSize = options.FontSize - 4;
        var width = LabelWidth(label, fontSize);
        var top = y + commitRadius + labelGap;

        builder.AddRect(
            x - width / 2,
            top,
            width,
            labelHeight,
            rx: 3,
            fill: "#F4F4F4",
            stroke: "#CCC",
            strokeWidth: 1);

        builder.AddText(
            x,
            top + labelHeight / 2,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: "#333");
    }

    static double LabelWidth(string label, double fontSize) =>
        MeasureText(label, fontSize) + labelPadding * 2;

    static ComputedGitGraph ComputeGraph(GitGraphModel model)
    {
        var computed = new ComputedGitGraph();
        var branchMap = new Dictionary<string, GitBranch>();
        var currentBranch = model.MainBranchName;
        var commitCounter = 0;

        // Create main branch
        var mainBranch = new GitBranch
        {
            Name = model.MainBranchName,
            Order = model.MainBranchOrder,
            Column = 0
        };
        branchMap[model.MainBranchName] = mainBranch;
        computed.Branches.Add(mainBranch);

        string? lastCommitId = null;
        // branch -> latest commit id
        var branchHeads = new Dictionary<string, string>();

        foreach (var op in model.Operations)
        {
            switch (op)
            {
                case CommitOperation commit:
                    var commitId = commit.Id ?? $"commit{commitCounter}";
                    var gitCommit = new GitCommit
                    {
                        Id = commitId,
                        Message = commit.Message,
                        Tag = commit.Tag,
                        Type = commit.Type,
                        Branch = currentBranch,
                        Row = commitCounter
                    };

                    // Add parent (previous commit on this branch, or branch point)
                    if (branchHeads.TryGetValue(currentBranch, out var branchHead))
                    {
                        gitCommit.Parents.Add(branchHead);
                    }
                    else if (lastCommitId != null)
                    {
                        gitCommit.Parents.Add(lastCommitId);
                    }

                    computed.Commits.Add(gitCommit);
                    computed.CommitMap[commitId] = gitCommit;
                    branchHeads[currentBranch] = commitId;

                    if (branchMap.TryGetValue(currentBranch, out var commitBranch))
                    {
                        commitBranch.Commits.Add(gitCommit);
                    }

                    lastCommitId = commitId;
                    commitCounter++;
                    break;

                case BranchOperation branch:
                    if (!branchMap.ContainsKey(branch.Name))
                    {
                        var newBranch = new GitBranch
                        {
                            Name = branch.Name,
                            Order = branch.BranchOrder ?? computed.Branches.Count,
                            Column = computed.Branches.Count
                        };
                        branchMap[branch.Name] = newBranch;
                        computed.Branches.Add(newBranch);

                        // New branch starts from current branch's head
                        if (branchHeads.TryGetValue(currentBranch, out var parentCommit))
                        {
                            branchHeads[branch.Name] = parentCommit;
                        }
                    }
                    currentBranch = branch.Name;
                    break;

                case CheckoutOperation checkout:
                    currentBranch = checkout.BranchName;
                    break;

                case MergeOperation merge:
                    var mergeId = merge.Id ?? $"merge{commitCounter}";
                    var mergeCommit = new GitCommit
                    {
                        Id = mergeId,
                        Tag = merge.Tag,
                        Type = merge.Type,
                        Branch = currentBranch,
                        Row = commitCounter,
                        IsMerge = true
                    };

                    // Merge has two parents: current branch head and merged branch head
                    if (branchHeads.TryGetValue(currentBranch, out var currentHead))
                    {
                        mergeCommit.Parents.Add(currentHead);
                    }

                    if (branchHeads.TryGetValue(merge.BranchName, out var mergedHead))
                    {
                        mergeCommit.Parents.Add(mergedHead);
                    }

                    computed.Commits.Add(mergeCommit);
                    computed.CommitMap[mergeId] = mergeCommit;
                    branchHeads[currentBranch] = mergeId;

                    if (branchMap.TryGetValue(currentBranch, out var mergeBranch))
                    {
                        mergeBranch.Commits.Add(mergeCommit);
                    }

                    lastCommitId = mergeId;
                    commitCounter++;
                    break;

                case CherryPickOperation cherryPick:
                    if (computed.CommitMap.TryGetValue(cherryPick.CommitId, out var sourceCommit))
                    {
                        var cherryId = $"cherry{commitCounter}";
                        var cherryCommit = new GitCommit
                        {
                            Id = cherryId,
                            Message = sourceCommit.Message,
                            Tag = cherryPick.Tag,
                            Type = CommitType.Normal,
                            Branch = currentBranch,
                            Row = commitCounter,
                            IsCherryPick = true,
                            // Name the commit that was copied; the generated id says nothing.
                            Label = $"cherry-pick:{cherryPick.CommitId}"
                        };

                        if (branchHeads.TryGetValue(currentBranch, out var cherryHead))
                        {
                            cherryCommit.Parents.Add(cherryHead);
                        }

                        computed.Commits.Add(cherryCommit);
                        computed.CommitMap[cherryId] = cherryCommit;
                        branchHeads[currentBranch] = cherryId;

                        if (branchMap.TryGetValue(currentBranch, out var cherryBranch))
                        {
                            cherryBranch.Commits.Add(cherryCommit);
                        }

                        lastCommitId = cherryId;
                        commitCounter++;
                    }
                    break;
            }
        }

        return computed;
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;

}

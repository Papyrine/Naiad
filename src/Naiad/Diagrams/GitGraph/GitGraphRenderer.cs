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

    // With `rotateCommitLabel` (Mermaid's default) a caption runs off at 45 degrees, down and to the left,
    // from just below its commit (or, in a vertical graph, from beside it). As in Mermaid, lanes are then
    // 40px further apart to leave room for the captions between them.
    const double rotatedLaneGap = 40;
    const double rotatedDrop = 6;
    const double rotatedSideGap = 4;
    static readonly double diagonal = Math.Sqrt(0.5);

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

    /// <summary>
    /// The <c>gitGraph</c> options from the diagram's configuration, with Mermaid's defaults.
    /// </summary>
    sealed record Settings(
        bool ShowBranches,
        bool ShowCommitLabel,
        bool RotateCommitLabel,
        bool ParallelCommits,
        string[] Colors,
        Chip Caption,
        Chip Tag)
    {
        /// <summary>
        /// Reads the options, and the colours: Naiad's own, or with a configured theme (or theme variables)
        /// the theme's <c>git0</c> to <c>git7</c> for the branches and its commit and tag label colours.
        /// </summary>
        public static Settings From(MermaidConfig config)
        {
            var colors = branchColors;
            var caption = new Chip("#F4F4F4", "#CCC", "#333");
            var tag = new Chip("#FFF9C4", "#FBC02D", "#333");
            if (MermaidTheme.From(config, "gitGraph") is { } theme)
            {
                colors = Enumerable.Range(0, 8).Select(_ => theme[$"git{_}"] ?? branchColors[_]).ToArray();
                var commitBackground = theme["commitLabelBackground"] ?? caption.Fill;
                caption = new(commitBackground, commitBackground, theme["commitLabelColor"] ?? caption.Text);
                tag = new(
                    theme["tagLabelBackground"] ?? tag.Fill,
                    theme["tagLabelBorder"] ?? tag.Stroke,
                    theme["tagLabelColor"] ?? tag.Text);
            }

            return new(
                config.GetBool("gitGraph", "showBranches") ?? true,
                config.GetBool("gitGraph", "showCommitLabel") ?? true,
                config.GetBool("gitGraph", "rotateCommitLabel") ?? true,
                config.GetBool("gitGraph", "parallelCommits") ?? false,
                colors,
                caption,
                tag);
        }

        public string Color(GitBranch branch) =>
            branch.Color ?? Colors[branch.Column % Colors.Length];

        // Whether the commit is captioned at all.
        public bool HasCaption(GitCommit commit) =>
            ShowCommitLabel &&
            !string.IsNullOrEmpty(commit.DisplayLabel);
    }

    // A label's background, border and text colours.
    sealed record Chip(string Fill, string Stroke, string Text);

    public SvgDocument Render(GitGraphModel model, RenderOptions options)
    {
        var document = RenderGraph(model, options);
        DiagramTitle.Add(document, model.Title, options);
        return document;
    }

    static SvgDocument RenderGraph(GitGraphModel model, RenderOptions options)
    {
        var settings = Settings.From(model.Config);

        // Compute the actual git graph from operations
        var computed = ComputeGraph(model, settings.ParallelCommits);

        if (model.Direction is Direction.TopToBottom or Direction.BottomToTop)
        {
            return RenderVertical(computed, model.Direction == Direction.BottomToTop, options, settings);
        }

        // Calculate dimensions
        var maxRow = computed.Commits.Count > 0 ? computed.Commits.Max(_ => _.Row) : 0;
        var maxColumn = computed.Branches.Count > 0 ? computed.Branches.Max(_ => _.Column) : 0;

        var labelFontSize = options.FontSize - 4;
        var captioned = computed.Commits.Where(settings.HasCaption).ToList();

        // Upright captions sit under their commit, so a column has to be at least as wide as the widest one
        // for neighbouring captions not to run together. Rotated ones run off at an angle, side by side.
        var maxLabelWidth = 0.0;
        if (!settings.RotateCommitLabel &&
            captioned.Count > 0)
        {
            maxLabelWidth = captioned.Max(_ => LabelWidth(_.DisplayLabel, labelFontSize));
        }

        var spacingX = Math.Max(commitSpacingX, maxLabelWidth + labelSpacing);
        var laneSpacing = commitSpacingY;
        if (settings.RotateCommitLabel)
        {
            laneSpacing += rotatedLaneGap;
        }

        var nameWidth = 0.0;
        var labelsWidth = 0.0;
        if (settings.ShowBranches &&
            computed.Branches.Count > 0)
        {
            nameWidth = computed.Branches.Max(_ => MeasureText(_.Name, options.FontSize - 2));
            labelsWidth = Math.Max(80, nameWidth + 10);
        }

        // How far left of the first column, and how far below the last lane, the captions and tags reach.
        var reachLeft = commitRadius;
        var reachBelow = commitRadius;
        foreach (var commit in computed.Commits)
        {
            var x = commit.Row * spacingX;
            var y = (computed.FindBranch(commit.Branch)?.Column ?? 0) * laneSpacing - maxColumn * laneSpacing;
            if (!string.IsNullOrEmpty(commit.Tag))
            {
                reachLeft = Math.Max(reachLeft, TagWidth(commit.Tag, options) / 2 - x);
            }

            if (!settings.HasCaption(commit))
            {
                continue;
            }

            var labelWidth = LabelWidth(commit.DisplayLabel, labelFontSize);
            if (settings.RotateCommitLabel)
            {
                var reach = RotatedReach(labelWidth);
                reachLeft = Math.Max(reachLeft, reach - x);
                reachBelow = Math.Max(reachBelow, y + commitRadius + rotatedDrop + reach);
            }
            else
            {
                reachLeft = Math.Max(reachLeft, labelWidth / 2 - x);
                reachBelow = Math.Max(reachBelow, commitRadius + labelGap + labelHeight);
            }
        }

        // Reserve whatever the tags above and the captions below actually need, so neither is clipped.
        var hasTag = computed.Commits.Any(_ => !string.IsNullOrEmpty(_.Tag));
        var topPad = Math.Max(
            commitSpacingY / 2,
            hasTag ? commitRadius + tagHeight + 5 : commitRadius);
        var bottomPad = Math.Max(commitSpacingY / 2, reachBelow);

        // Branch names leave the space to their right free, which captions and tags may reach into. The
        // first column moves right only for what reaches further.
        var offsetX = options.Padding + reachLeft;
        if (settings.ShowBranches)
        {
            var free = labelsWidth - nameWidth - 10;
            offsetX = options.Padding + labelsWidth + Math.Max(0, reachLeft - free);
        }

        var offsetY = options.Padding + topPad;

        var reachRight = commitRadius;
        if (!settings.RotateCommitLabel)
        {
            reachRight = Math.Max(commitRadius, maxLabelWidth / 2);
        }

        var width = offsetX + maxRow * spacingX + reachRight + options.Padding;
        var height = options.Padding * 2 + topPad + maxColumn * laneSpacing + bottomPad;

        var builder = new SvgBuilder();
        builder.Size(width, height);

        var placement = new Placement(false, (row, column) => (offsetX + row * spacingX, offsetY + column * laneSpacing));

        if (settings.ShowBranches)
        {
            // Draw branch labels
            foreach (var branch in computed.Branches)
            {
                var y = offsetY + branch.Column * laneSpacing;

                builder.AddText(
                    options.Padding + 5, y, branch.Name,
                    anchor: "start",
                    baseline: "middle",
                    fontSize: options.FontSize - 2,
                    fontFamily: options.FontFamily,
                    fill: settings.Color(branch),
                    fontWeight: "bold");
            }

            // Draw branch lines
            foreach (var branch in computed.Branches)
            {
                if (branch.Commits.Count == 0)
                {
                    continue;
                }

                var y = offsetY + branch.Column * laneSpacing;

                var firstCommit = branch.Commits.MinBy(_ => _.Row)!;
                var lastCommit = branch.Commits.MaxBy(_ => _.Row)!;

                var startX = offsetX + firstCommit.Row * spacingX;
                var endX = offsetX + lastCommit.Row * spacingX;

                builder.AddLine(
                    startX,
                    y,
                    endX,
                    y,
                    stroke: settings.Color(branch),
                    strokeWidth: 2);
            }
        }

        // Draw connections between commits (parent-child relationships)
        foreach (var commit in computed.Commits)
        {
            foreach (var parentId in commit.Parents)
            {
                if (computed.CommitMap.TryGetValue(parentId, out var parent))
                {
                    DrawConnection(builder, parent, commit, computed, placement, settings);
                }
            }
        }

        // Draw commits
        foreach (var commit in computed.Commits)
        {
            DrawCommit(builder, commit, computed, placement, options, settings);
        }

        return builder.Build();
    }

    // Where a commit sits, given its row (its place in time) and its branch's column (its lane), and
    // whether time runs down or up the page rather than across it.
    sealed record Placement(bool Vertical, Func<int, int, (double x, double y)> Point);

    /// <summary>
    /// The <c>TB</c> and <c>BT</c> layouts: each branch is a vertical lane with its name at the end the
    /// history starts from, and commits step down the page (or up it, for <c>BT</c>). Upright captions stay
    /// under their commit and tags above it, so a row is tall enough for both and a lane wide enough for
    /// the widest of them. Rotated captions run down to the left from beside the commit, so a lane is wide
    /// enough for the longest of those instead.
    /// </summary>
    static SvgDocument RenderVertical(ComputedGitGraph computed, bool bottomToTop, RenderOptions options, Settings settings)
    {
        var branchBand = 0.0;
        if (settings.ShowBranches)
        {
            branchBand = 28;
        }

        var maxRow = computed.Commits.Count > 0 ? computed.Commits.Max(_ => _.Row) : 0;
        var maxColumn = computed.Branches.Count > 0 ? computed.Branches.Max(_ => _.Column) : 0;

        var labelFontSize = options.FontSize - 4;
        var widest = 0.0;
        var rotatedReach = 0.0;
        foreach (var commit in computed.Commits)
        {
            if (settings.HasCaption(commit))
            {
                var labelWidth = LabelWidth(commit.DisplayLabel, labelFontSize);
                if (settings.RotateCommitLabel)
                {
                    rotatedReach = Math.Max(rotatedReach, RotatedReach(labelWidth));
                }
                else
                {
                    widest = Math.Max(widest, labelWidth);
                }
            }

            if (!string.IsNullOrEmpty(commit.Tag))
            {
                widest = Math.Max(widest, TagWidth(commit.Tag, options));
            }
        }

        if (settings.ShowBranches)
        {
            foreach (var branch in computed.Branches)
            {
                widest = Math.Max(widest, MeasureText(branch.Name, options.FontSize - 2) + 4);
            }
        }

        // A rotated caption starts this far left of its commit's centre.
        var rotatedStart = 0.0;
        if (rotatedReach > 0)
        {
            rotatedStart = commitRadius + rotatedSideGap + rotatedReach;
        }

        var hasTag = computed.Commits.Any(_ => !string.IsNullOrEmpty(_.Tag));
        var above = hasTag ? commitRadius + tagHeight + 5 : commitRadius;
        var below = commitRadius;
        if (settings is
            {
                ShowCommitLabel: true,
                RotateCommitLabel: false
            })
        {
            below += labelGap + labelHeight;
        }

        var laneSpacing = Math.Max(
            commitSpacingX,
            Math.Max(widest, rotatedStart + commitRadius) + labelSpacing);
        var rowSpacing = above + below + labelSpacing;

        var offsetX = options.Padding + Math.Max(Math.Max(commitRadius, widest / 2), rotatedStart);
        var firstRowY = options.Padding + above + (bottomToTop ? 0 : branchBand);
        var lastRowY = firstRowY + maxRow * rowSpacing;

        var placement = new Placement(
            true,
            (row, column) =>
            {
                var step = bottomToTop ? maxRow - row : row;
                return (offsetX + column * laneSpacing, firstRowY + step * rowSpacing);
            });

        // How far down the commits and their captions reach.
        var bottom = lastRowY + below;
        if (settings.RotateCommitLabel)
        {
            foreach (var commit in computed.Commits)
            {
                if (settings.HasCaption(commit))
                {
                    var (_, y) = placement.Point(commit.Row, computed.FindBranch(commit.Branch)?.Column ?? 0);
                    bottom = Math.Max(
                        bottom,
                        y + rotatedSideGap + RotatedReach(LabelWidth(commit.DisplayLabel, labelFontSize)));
                }
            }
        }

        var width = offsetX + maxColumn * laneSpacing + Math.Max(commitRadius, widest / 2) + options.Padding;
        var height = bottom + options.Padding + (bottomToTop ? branchBand : 0);

        var builder = new SvgBuilder();
        builder.Size(width, height);

        if (settings.ShowBranches)
        {
            // Branch names sit where the history starts: above the lanes, or below them when it runs upwards.
            var bandY = bottomToTop
                ? bottom + branchBand / 2
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
                    fill: settings.Color(branch),
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
                    stroke: settings.Color(branch),
                    strokeWidth: 2);
            }
        }

        foreach (var commit in computed.Commits)
        {
            foreach (var parentId in commit.Parents)
            {
                if (computed.CommitMap.TryGetValue(parentId, out var parent))
                {
                    DrawConnection(builder, parent, commit, computed, placement, settings);
                }
            }
        }

        foreach (var commit in computed.Commits)
        {
            DrawCommit(builder, commit, computed, placement, options, settings);
        }

        return builder.Build();
    }

    static void DrawConnection(
        SvgBuilder builder,
        GitCommit from,
        GitCommit to,
        ComputedGitGraph graph,
        Placement placement,
        Settings settings)
    {
        var fromBranch = graph.FindBranch(from.Branch);
        var toBranch = graph.FindBranch(to.Branch);

        if (fromBranch == null || toBranch == null)
        {
            return;
        }

        var (fromX, fromY) = placement.Point(from.Row, fromBranch.Column);
        var (toX, toY) = placement.Point(to.Row, toBranch.Column);

        var toColor = settings.Color(toBranch);

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

    static void DrawCommit(
        SvgBuilder builder,
        GitCommit commit,
        ComputedGitGraph graph,
        Placement placement,
        RenderOptions options,
        Settings settings)
    {
        var branch = graph.FindBranch(commit.Branch);
        if (branch == null)
        {
            return;
        }

        var (x, y) = placement.Point(commit.Row, branch.Column);
        var color = settings.Color(branch);

        DrawCommitGlyph(builder, commit, x, y, color);
        if (settings.HasCaption(commit))
        {
            DrawCommitLabel(builder, commit.DisplayLabel, x, y, placement.Vertical, options, settings);
        }

        // Tag
        if (!string.IsNullOrEmpty(commit.Tag))
        {
            var tagWidth = TagWidth(commit.Tag, options);
            var tagX = x - tagWidth / 2;
            var tagY = y - commitRadius - tagHeight - 5;

            builder.AddRect(
                tagX,
                tagY,
                tagWidth,
                tagHeight,
                rx: 3,
                fill: settings.Tag.Fill,
                stroke: settings.Tag.Stroke,
                strokeWidth: 1);

            builder.AddText(
                x,
                tagY + tagHeight / 2,
                commit.Tag,
                anchor: "middle",
                baseline: "middle",
                fontSize: options.FontSize - 2,
                fontFamily: options.FontFamily,
                fill: settings.Tag.Text);
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

    static void DrawCommitLabel(
        SvgBuilder builder,
        string label,
        double x,
        double y,
        bool vertical,
        RenderOptions options,
        Settings settings)
    {
        var fontSize = options.FontSize - 4;
        var width = LabelWidth(label, fontSize);

        if (settings.RotateCommitLabel)
        {
            // The chip is drawn level with its right end at the origin, then turned about that end: just
            // below the commit, or just left of it in a vertical graph.
            var anchorX = x;
            var anchorY = y + commitRadius + rotatedDrop;
            if (vertical)
            {
                anchorX = x - commitRadius - rotatedSideGap;
                anchorY = y + rotatedSideGap;
            }

            builder.BeginGroup(
                cssClass: "commit-label",
                transform: string.Create(CultureInfo.InvariantCulture, $"translate({anchorX:0.##},{anchorY:0.##}) rotate(-45)"));
            DrawLabelChip(builder, label, -width / 2, -labelHeight / 2, width, fontSize, options, settings.Caption);
            builder.EndGroup();
            return;
        }

        DrawLabelChip(builder, label, x, y + commitRadius + labelGap, width, fontSize, options, settings.Caption);
    }

    // A caption chip of the given width, centred on centreX, with its top at top.
    static void DrawLabelChip(
        SvgBuilder builder,
        string label,
        double centreX,
        double top,
        double width,
        double fontSize,
        RenderOptions options,
        Chip chip)
    {
        builder.AddRect(
            centreX - width / 2,
            top,
            width,
            labelHeight,
            rx: 3,
            fill: chip.Fill,
            stroke: chip.Stroke,
            strokeWidth: 1);

        builder.AddText(
            centreX,
            top + labelHeight / 2,
            label,
            anchor: "middle",
            baseline: "middle",
            fontSize: fontSize,
            fontFamily: options.FontFamily,
            fill: chip.Text);
    }

    static double LabelWidth(string label, double fontSize) =>
        MeasureText(label, fontSize) + labelPadding * 2;

    static double TagWidth(string tag, RenderOptions options) =>
        MeasureText(tag, options.FontSize - 2) + tagPadding * 2;

    // How far a rotated caption of the given width reaches, across and down, from where it starts.
    static double RotatedReach(double width) =>
        (width + labelHeight / 2) * diagonal;

    static ComputedGitGraph ComputeGraph(GitGraphModel model, bool parallelCommits)
    {
        var computed = new ComputedGitGraph();
        var branchMap = new Dictionary<string, GitBranch>();
        var currentBranch = model.MainBranchName;
        var commitCounter = 0;

        // Create main branch
        var mainBranch = new GitBranch
        {
            Name = model.MainBranchName,
            Order = model.MainBranchOrder
        };
        branchMap[model.MainBranchName] = mainBranch;
        computed.Branches.Add(mainBranch);

        // The order each branch was given, if any. The main branch always has one.
        var givenOrders = new List<int?>
        {
            model.MainBranchOrder
        };

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
                            Order = branch.BranchOrder ?? computed.Branches.Count
                        };
                        branchMap[branch.Name] = newBranch;
                        computed.Branches.Add(newBranch);
                        givenOrders.Add(branch.BranchOrder);

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

                        // As in Mermaid, the copy is also linked to the commit it was taken from.
                        cherryCommit.Parents.Add(sourceCommit.Id);

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

        AssignLanes(computed.Branches, givenOrders);

        if (parallelCommits)
        {
            // `parallelCommits`: a commit sits one step after the latest of its parents, rather than after
            // the commit made before it, so commits as far from their parents line up.
            foreach (var commit in computed.Commits)
            {
                var row = 0;
                foreach (var parentId in commit.Parents)
                {
                    if (computed.CommitMap.TryGetValue(parentId, out var parent) &&
                        parent != commit)
                    {
                        row = Math.Max(row, parent.Row + 1);
                    }
                }

                commit.Row = row;
            }
        }

        return computed;
    }

    /// <summary>
    /// Puts the branches in Mermaid's order of lanes: by the order each was given (the main branch's being
    /// <c>mainBranchOrder</c>, 0 unless configured). One given no order sorts as if it were <c>0.i</c>, i
    /// being its place among the branches, so those keep the order they were made in, after the main branch
    /// and ahead of any given an order of 1 or more. Mermaid reads that place as decimals, so the tenth
    /// branch made (<c>0.10</c>) sorts with the first (<c>0.1</c>); that is kept, to lay out as it does.
    /// </summary>
    static void AssignLanes(List<GitBranch> branches, List<int?> givenOrders)
    {
        var keys = new double[branches.Count];
        for (var index = 0; index < branches.Count; index++)
        {
            keys[index] = givenOrders[index] ??
                          double.Parse(string.Create(CultureInfo.InvariantCulture, $"0.{index}"), CultureInfo.InvariantCulture);
        }

        var lanes = Enumerable.Range(0, branches.Count)
            .OrderBy(_ => keys[_])
            .ToList();
        for (var column = 0; column < lanes.Count; column++)
        {
            branches[lanes[column]].Column = column;
        }
    }

    static double MeasureText(string text, double fontSize) =>
        text.Length * fontSize * 0.55;
}

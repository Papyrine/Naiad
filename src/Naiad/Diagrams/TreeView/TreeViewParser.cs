namespace Naiad.Diagrams.TreeView;

class TreeViewParser : IDiagramParser<TreeViewModel>
{
    static Parser<TreeViewModel> parser;

    static TreeViewParser()
    {
        var inlineGap = Literals.AnyOf(" \t");
        var restOfLine = Literals.NoneOf("\r\n", minSize: 0);

        // The title and the accessibility title and description may only come before the first node, as in
        // Mermaid's grammar. The accessibility text is accepted and dropped, as other diagrams here do.
        var title =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("title"))
                .SkipAnd(inlineGap.SkipAnd(restOfLine).Optional())
                .AndSkip(CommonParsers.LineEnd)
                .Then<Line?>(static _ => new TitleLine(_.HasValue ? _.Value.Span.Trim().ToString() : ""));

        var accessibilityTitle =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accTitle"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(restOfLine)
                .AndSkip(CommonParsers.LineEnd)
                .Then<Line?>(static _ => null);

        var accessibilityDescription =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Text("accDescr"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(
                    Literals.Char(':').SkipAnd(restOfLine)
                        .Or(
                            // `accDescr { ... }` may run over several lines.
                            Literals.Char('{')
                                .SkipAnd(Literals.NoneOf("}", minSize: 0))
                                .AndSkip(Literals.Char('}'))
                                .AndSkip(CommonParsers.InlineWhitespace)))
                .AndSkip(CommonParsers.LineEnd)
                .Then<Line?>(static _ => null);

        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline))
                .Then<Line?>(static _ => null);

        // A bare name runs to the end of the line, or up to the first annotation: whitespace followed by
        // `:::` and a class name, `icon(` or `##`. Spaces inside it are kept, as Mermaid keeps them.
        var annotationStart =
            OneOf(
                inlineGap.And(Literals.Text(":::")).And(CommonParsers.InlineWhitespace)
                    .And(Literals.Pattern(static _ => char.IsAsciiLetter(_) || _ == '_', maxSize: 1))
                    .Then(static _ => 0),
                inlineGap.And(Literals.Text("icon(")).Then(static _ => 0),
                inlineGap.And(Literals.Text("##")).Then(static _ => 0));

        var bareName =
            Capture(
                    Not(OneOf(Literals.Text(":::"), Literals.Text("icon("), Literals.Text("##")))
                        .SkipAnd(Literals.NoneOf(" \t\r\n\"'", maxSize: 1))
                        .And(ZeroOrMany(Not(annotationStart).SkipAnd(Literals.NoneOf("\r\n", maxSize: 1)))))
                .Then(static _ => _.Span.TrimEnd(" \t").ToString());

        var name = CommonParsers.QuotedString.Or(bareName);

        var classAnnotation =
            inlineGap
                .SkipAnd(Literals.Text(":::"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ is '_' or '-'))
                .Assert(static _ => char.IsAsciiLetter(_.Span[0]) || _.Span[0] == '_', "Expected a class name")
                .Then<Action<TreeViewNode>>(static _ => node => node.CssClass = _.ToString());

        // An empty icon() hides the icon, as icon(none) does.
        var iconAnnotation =
            inlineGap
                .SkipAnd(Literals.Text("icon("))
                .SkipAnd(Literals.Pattern(static _ => char.IsAsciiLetterOrDigit(_) || _ is '_' or '-' or ':', minSize: 0))
                .AndSkip(Literals.Char(')'))
                .Then<Action<TreeViewNode>>(static _ => node => node.Icon = _.Length == 0 ? "none" : _.ToString());

        var descriptionAnnotation =
            inlineGap
                .SkipAnd(Literals.Text("##"))
                .SkipAnd(restOfLine)
                .Then<Action<TreeViewNode>>(static _ =>
                {
                    var text = _.Span.Trim().ToString();
                    return node => node.Description = text.Length == 0 ? null : text;
                });

        var nodeLine =
            CommonParsers.Indentation
                .And(name)
                .And(OneOf(classAnnotation, iconAnnotation, descriptionAnnotation).Many())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.Comment.Or(CommonParsers.LineEnd))
                .Then<Line?>(static _ => new NodeLine(_.Item1, CreateNode(_.Item2, _.Item3)));

        var headerLine = OneOf(title, accessibilityTitle, accessibilityDescription, skipLine);

        // A comment has to be tried before a node line, which would otherwise take it as a name.
        var bodyLine = skipLine.Or(nodeLine);

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("treeView-beta"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(headerLine.Many())
                .And(bodyLine.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.Item1.Concat(_.Item2)));
    }

    static TreeViewNode CreateNode(string name, IReadOnlyList<Action<TreeViewNode>> annotations)
    {
        var isDirectory = name.EndsWith('/');
        var node = new TreeViewNode
        {
            Name = isDirectory ? name[..^1] : name,
            IsDirectory = isDirectory
        };
        foreach (var annotation in annotations)
        {
            annotation(node);
        }

        return node;
    }

    /// <summary>
    /// Builds the tree from the indentation, as Mermaid does: a node goes under the nearest node before it
    /// that is indented less, so only the order of the indentation matters, not its amount.
    /// </summary>
    static TreeViewModel BuildModel(IEnumerable<Line?> lines)
    {
        var model = new TreeViewModel();
        var stack = new Stack<(int indent, List<TreeViewNode> children)>();
        stack.Push((-1, model.Nodes));

        foreach (var line in lines)
        {
            switch (line)
            {
                case TitleLine titleLine:
                    model.Title = titleLine.Text.Length == 0 ? null : titleLine.Text;
                    break;
                case NodeLine nodeLine:
                    while (nodeLine.Indent <= stack.Peek().indent)
                    {
                        stack.Pop();
                    }

                    stack.Peek().children.Add(nodeLine.Node);
                    stack.Push((nodeLine.Indent, nodeLine.Node.Children));
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<TreeViewModel> Parse(string input)
    {
        if (!BoxDrawing.TryConvert(input, out var converted, out var error))
        {
            return new(error);
        }

        return parser.ParseDiagram(converted);
    }

    abstract record Line;

    sealed record TitleLine(string Text) : Line;

    sealed record NodeLine(int Indent, TreeViewNode Node) : Line;

    /// <summary>
    /// Turns a tree drawn with box-drawing characters (<c>├──</c>, <c>└──</c>, <c>│</c>, and the heavy
    /// <c>┣━━</c>, <c>┗━━</c>, <c>┃</c>) into the indented form the grammar reads, as Mermaid's
    /// preprocessor does. The depth of a node comes from the column of its branch character. Lines that
    /// are only decoration become blank rather than being dropped, so parse errors still point at the
    /// line as written.
    /// </summary>
    static class BoxDrawing
    {
        static readonly SearchValues<char> allBoxCharacters = SearchValues.Create("─━│┃└┗├┣");
        static readonly SearchValues<char> branchCharacters = SearchValues.Create("└┗├┣");
        const string dashCharacters = "─━";
        const string verticalCharacters = "│┃";

        public static bool TryConvert(string input, out string output, [NotNullWhen(false)] out string? error)
        {
            output = input;
            error = null;
            var lines = input.Split('\n').Select(static _ => _.TrimEnd('\r')).ToArray();

            // The first line is the keyword, since the preamble has been stripped by now.
            var contentLines = lines
                .Skip(1)
                .Where(static _ => !IsPassThrough(_) && !IsDecorationOnly(_))
                .Select(static _ => _.Replace("\t", "    "))
                .ToList();
            if (!contentLines.Any(static _ => _.AsSpan().IndexOfAny(allBoxCharacters) >= 0))
            {
                return true;
            }

            var segmentWidth = InferSegmentWidth(contentLines);
            for (var i = 1; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsPassThrough(line))
                {
                    continue;
                }

                if (IsDecorationOnly(line))
                {
                    lines[i] = "";
                    continue;
                }

                var normalized = line.Replace("\t", "    ");
                var branchColumn = normalized.AsSpan().IndexOfAny(branchCharacters);
                if (branchColumn >= 0)
                {
                    var depth = (int) Math.Round((double) branchColumn / segmentWidth, MidpointRounding.AwayFromZero) + 1;
                    var position = branchColumn + 1;
                    while (position < normalized.Length &&
                           dashCharacters.Contains(normalized[position]))
                    {
                        position++;
                    }

                    while (position < normalized.Length &&
                           normalized[position] == ' ')
                    {
                        position++;
                    }

                    var content = normalized[position..].TrimEnd();
                    if (content.Length == 0)
                    {
                        error = $"Empty node: expected a name after the box-drawing prefix at line {i + 1}, col {position + 1}";
                        return false;
                    }

                    lines[i] = new string(' ', depth * 4) + content;
                }
                else if (normalized.All(static _ => char.IsWhiteSpace(_) || allBoxCharacters.Contains(_)))
                {
                    lines[i] = "";
                }
                else if (normalized.AsSpan().IndexOfAny(allBoxCharacters) < 0 &&
                         normalized[0] is ' ')
                {
                    // Box characters with no branch are taken as part of a top-level name, such as
                    // "Section ─ A.txt"; indentation with none at all means the two styles were mixed.
                    error = $"Unexpected indentation without box-drawing characters; use ├── or └── to indent a node at line {i + 1}, col 1";
                    return false;
                }
            }

            output = string.Join('\n', lines);
            return true;
        }

        // Blank lines, comments and the title and accessibility lines are left as they are.
        static bool IsPassThrough(string line)
        {
            var trimmed = line.AsSpan().TrimStart(" \t");
            if (trimmed.IsEmpty ||
                trimmed.StartsWith("%%"))
            {
                return true;
            }

            if (trimmed.StartsWith("title") &&
                trimmed.Length > 5 &&
                trimmed[5] is ' ' or '\t')
            {
                return true;
            }

            if (trimmed.StartsWith("accTitle"))
            {
                return trimmed["accTitle".Length..].TrimStart(" \t").StartsWith(":");
            }

            if (trimmed.StartsWith("accDescr"))
            {
                var rest = trimmed["accDescr".Length..].TrimStart(" \t");
                return rest.StartsWith(":") || rest.StartsWith("{");
            }

            return line.Trim().Length == 0;
        }

        static bool IsDecorationOnly(string line) =>
            line.Any(static _ => verticalCharacters.Contains(_)) &&
            line.All(static _ => char.IsWhiteSpace(_) || verticalCharacters.Contains(_));

        // The width of one level is the column of the first branch character that is not in the first
        // column, or four when every branch is.
        static int InferSegmentWidth(List<string> contentLines)
        {
            foreach (var line in contentLines)
            {
                var column = line.AsSpan().IndexOfAny(branchCharacters);
                if (column > 0)
                {
                    return column;
                }
            }

            return 4;
        }
    }
}

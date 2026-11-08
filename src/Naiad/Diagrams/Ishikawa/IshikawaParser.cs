class IshikawaParser : IDiagramParser<IshikawaModel>
{
    static Parser<IshikawaModel> parser;

    static IshikawaParser()
    {
        // A node is a whole line of text; its indentation places it in the tree.
        var nodeLine =
            CommonParsers.Indentation
                .And(Literals.NoneOf("\r\n"))
                .AndSkip(CommonParsers.LineEnd)
                .Then(static _ => (indent: _.Item1, text: _.Item2.Span.Trim().ToString()));

        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // A comment has to be tried before a node line, which would otherwise take it as text.
        var contentLine =
            skipLine.Then<(int indent, string text)?>(static _ => null)
                .Or(nodeLine.Then<(int indent, string text)?>(static _ => _));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("ishikawa"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentLine.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.Where(static _ => _.HasValue).Select(static _ => _!.Value).ToList()));
    }

    /// <summary>
    /// The first line is the effect, whatever its indentation. The causes are levelled from the indentation
    /// of the first cause, as Mermaid does, so an effect indented further than its causes still leaves them
    /// in their relative places. A line indented no further than the first cause is a top-level cause.
    /// </summary>
    static IshikawaModel BuildModel(List<(int indent, string text)> lines)
    {
        var model = new IshikawaModel();
        if (lines.Count == 0)
        {
            return model;
        }

        var root = new IshikawaNode
        {
            Text = lines[0].text
        };
        model.Root = root;

        if (lines.Count == 1)
        {
            return model;
        }

        var baseIndent = lines[1].indent;
        var stack = new Stack<(int level, IshikawaNode node)>();
        stack.Push((0, root));

        foreach (var (indent, text) in lines.Skip(1))
        {
            var level = Math.Max(1, indent - baseIndent + 1);
            while (stack.Count > 1 &&
                   stack.Peek().level >= level)
            {
                stack.Pop();
            }

            var node = new IshikawaNode
            {
                Text = text
            };
            stack.Peek().node.Children.Add(node);
            stack.Push((level, node));
        }

        return model;
    }

    public DiagramParseResult<IshikawaModel> Parse(string input) => parser.ParseDiagram(input);
}

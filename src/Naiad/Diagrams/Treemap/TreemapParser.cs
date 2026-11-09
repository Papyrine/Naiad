class TreemapParser : IDiagramParser<TreemapModel>
{
    // Node line
    internal record NodeLine(int Indent, string Name, double? Value, string? CssClass);

    static Parser<TreemapModel> parser;

    static TreemapParser()
    {
        // Quoted string: "text"
        var quotedString =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        // Number
        var number = CommonParsers.SignedDecimal;

        // CSS class: :::className
        var cssClass =
            Literals.Text(":::")
                .SkipAnd(Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString());

        // A node with no value is given 0. Either part may be left out, and blanks may end any line: a
        // value is a colon and a number, which `:::` is not, so a class straight after the name is still a
        // class.
        var value =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(number)
                .Optional()
                .Then(static _ => _.HasValue ? _.Value : 0d);

        var cssClassName =
            CommonParsers.InlineWhitespace
                .SkipAnd(cssClass)
                .Optional()
                .Then(static _ => _.OrNull());

        var nodeLineParser =
            CommonParsers.Indentation
                .And(quotedString)
                .And(value)
                .And(cssClassName)
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((indent, name, nodeValue, className) => new NodeLine(indent, name, nodeValue, className));

        // Skip line
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        // Content item
        var contentItem =
            nodeLineParser.Then<NodeLine?>(static _ => _)
                .Or(skipLine.Then<NodeLine?>(static _ => null));

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("treemap"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.OfType<NodeLine>().ToList()));
    }

    static TreemapModel BuildModel(List<NodeLine> lines)
    {
        var model = new TreemapModel();
        var stack = new Stack<(TreemapNode node, int indent)>();

        foreach (var line in lines)
        {
            var node = new TreemapNode
            {
                Name = line.Name,
                Value = line.Value,
                CssClass = line.CssClass
            };

            // Pop nodes from stack until we find parent
            while (stack.TryPeek(out var top) && top.indent >= line.Indent)
            {
                stack.Pop();
            }

            if (stack.TryPeek(out var parent))
            {
                // Child of current parent
                parent.node.Children.Add(node);
            }
            else
            {
                // Root level node
                model.RootNodes.Add(node);
            }

            // Push this node as potential parent
            stack.Push((node, line.Indent));
        }

        return model;
    }

    public DiagramParseResult<TreemapModel> Parse(string input) => parser.ParseDiagram(input);
}

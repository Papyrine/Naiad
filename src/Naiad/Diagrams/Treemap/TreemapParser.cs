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

        // Where a value or a class is left out, what comes next is neither a blank nor a colon: either
        // of those has to lead into the value or the class. So a name takes a class only after a value
        // (`"Name":::class` is not read), and blanks may end a line only after a class.
        var leftOut =
            Not(Literals.AnyOf(" \t:", minSize: 1, maxSize: 1));

        // A node with no value is given 0.
        var value =
            CommonParsers.InlineWhitespace
                .SkipAnd(Literals.Char(':'))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(number)
                .Or(leftOut.Then(0d));

        var cssClassName =
            CommonParsers.InlineWhitespace
                .SkipAnd(cssClass)
                .Then<string?>(static _ => _)
                .Or(leftOut.Then<string?>(static _ => null));

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

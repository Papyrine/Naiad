class KanbanParser : IDiagramParser<KanbanModel>
{
    static Parser<KanbanModel> parser;

    static KanbanParser()
    {
        var identifier =
            Literals.Pattern(static _ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AsString();

        // Label in brackets: [Label Text]
        var labelParser =
            Between(Literals.Char('['), Literals.NoneOf("]\r\n", minSize: 0), Literals.Char(']'))
                .AsTrimmedString();

        // A metadata value: quoted either way, or bare text up to the next comma or the closing brace.
        // A value that opens with a quote is a quoted one: left unclosed, it is not read as bare text
        // instead.
        var metadataValue =
            OneOf(
                Between(Literals.Char('\''), Literals.NoneOf("'\r\n", minSize: 0), Literals.Char('\'')).AsString(),
                Between(Literals.Char('"'), Literals.NoneOf("\"\r\n", minSize: 0), Literals.Char('"')).AsString(),
                Not(Literals.AnyOf("'\"", minSize: 1, maxSize: 1))
                    .SkipAnd(Literals.NoneOf(",}\r\n", minSize: 0))
                    .AsTrimmedString());

        var metadataEntry =
            ZeroOrMore(char.IsWhiteSpace)
                .SkipAnd(Literals.Pattern(char.IsLetter).AsString())
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(metadataValue)
                .AndSkip(ZeroOrMore(char.IsWhiteSpace));

        // Metadata after a task: @{ assigned: 'knsv', ticket: MC-2037, priority: 'High' }. The whitespace
        // inside the braces belongs to the entries, so with no entries the braces are empty: `@{ }` is not
        // read.
        var metadataParser =
            Literals.Text("@{")
                .SkipAnd(Separated(Literals.Char(','), metadataEntry).Else([]))
                .AndSkip(Literals.Char('}'));

        // An item is id[Label], [Label] on its own, or bare text. Bare text and a lone label stand for
        // both the id and the name.
        var bracketed =
            identifier.Optional()
                .And(labelParser)
                .Map((id, label) => (id: id.OrSome(label), label));

        var bare =
            Literals.NoneOf("@[]\r\n")
                .AsTrimmedString()
                .When(static (_, text) => text.Length > 0)
                .Then(static _ => (id: _, label: _));

        // Whether an item is a column or a task is decided by its indentation, once every item is known.
        var itemParser =
            CommonParsers.Indentation
                .And(bracketed.Or(bare))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(metadataParser.Else([]))
                .AndSkip(CommonParsers.InlineWhitespace)
                .AndSkip(CommonParsers.LineEnd)
                .Map((indent, named, metadata) => new Item(indent, named.id, named.label, metadata));

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                skipLine.Then<Item?>(static _ => null),
                itemParser.Then<Item?>(static _ => _)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(TextIgnoreCase("kanban"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(BuildModel);
    }

    static KanbanModel BuildModel(IEnumerable<Item?> content)
    {
        var model = new KanbanModel();
        KanbanColumn? currentColumn = null;

        // The first item is a column and sets the indentation every other column shares. Anything
        // indented further belongs to the column above it.
        int? columnIndent = null;

        foreach (var item in content)
        {
            if (item is null)
            {
                continue;
            }

            columnIndent ??= item.Indent;

            if (item.Indent <= columnIndent)
            {
                currentColumn = new()
                {
                    Id = item.Id,
                    Name = item.Label
                };
                model.Columns.Add(currentColumn);
                continue;
            }

            var task = new KanbanTask
            {
                Id = item.Id,
                Name = item.Label
            };

            foreach (var (key, value) in item.Metadata)
            {
                if (value.Length == 0)
                {
                    continue;
                }

                switch (key.ToLowerInvariant())
                {
                    case "assigned":
                        task.Assigned = value;
                        break;
                    case "ticket":
                        task.Ticket = value;
                        break;
                    case "priority":
                        task.Priority = value;
                        break;
                }
            }

            currentColumn!.Tasks.Add(task);
        }

        return model;
    }

    public DiagramParseResult<KanbanModel> Parse(string input) => parser.ParseDiagram(input);

    sealed record Item(int Indent, string Id, string Label, IReadOnlyList<(string key, string value)> Metadata);
}

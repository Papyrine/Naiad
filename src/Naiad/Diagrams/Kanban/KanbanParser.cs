class KanbanParser : IDiagramParser<KanbanModel>
{
    static Parser<char, KanbanModel> parser;

    static KanbanParser()
    {
        var identifier =
            Token(_ => char.IsLetterOrDigit(_) || _ == '_' || _ == '-').AtLeastOnceString();

        // Label in brackets: [Label Text]
        var labelParser =
            from _ in Char('[')
            from label in Token(_ => _ != ']' && _ != '\r' && _ != '\n').ManyString()
            from __ in Char(']')
            select label.Trim();

        // A metadata value: quoted either way, or bare text up to the next comma or the closing brace.
        var metadataValue =
            OneOf(
                Char('\'').Then(Token(_ => _ != '\'' && _ != '\r' && _ != '\n').ManyString()).Before(Char('\'')),
                Char('"').Then(Token(_ => _ != '"' && _ != '\r' && _ != '\n').ManyString()).Before(Char('"')),
                Token(_ => _ is not (',' or '}' or '\r' or '\n')).ManyString().Select(_ => _.Trim()));

        var metadataEntry =
            from _ in SkipWhitespaces
            from key in Token(char.IsLetter).AtLeastOnceString()
            from __ in CommonParsers.InlineWhitespace
            from colon in Char(':')
            from ___ in CommonParsers.InlineWhitespace
            from value in metadataValue
            from ____ in SkipWhitespaces
            select (key, value);

        // Metadata after a task: @{ assigned: 'knsv', ticket: MC-2037, priority: 'High' }
        var metadataParser =
            String("@{")
                .Then(metadataEntry.Separated(Char(',')))
                .Before(SkipWhitespaces)
                .Before(Char('}'))
                .Select(_ => _.ToList());

        // An item is id[Label], [Label] on its own, or bare text. Bare text and a lone label stand for
        // both the id and the name.
        var bracketed =
            from id in identifier.Optional()
            from label in labelParser
            select (id: id.GetValueOrDefault() ?? label, label);

        var bare =
            Token(_ => _ is not ('@' or '[' or ']' or '\r' or '\n'))
                .AtLeastOnceString()
                .Select(_ => _.Trim())
                .Assert(_ => _.Length > 0)
                .Select(_ => (id: _, label: _));

        // Whether an item is a column or a task is decided by its indentation, once every item is known.
        var itemParser =
            from indent in CommonParsers.Indentation
            from named in Try(bracketed).Or(bare)
            from _ in CommonParsers.InlineWhitespace
            from metadata in metadataParser.Optional()
            from __ in CommonParsers.InlineWhitespace
            from ___ in CommonParsers.LineEnd
            select new Item(indent, named.id, named.label, metadata.GetValueOrDefault() ?? []);

        // Skip line (comments, empty lines)
        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        var contentItem =
            OneOf(
                Try(skipLine.ThenReturn<Item?>(null)),
                itemParser.Select<Item?>(_ => _)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CIString("kanban")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1);
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

    public Result<char, KanbanModel> Parse(string input) => parser.Parse(input);

    sealed record Item(int Indent, string Id, string Label, List<(string key, string value)> Metadata);
}

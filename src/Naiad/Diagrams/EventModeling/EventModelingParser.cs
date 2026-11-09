namespace Naiad.Diagrams.EventModeling;

/// <summary>
/// Reads Mermaid's event modeling DSL. Mermaid's Langium grammar treats every line break as whitespace; this
/// reads it a statement per line, except where a statement is made to span lines: a data block's braces, and
/// the statements of a <c>gwt</c> (given / when / then) specification.
/// </summary>
class EventModelingParser : IDiagramParser<EventModelingModel>
{
    static Parser<EventModelingModel> parser;

    static EventModelingParser()
    {
        var inlineSpace = Literals.AnyOf(" \t");
        var optionalSpace = CommonParsers.InlineWhitespace;

        // Whitespace across lines, for the parts of the grammar that may be spread over several.
        var anySpace = Literals.AnyOf(" \t\r\n", minSize: 0);

        var lineEnd = optionalSpace.SkipAnd(CommonParsers.LineEnd);

        // EM_ID: /[_a-zA-Z][\w_]*/
        var identifier =
            Capture(
                    Literals.Pattern(static _ => char.IsAsciiLetter(_) || _ == '_', maxSize: 1)
                        .And(ZeroOrMore(static _ => char.IsAsciiLetterOrDigit(_) || _ == '_')))
                .AsString();

        var qualifiedName =
            Capture(identifier.And(ZeroOrMany(Literals.Char('.').And(identifier))))
                .AsString();

        // EM_FID: /\d{1,3}/. A frame's number is compared as text, so 01 and 1 are different frames.
        var frameId =
            Literals.Pattern(char.IsAsciiDigit, maxSize: 3)
                .AsString();

        // The longer spelling of each kind is tried first; none of them is a prefix of another kind's spelling,
        // but each has to end at a word boundary, which the whitespace after it sees to.
        var entityKind =
            OneOf(
                TextIgnoreCase("readmodel").Or(TextIgnoreCase("rmo")).Then(EventModelingEntityKind.ReadModel),
                TextIgnoreCase("ui").Then(EventModelingEntityKind.Ui),
                TextIgnoreCase("command").Or(TextIgnoreCase("cmd")).Then(EventModelingEntityKind.Command),
                TextIgnoreCase("event").Or(TextIgnoreCase("evt")).Then(EventModelingEntityKind.Event),
                TextIgnoreCase("processor").Or(TextIgnoreCase("pcr")).Then(EventModelingEntityKind.Processor));

        var dataType =
            Between(
                Literals.Char('`'),
                OneOf(
                        TextIgnoreCase("json"),
                        TextIgnoreCase("jsobj"),
                        TextIgnoreCase("figma"),
                        TextIgnoreCase("salt"),
                        TextIgnoreCase("uri"),
                        TextIgnoreCase("md"),
                        TextIgnoreCase("html"),
                        TextIgnoreCase("text"))
                    .Then(static _ => _.ToLowerInvariant()),
                Literals.Char('`'));

        // EM_DATA_INLINE: /\{(.*)\}|"(.*)"|'(.*)'/, so the data runs from its opening brace (or quote) to the
        // last closing one on the line.
        var inlineData =
            Literals.NoneOf("\r\n")
                .AsTrimmedString()
                .Assert(
                    static _ => _.Length >= 2 &&
                                ((_[0] == '{' && _[^1] == '}') ||
                                 (_[0] == '"' && _[^1] == '"') ||
                                 (_[0] == '\'' && _[^1] == '\'')),
                    "Inline data has to be wrapped in braces or quotes")
                .Then(static _ => _[1..^1].Trim());

        var frameKeyword =
            OneOf(
                    TextIgnoreCase("timeframe").Then(false),
                    TextIgnoreCase("tf").Then(false),
                    TextIgnoreCase("resetframe").Then(true),
                    TextIgnoreCase("rf").Then(true))
                .Then(static (context, isReset) => (isReset, line: context.Scanner.Cursor.Position.Line));

        var sourceFrame =
            optionalSpace
                .SkipAnd(Literals.Text("->>"))
                .SkipAnd(optionalSpace)
                .SkipAnd(frameId);

        var dataReference =
            optionalSpace
                .SkipAnd(Literals.Text("[["))
                .SkipAnd(identifier)
                .AndSkip(Literals.Text("]]"));

        var frameData =
            optionalSpace
                .SkipAnd(dataType.Optional())
                .AndSkip(optionalSpace)
                .And(inlineData);

        var frame =
            frameKeyword
                .AndSkip(inlineSpace)
                .And(frameId)
                .AndSkip(inlineSpace)
                .And(entityKind)
                .AndSkip(inlineSpace)
                .And(qualifiedName)
                .And(sourceFrame.Many())
                .And(dataReference.Optional())
                .And(frameData.Optional())
                .AndSkip(lineEnd)
                .Then(
                    static object (_) =>
                    {
                        var ((isReset, line), id, kind, entityIdentifier, sources, reference, data) = _;
                        var frameValue = new EventModelingFrame
                        {
                            Id = id,
                            IsReset = isReset,
                            Kind = kind,
                            EntityIdentifier = entityIdentifier,
                            DataReference = reference.OrNull(),
                            DataType = data.HasValue ? data.Value.Item1.OrNull() : null,
                            InlineData = data.HasValue ? data.Value.Item2 : null,
                            Line = line
                        };
                        frameValue.SourceFrameIds.AddRange(sources);
                        return frameValue;
                    });

        // A data block's opening brace may be on the line after its name.
        var blockData =
            optionalSpace
                .SkipAnd(dataType.Optional())
                .AndSkip(anySpace)
                .And(new BlockBody())
                .AndSkip(lineEnd);

        var dataBlock =
            TextIgnoreCase("data")
                .SkipAnd(inlineSpace)
                .SkipAnd(identifier)
                .And(blockData)
                .Then(
                    static object (_) => new EventModelingDataBlock
                    {
                        Name = _.Item1,
                        DataType = _.Item2.Item1.OrNull(),
                        Content = _.Item2.Item2
                    });

        var note =
            TextIgnoreCase("note")
                .SkipAnd(inlineSpace)
                .SkipAnd(frameId)
                .And(blockData)
                .Then(
                    static object (_) => new EventModelingNote
                    {
                        FrameId = _.Item1,
                        DataType = _.Item2.Item1.OrNull(),
                        Content = _.Item2.Item2
                    });

        // `entity` declares a name for a gwt specification to refer to. Neither is drawn, by Mermaid or here.
        var entity =
            TextIgnoreCase("entity")
                .SkipAnd(inlineSpace)
                .SkipAnd(qualifiedName)
                .AndSkip(lineEnd)
                .Then(static object? (_) => null);

        var gwtStatement =
            anySpace
                .SkipAnd(entityKind)
                .AndSkip(inlineSpace)
                .And(identifier);

        var gwtStatements =
            gwtStatement.And(gwtStatement.Many());

        var gwt =
            TextIgnoreCase("gwt")
                .SkipAnd(inlineSpace)
                .SkipAnd(frameId)
                .SkipAnd(anySpace)
                .SkipAnd(TextIgnoreCase("given"))
                .SkipAnd(gwtStatements)
                .SkipAnd(anySpace.SkipAnd(TextIgnoreCase("when")).SkipAnd(gwtStatements).Optional())
                .SkipAnd(anySpace)
                .SkipAnd(TextIgnoreCase("then"))
                .SkipAnd(gwtStatements)
                .AndSkip(lineEnd)
                .Then(static object? (_) => null);

        var title =
            TextIgnoreCase("title")
                .SkipAnd(
                    inlineSpace.SkipAnd(Literals.NoneOf("\r\n", minSize: 0)).AsTrimmedString()
                        .Or(Literals.Text("").Then(static _ => "")))
                .AndSkip(CommonParsers.LineEnd)
                .Then(static object (_) => new TitleStatement(_));

        // Accessibility titles and descriptions are accepted and left out: an SVG of this diagram has nowhere
        // they are shown.
        var accessibility =
            OneOf(
                    TextIgnoreCase("accTitle").SkipAnd(optionalSpace).SkipAnd(Literals.Char(':')).SkipAnd(Literals.NoneOf("\r\n", minSize: 0)),
                    TextIgnoreCase("accDescr").SkipAnd(optionalSpace).SkipAnd(Literals.Char(':')).SkipAnd(Literals.NoneOf("\r\n", minSize: 0)),
                    TextIgnoreCase("accDescr").SkipAnd(anySpace).SkipAnd(Literals.Char('{')).SkipAnd(Literals.NoneOf("}", minSize: 0)).AndSkip(Literals.Char('}')))
                .AndSkip(lineEnd)
                .Then(static object? (_) => null);

        // Mermaid's grammar also takes C-style comments, besides its own %% ones.
        var lineComment =
            Literals.Text("//")
                .SkipAnd(Literals.NoneOf("\r\n", minSize: 0))
                .AndSkip(CommonParsers.LineEnd);

        var blockComment =
            Literals.Text("/*")
                .SkipAnd(AnyCharBefore(Literals.Text("*/"), canBeEmpty: true))
                .AndSkip(Literals.Text("*/"))
                .AndSkip(lineEnd);

        var skipLine =
            OneOf(CommonParsers.Comment, lineComment, blockComment, CommonParsers.Newline)
                .Then(static object? (_) => null);

        var statement =
            optionalSpace.SkipAnd(
                OneOf(
                    skipLine,
                    frame.Then(static object? (_) => _),
                    dataBlock.Then(static object? (_) => _),
                    note.Then(static object? (_) => _),
                    entity,
                    gwt,
                    title.Then(static object? (_) => _),
                    accessibility));

        parser =
            optionalSpace
                .SkipAnd(TextIgnoreCase("eventmodeling"))
                .SkipAnd(lineEnd)
                .SkipAnd(statement.Many())
                .AndSkip(optionalSpace)
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_));
    }

    static EventModelingModel BuildModel(IReadOnlyList<object?> statements)
    {
        var model = new EventModelingModel();
        var frameIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case EventModelingFrame frame:
                    // Mermaid refuses a reused number, since `->>` could not tell the frames apart.
                    if (!frameIds.Add(frame.Id))
                    {
                        throw new MermaidParseException(
                            $"Failed to parse event modeling diagram: Duplicate event modeling frame ID \"{frame.Id}\" on line {frame.Line}");
                    }

                    model.Frames.Add(frame);
                    break;
                case EventModelingDataBlock dataBlock:
                    model.DataBlocks.Add(dataBlock);
                    break;
                case EventModelingNote note:
                    model.Notes.Add(note);
                    break;
                case TitleStatement title:
                    model.Title = title.Text;
                    break;
            }
        }

        return model;
    }

    public DiagramParseResult<EventModelingModel> Parse(string input) => parser.ParseDiagram(input);

    sealed record TitleStatement(string Text);

    /// <summary>
    /// A data block: an opening brace with nothing after it on its line, then lines up to a closing brace on
    /// a line of its own. Mermaid only ends the block at a brace in the first column, so that the braces of
    /// nested JSON inside it do not end it. Here the closing brace may be indented as far as the line that
    /// opened the block, so the same holds in a diagram that is indented as a whole. The content is returned
    /// with the indentation its lines share taken off.
    /// </summary>
    sealed class BlockBody : Parser<string>
    {
        public override bool Parse(ParseContext context, ref ParseResult<string> result)
        {
            var cursor = context.Scanner.Cursor;
            var start = cursor.Position;
            var buffer = cursor.Buffer;
            var offset = start.Offset;
            if (offset >= buffer.Length ||
                buffer[offset] != '{')
            {
                return false;
            }

            var openIndent = Indentation(buffer, LineStart(buffer, offset));

            var position = offset + 1;
            while (position < buffer.Length && buffer[position] is ' ' or '\t' or '\r')
            {
                position++;
            }

            if (position >= buffer.Length ||
                buffer[position] != '\n')
            {
                return false;
            }

            position++;
            var lines = new List<string>();
            while (position < buffer.Length)
            {
                var lineEnd = buffer.IndexOf('\n', position);
                if (lineEnd < 0)
                {
                    lineEnd = buffer.Length;
                }

                var line = buffer[position..lineEnd].TrimEnd('\r');
                if (line.Trim() == "}" &&
                    Indentation(buffer, position) <= openIndent)
                {
                    var closeOffset = buffer.IndexOf('}', position) + 1;
                    cursor.Advance(closeOffset - offset);
                    result.Set(offset, closeOffset, Dedent(lines));
                    return true;
                }

                lines.Add(line);
                position = lineEnd + 1;
            }

            DiagramParseContext.Failed(context, offset, "A data block has to be closed with '}' on a line of its own");
            return false;
        }

        static int LineStart(string buffer, int offset)
        {
            var newline = offset == 0 ? -1 : buffer.LastIndexOf('\n', offset - 1);
            return newline + 1;
        }

        static int Indentation(string buffer, int lineStart)
        {
            var count = 0;
            while (lineStart + count < buffer.Length && buffer[lineStart + count] is ' ' or '\t')
            {
                count++;
            }

            return count;
        }

        static string Dedent(List<string> lines)
        {
            var common = lines
                .Where(static _ => _.Trim().Length > 0)
                .Select(static _ => _.Length - _.TrimStart(' ', '\t').Length)
                .DefaultIfEmpty(0)
                .Min();
            return string.Join('\n', lines.Select(_ => _.Length >= common ? _[common..] : _.TrimStart()));
        }
    }
}

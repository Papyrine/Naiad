class PacketParser : IDiagramParser<PacketModel>
{
    static Parser<PacketModel> parser;

    static PacketParser()
    {
        var quotedLabel =
            Between(Literals.Char('"'), Literals.NoneOf("\"", minSize: 0), Literals.Char('"'))
                .AsString();

        // Unquoted label (rest of line)
        var unquotedLabel =
            Literals.NoneOf("\r\n").AsTrimmedString();

        // Label (quoted or unquoted). A label that opens with a quote is a quoted one: left unclosed, it
        // is not read as an unquoted label instead.
        var labelParser =
            quotedLabel.Or(Not(Literals.Char('"')).SkipAnd(unquotedLabel));

        // "+bits": a width relative to where the previous field ended.
        var relativeSpec =
            Literals.Char('+')
                .SkipAnd(CommonParsers.UnsignedInt)
                .Then(static _ => new RawField(null, null, _, ""));

        // "start" (single bit) or "start-end" (explicit range).
        var explicitSpec =
            CommonParsers.UnsignedInt
                .And(Literals.Char('-').SkipAnd(CommonParsers.UnsignedInt).Optional())
                .Map((start, end) => new RawField(start, end.ToNullable(), null, ""));

        // Field: a bit spec ("start", "start-end", or "+bits") then ": label". Mermaid accepts all three;
        // absolute positions and contiguity are resolved in BuildModel since "+bits" depends on order.
        var fieldParser =
            CommonParsers.InlineWhitespace
                .SkipAnd(relativeSpec.Or(explicitSpec))
                .AndSkip(Literals.Char(':'))
                .AndSkip(CommonParsers.InlineWhitespace)
                .And(labelParser)
                .AndSkip(CommonParsers.LineEnd)
                .Map((spec, label) =>
                    spec with
                    {
                        Label = label
                    });

        // Skip line (comments, empty lines)
        var skipLine =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.Comment.Or(CommonParsers.Newline));

        var contentItem =
            OneOf(
                fieldParser.Then<RawField?>(static _ => _),
                skipLine.Then<RawField?>(static _ => null)
            );

        parser =
            CommonParsers.InlineWhitespace
                .SkipAnd(CommonParsers.BetaKeyword("packet"))
                .SkipAnd(CommonParsers.InlineWhitespace)
                .SkipAnd(CommonParsers.LineEnd)
                .SkipAnd(contentItem.Many())
                .AndSkip(CommonParsers.End)
                .Then(static _ => BuildModel(_.OfType<RawField>().ToList()));
    }

    // Resolve each raw field to absolute bit positions and validate the sequence the way Mermaid does:
    // fields must tile the packet contiguously from bit 0 with no gaps, overlaps, or reversed ranges.
    static PacketModel BuildModel(List<RawField> fields)
    {
        var model = new PacketModel();
        var nextBit = 0;

        foreach (var field in fields)
        {
            int start;
            int end;

            if (field.Bits is { } bits)
            {
                // "+bits" continues from the previous field; its start is contiguous by construction.
                start = nextBit;
                if (bits == 0)
                {
                    throw new MermaidParseException(
                        $"Packet block {start} is invalid. Cannot have a zero bit field.");
                }

                end = start + bits - 1;
            }
            else
            {
                start = field.Start!.Value;
                end = field.End ?? start;

                if (start > end)
                {
                    throw new MermaidParseException(
                        $"Packet block {start} - {end} is invalid. End must be greater than start.");
                }

                if (start != nextBit)
                {
                    throw new MermaidParseException(
                        $"Packet block {start} - {end} is not contiguous. It should start from {nextBit}.");
                }
            }

            model.Fields.Add(
                new()
                {
                    StartBit = start,
                    EndBit = end,
                    Label = field.Label
                });

            nextBit = end + 1;
        }

        return model;
    }

    public DiagramParseResult<PacketModel> Parse(string input) => parser.ParseDiagram(input);

    // A field as written, before resolution: an explicit Start (with optional End), or a relative
    // width in Bits ("+N"). Exactly one of (Start, Bits) is set.
    internal sealed record RawField(int? Start, int? End, int? Bits, string Label);
}

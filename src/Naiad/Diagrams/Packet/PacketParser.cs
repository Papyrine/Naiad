class PacketParser : IDiagramParser<PacketModel>
{
    static Parser<char, PacketModel> parser;

    static PacketParser()
    {
        var quotedLabel =
            Char('"').Then(Token(_ => _ != '"').ManyString()).Before(Char('"'));

        // Unquoted label (rest of line)
        var unquotedLabel =
            Token(_ => _ != '\r' && _ != '\n').AtLeastOnceString()
                .Select(_ => _.Trim());

        // Label (quoted or unquoted)
        var labelParser =
            quotedLabel.Or(unquotedLabel);

        // "+bits": a width relative to where the previous field ended.
        var relativeSpec =
            from _ in Char('+')
            from bits in CommonParsers.UnsignedInt
            select new RawField(null, null, bits, "");

        // "start" (single bit) or "start-end" (explicit range).
        var explicitSpec =
            from start in CommonParsers.UnsignedInt
            from end in Try(Char('-').Then(CommonParsers.UnsignedInt)).Optional()
            select new RawField(start, end.HasValue ? end.Value : null, null, "");

        // Field: a bit spec ("start", "start-end", or "+bits") then ": label". Mermaid accepts all three;
        // absolute positions and contiguity are resolved in BuildModel since "+bits" depends on order.
        var fieldParser =
            from _ in CommonParsers.InlineWhitespace
            from spec in relativeSpec.Or(explicitSpec)
            from __ in Char(':')
            from ___ in CommonParsers.InlineWhitespace
            from label in labelParser
            from ____ in CommonParsers.LineEnd
            select spec with
            {
                Label = label
            };

        // Skip line (comments, empty lines)
        var skipLine =
            Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Comment))
                .Or(Try(CommonParsers.InlineWhitespace.Then(CommonParsers.Newline)));

        var contentItem =
            OneOf(
                Try(fieldParser.Select<RawField?>(_ => _)),
                skipLine.ThenReturn<RawField?>(null)
            );

        parser =
            from _ in CommonParsers.InlineWhitespace
            from __ in CommonParsers.BetaKeyword("packet")
            from ___ in CommonParsers.InlineWhitespace
            from ____ in CommonParsers.LineEnd
            from result in contentItem.ManyThen(End)
            select BuildModel(result.Item1.Where(_ => _ != null).ToList());
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

    public Result<char, PacketModel> Parse(string input) => parser.Parse(input);

    // A field as written, before resolution: an explicit Start (with optional End), or a relative
    // width in Bits ("+N"). Exactly one of (Start, Bits) is set.
    internal sealed record RawField(int? Start, int? End, int? Bits, string Label);
}

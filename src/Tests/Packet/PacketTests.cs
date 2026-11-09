public class PacketTests : TestBase
{
    [Test]
    public Task Simple()
    {
        const string input =
            """
            packet-beta
            0-15: "Source Port"
            16-31: "Destination Port"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task TCPHeader()
    {
        const string input =
            """
            packet-beta
            0-15: "Source Port"
            16-31: "Destination Port"
            32-63: "Sequence Number"
            64-95: "Acknowledgment Number"
            96-99: "Data Offset"
            100-102: "Reserved"
            103-103: "NS"
            104-104: "CWR"
            105-105: "ECE"
            106-106: "URG"
            107-107: "ACK"
            108-108: "PSH"
            109-109: "RST"
            110-110: "SYN"
            111-111: "FIN"
            112-127: "Window Size"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task IPv4Header()
    {
        const string input =
            """
            packet-beta
            0-3: "Version"
            4-7: "IHL"
            8-13: "DSCP"
            14-15: "ECN"
            16-31: "Total Length"
            32-47: "Identification"
            48-50: "Flags"
            51-63: "Fragment Offset"
            64-71: "TTL"
            72-79: "Protocol"
            80-95: "Header Checksum"
            96-127: "Source IP Address"
            128-159: "Destination IP Address"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SingleRow()
    {
        const string input =
            """
            packet-beta
            0-7: "Byte 1"
            8-15: "Byte 2"
            16-23: "Byte 3"
            24-31: "Byte 4"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task Fields()
    {
        const string input =
            """
            packet-beta
            0-31: "First Word"
            32-63: "Second Word"
            64-95: "Third Word"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task SingleBit()
    {
        const string input =
            """
            packet-beta
            0-7: "Header"
            8: "Flag"
            9-15: "Data"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task RelativeWidth()
    {
        const string input =
            """
            packet-beta
            +8: "Byte 1"
            +8: "Byte 2"
            +16: "Word"
            """;

        return VerifySvg(input);
    }

    // Mermaid's docs example: 16-bit rows, mirrored so each runs from its highest bit down to bit 0.
    [Test]
    public Task DescendingBitsPerRow()
    {
        const string input =
            """
            ---
            config:
              packet:
                showBits: true
                bitOrder: descending
                bitsPerRow: 16
            ---
            packet
            0-7: "DATA"
            8-11: "TYPE"
            12: "EN"
            13-15: "RESERVED"
            16-23: "NEXT"
            """;

        return VerifySvg(input);
    }

    [Test]
    public Task HideBits()
    {
        const string input =
            """
            %%{init: {"packet": {"showBits": false, "bitsPerRow": 8}}}%%
            packet
            0-3: "Low"
            4-11: "Spans a row"
            """;

        return VerifySvg(input);
    }
}

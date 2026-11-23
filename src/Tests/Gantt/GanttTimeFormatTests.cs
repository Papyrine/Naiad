using Naiad.Diagrams.Gantt;

// Mermaid writes a gantt chart's axis with d3-time-format, so `axisFormat` takes d3's specifiers, read the
// way d3's default en-US locale reads them.
public class GanttTimeFormatTests
{
    [Test]
    [Arguments("2024-01-07 15:04:05.006", "%Y-%m-%d", "2024-01-07")]
    [Arguments("2024-01-07 15:04:05.006", "%d/%m/%y", "07/01/24")]
    [Arguments("2024-01-07 15:04:05.006", "%a %A", "Sun Sunday")]
    [Arguments("2024-01-07 15:04:05.006", "%b %B", "Jan January")]
    [Arguments("2024-01-07 15:04:05.006", "%H:%M:%S.%L", "15:04:05.006")]
    [Arguments("2024-01-07 15:04:05.006", "%I %p", "03 PM")]
    [Arguments("2024-01-07 00:30:00.000", "%I %p", "12 AM")]
    [Arguments("2024-01-07 15:04:05.006", "%j %f", "007 006000")]
    [Arguments("2024-08-07 15:04:05.006", "%q", "3")]
    [Arguments("2024-01-07 15:04:05.006", "%u %w", "7 0")]
    [Arguments("2024-01-07 15:04:05.006", "%s %Q", "1704639845 1704639845006")]
    [Arguments("2024-01-07 15:04:05.006", "%Z", "+0000")]
    // %e pads with a space where the others pad with zeros, and a modifier overrides either.
    [Arguments("2024-01-07 15:04:05.006", "%e", " 7")]
    [Arguments("2024-01-07 15:04:05.006", "%-d %-m %-I", "7 1 3")]
    [Arguments("2024-01-07 15:04:05.006", "%_m %0e", " 1 07")]
    // The locale's own date and time.
    [Arguments("2024-01-07 15:04:05.006", "%x", "1/7/2024")]
    [Arguments("2024-01-07 15:04:05.006", "%X", "3:04:05 PM")]
    [Arguments("2024-01-07 15:04:05.006", "%c", "1/7/2024, 3:04:05 PM")]
    // Week numbers: from the first Sunday, from the first Monday, and ISO 8601, whose year can differ.
    [Arguments("2023-01-01 00:00:00.000", "%U %W %V %G %g", "01 00 52 2022 22")]
    [Arguments("2024-12-30 00:00:00.000", "%U %W %V %G %g", "52 53 01 2025 25")]
    // A percent sign, a specifier d3 does not know, and a dangling percent sign.
    [Arguments("2024-01-07 15:04:05.006", "100%%", "100%")]
    [Arguments("2024-01-07 15:04:05.006", "%k", "k")]
    [Arguments("2024-01-07 15:04:05.006", "week %", "week ")]
    [Arguments("2024-01-07 15:04:05.006", "week %-", "week ")]
    public async Task Format(string date, string specifier, string expected)
    {
        var value = DateTime.ParseExact(date, "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);

        await Assert.That(TimeFormat.Format(specifier, value)).IsEqualTo(expected);
    }
}

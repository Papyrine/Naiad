public class LabelLinesTests
{
    [Test]
    [Arguments("one<br>two")]
    [Arguments("one<br/>two")]
    [Arguments("one<br />two")]
    [Arguments("one <BR/> two")]
    public async Task EveryBreakSpelling_SplitsTheLabel(string label)
    {
        await Assert.That(string.Join("|", LabelLines.Split(label))).IsEqualTo("one|two");
        await Assert.That(LabelLines.Count(label)).IsEqualTo(2);
        await Assert.That(LabelLines.HasBreak(label)).IsTrue();
        await Assert.That(LabelLines.Flatten(label)).IsEqualTo("one two");
    }

    [Test]
    [Arguments("plain")]
    [Arguments("a < b")]
    [Arguments("<b>bold</b>")]
    [Arguments("")]
    public async Task LabelWithoutABreak_IsOneLineLeftAsIs(string label)
    {
        await Assert.That(LabelLines.Split(label).Single()).IsEqualTo(label);
        await Assert.That(LabelLines.Count(label)).IsEqualTo(1);
        await Assert.That(LabelLines.WidestLength(label)).IsEqualTo(label.Length);
        await Assert.That(LabelLines.Flatten(label)).IsEqualTo(label);
    }

    [Test]
    public async Task WidthComesFromTheLongestLine()
    {
        const string label = "short<br/>the longest line<br/>mid";

        await Assert.That(LabelLines.Count(label)).IsEqualTo(3);
        await Assert.That(LabelLines.WidestLength(label)).IsEqualTo("the longest line".Length);
    }

    [Test]
    public async Task NullCountsAsOneLine() =>
        await Assert.That(LabelLines.Count(null)).IsEqualTo(1);
}

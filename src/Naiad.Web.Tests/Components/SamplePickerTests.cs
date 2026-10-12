public class SamplePickerTests : BunitTestContext
{
    [Test]
    public async Task Render_ListsEverySamplePlusPlaceholder()
    {
        var cut = Render<SamplePicker>(_ => _
            .Add(_ => _.Samples, DiagramSamples.All));

        var options = cut.FindAll("option");

        // One option per sample, plus the leading "Load a sample…" placeholder — and every sample name
        // must actually be listed, not merely the right count.
        await Assert.That(options.Count).IsEqualTo(DiagramSamples.All.Count + 1);

        var optionNames = options.Skip(1).Select(_ => _.TextContent.Trim()).ToList();
        foreach (var sample in DiagramSamples.All)
        {
            await Assert.That(optionNames).Contains(sample.Name);
        }
    }

    [Test]
    public async Task Change_RaisesOnSampleSelected_WithPickedSample()
    {
        DiagramSample? picked = null;
        var cut = Render<SamplePicker>(_ => _
            .Add(_ => _.Samples, DiagramSamples.All)
            .Add(_ => _.OnSampleSelected, (DiagramSample sample) => picked = sample));

        await cut.Find("select").ChangeAsync("Pie");

        await Assert.That(picked).IsNotNull();
        await Assert.That(picked!.Name).IsEqualTo("Pie");
    }

    [Test]
    public async Task Change_ToPlaceholder_DoesNotRaise()
    {
        var raised = false;
        var cut = Render<SamplePicker>(_ => _
            .Add(_ => _.Samples, DiagramSamples.All)
            .Add(_ => _.OnSampleSelected, (DiagramSample _) => raised = true));

        await cut.Find("select").ChangeAsync("");

        await Assert.That(raised).IsFalse();
    }
}

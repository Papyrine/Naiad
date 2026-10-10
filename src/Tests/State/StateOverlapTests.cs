using Naiad.Diagrams.State;

// Drives the State renderer with ValidateLayout enabled, which makes Render throw when a label, line,
// or node overlaps another (or falls outside the SVG bounds). This is the Release-config guard for the
// self-checks that previously only ran under #if DEBUG (and so never ran in the test suite).
public partial class StateOverlapTests
{
    [Test]
    [Arguments(StateSamples.Simple)]
    [Arguments(StateSamples.MultipleStates)]
    [Arguments(StateSamples.TransitionLabels)]
    [Arguments(StateSamples.DirectionLeftToRight)]
    [Arguments(StateSamples.CompositeState)]
    [Arguments(StateSamples.NestedCompositeState)]
    [Arguments(StateSamples.Description)]
    [Arguments(StateSamples.ForkJoinState)]
    [Arguments(StateSamples.ChoiceState)]
    [Arguments(StateSamples.StateWithNote)]
    [Arguments(StateSamples.StateDiagramV1)]
    [Arguments(StateSamples.Complex)]
    [Arguments(StateSamples.Concurrency)]
    [Arguments(StateSamples.NotesAndDescribedComposite)]
    public async Task NoLayoutOverlaps(string input)
    {
        var result = new StateParser().Parse(input);
        await Assert.That(result.Success).IsTrue();

        var renderer = new StateRenderer { ValidateLayout = true };
        await Assert.That(() => renderer.Render(result.Value, RenderOptions.Default)).ThrowsNothing();
    }

    // Checks the same invariant as the layout guard above, but from the rendered output rather than the
    // renderer's own bookkeeping, so it holds regardless of how that bookkeeping is written. The guard
    // measures each label as the chip it is painted in; measuring the glyphs instead understates the chip
    // by 8 units of width and 1.6 of height, and two chips could touch while the guard saw a gap.
    // Only the samples that declare labelled transitions - the rest paint no chips at all.
    [Test]
    [Arguments(StateSamples.TransitionLabels)]
    [Arguments(StateSamples.ChoiceState)]
    [Arguments(StateSamples.Complex)]
    public async Task EdgeLabelChipsDoNotOverlap(string input)
    {
        var svg = Mermaid.Render(input);

        var chips = EdgeLabelChipRegex()
            .Matches(svg)
            .Select(_ => (
                X: double.Parse(_.Groups[1].Value, CultureInfo.InvariantCulture),
                Y: double.Parse(_.Groups[2].Value, CultureInfo.InvariantCulture),
                Width: double.Parse(_.Groups[3].Value, CultureInfo.InvariantCulture),
                Height: double.Parse(_.Groups[4].Value, CultureInfo.InvariantCulture)))
            .ToList();

        // Guard against the regex silently matching nothing, which would make the loop below vacuous.
        await Assert.That(chips.Count)
            .IsGreaterThan(0)
            .Because("the sampled diagrams all carry edge labels");

        for (var i = 0; i < chips.Count; i++)
        {
            for (var j = i + 1; j < chips.Count; j++)
            {
                var a = chips[i];
                var b = chips[j];
                var overlaps = a.X < b.X + b.Width && a.X + a.Width > b.X &&
                               a.Y < b.Y + b.Height && a.Y + a.Height > b.Y;

                await Assert.That(overlaps)
                    .IsFalse()
                    .Because($"edge label chips {a} and {b} overlap");
            }
        }
    }

    [GeneratedRegex(@"<rect x='([-\d.]+)' y='([-\d.]+)' width='([-\d.]+)' height='([-\d.]+)'[^>]*fill='rgba\(232,232,232,0\.8\)'")]
    private static partial Regex EdgeLabelChipRegex();
}

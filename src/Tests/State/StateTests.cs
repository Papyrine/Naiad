public class StateTests : TestBase
{
    [Test]
    public Task Simple() => VerifySvg(StateSamples.Simple);

    [Test]
    public Task MultipleStates() => VerifySvg(StateSamples.MultipleStates);

    [Test]
    public Task TransitionLabels() => VerifySvg(StateSamples.TransitionLabels);

    [Test]
    public Task DirectionLeftToRight() => VerifySvg(StateSamples.DirectionLeftToRight);

    [Test]
    public Task CompositeState() => VerifySvg(StateSamples.CompositeState);

    [Test]
    public Task NestedCompositeState() => VerifySvg(StateSamples.NestedCompositeState);

    [Test]
    public Task Description() => VerifySvg(StateSamples.Description);

    [Test]
    public Task ForkJoinState() => VerifySvg(StateSamples.ForkJoinState);

    [Test]
    public Task ChoiceState() => VerifySvg(StateSamples.ChoiceState);

    [Test]
    public Task StateWithNote() => VerifySvg(StateSamples.StateWithNote);

    [Test]
    public Task StateDiagramV1() => VerifySvg(StateSamples.StateDiagramV1);

    [Test]
    public Task Complex() => VerifySvg(StateSamples.Complex);

    [Test]
    public Task LineBreaks() => VerifySvg(StateSamples.LineBreaks);

    [Test]
    public Task Concurrency() => VerifySvg(StateSamples.Concurrency);

    [Test]
    public Task NotesAndDescribedComposite() => VerifySvg(StateSamples.NotesAndDescribedComposite);
}

// The theme variables worked out from Mermaid's themes, checked against values Mermaid produces.
public class MermaidThemeTests
{
    static MermaidTheme Theme(string json) =>
        MermaidTheme.From(MermaidConfig.Create((Dictionary<string, object?>) ConfigReader.ReadJson(json)!, []), "pie")!;

    // The default theme's colours, written as khroma writes them. The pie and timeline renderers already
    // carried several of these, copied from Mermaid's output.
    [Test]
    public async Task Default()
    {
        var theme = Theme("{'theme': 'default'}");

        await Assert.That(theme["pie1"]).IsEqualTo("#ECECFF");
        await Assert.That(theme["pie2"]).IsEqualTo("#ffffde");
        await Assert.That(theme["pie3"]).IsEqualTo("hsl(80, 100%, 56.2745098039%)");
        await Assert.That(theme["pie4"]).IsEqualTo("hsl(240, 100%, 86.2745098039%)");
        // Darkened twice: once as the theme is made and again once overrides are applied.
        await Assert.That(theme["cScale0"]).IsEqualTo("hsl(240, 100%, 76.2745098039%)");
        await Assert.That(theme["cScale1"]).IsEqualTo("hsl(60, 100%, 73.5294117647%)");
        await Assert.That(theme["cScaleLabel0"]).IsEqualTo("#ffffff");
        await Assert.That(theme["cScaleLabel1"]).IsEqualTo("black");
        await Assert.That(theme["git0"]).IsEqualTo("hsl(240, 100%, 46.2745098039%)");
        await Assert.That(theme["primaryTextColor"]).IsEqualTo("#131300");
        await Assert.That(theme["primaryBorderColor"]).IsEqualTo("hsl(240, 60%, 86.2745098039%)");
    }

    [Test]
    public async Task ThemeVariablesAlone_ApplyOverTheDefaultTheme()
    {
        var theme = Theme("{'themeVariables': {'pie1': '#ff0000'}}");

        await Assert.That(theme["pie1"]).IsEqualTo("#ff0000");
        await Assert.That(theme["pie2"]).IsEqualTo("#ffffde");
    }

    [Test]
    public async Task Base_DerivesFromPrimaryColor()
    {
        var theme = Theme("{'theme': 'base', 'themeVariables': {'primaryColor': '#00ff00'}}");

        await Assert.That(theme["pie1"]).IsEqualTo("#00ff00");
        await Assert.That(theme["pie2"]).IsEqualTo("hsl(0, 100%, 50%)");
        await Assert.That(theme["git0"]).IsEqualTo("hsl(120, 100%, 25%)");
        await Assert.That(theme["primaryTextColor"]).IsEqualTo("#333");
    }

    [Test]
    public async Task Dark()
    {
        var theme = Theme("{'theme': 'dark'}");

        await Assert.That(theme["pie1"]).IsEqualTo("#0b0000");
        await Assert.That(theme["pieTitleTextColor"]).IsEqualTo("lightgrey");
        await Assert.That(theme["pieSectionTextColor"]).IsEqualTo("#ccc");
    }

    [Test]
    public async Task Neutral()
    {
        var theme = Theme("{'theme': 'neutral'}");

        await Assert.That(theme["pie1"]).IsEqualTo("#F4F4F4");
        await Assert.That(theme["pie12"]).IsEqualTo("#555");
        await Assert.That(theme["gitBranchLabel1"]).IsEqualTo("white");
    }

    [Test]
    public async Task NoTheme_IsNull() =>
        await Assert.That(MermaidTheme.From(MermaidConfig.Empty, "pie")).IsNull();

    [Test]
    public async Task Khroma_Functions()
    {
        await Assert.That(Khroma.Invert("#f4f4f4")).IsEqualTo("#0b0b0b");
        await Assert.That(Khroma.Darken("#ECECFF", 10)).IsEqualTo("hsl(240, 100%, 86.2745098039%)");
        await Assert.That(Khroma.Lighten("white", 10)).IsEqualTo("white");
        await Assert.That(Khroma.Adjust("#ff0000", r: -255, b: 255)).IsEqualTo("#0000ff");
        await Assert.That(Khroma.Adjust("#00ff00", h: 0)).IsEqualTo("#00ff00");
        await Assert.That(Khroma.IsDark("#1f2020")).IsTrue();
        await Assert.That(Khroma.IsDark("lightgrey")).IsFalse();
    }
}

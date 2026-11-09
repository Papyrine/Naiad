// Reading the configuration Mermaid takes from front matter (YAML) and %%{init}%% directives (JSON), and
// merging the two.
public class MermaidConfigTests
{
    [Test]
    public async Task Yaml_NestedMappingsAndScalars()
    {
        const string yaml =
            """
            title: Hello # a comment
            config:
              theme: 'base'
              themeVariables:
                primaryColor: "#00ff00"
              gitGraph:
                showBranches: false
                mainBranchOrder: 2
                mainBranchName: it's main
            # a whole-line comment
            other: ~
            """;

        var config = Read(yaml);

        await Assert.That(config.GetString("title")).IsEqualTo("Hello");
        await Assert.That(config.GetString("config", "theme")).IsEqualTo("base");
        await Assert.That(config.GetString("config", "themeVariables", "primaryColor")).IsEqualTo("#00ff00");
        await Assert.That(config.GetBool("config", "gitGraph", "showBranches")).IsFalse();
        await Assert.That(config.GetNumber("config", "gitGraph", "mainBranchOrder")).IsEqualTo(2);
        await Assert.That(config.GetString("config", "gitGraph", "mainBranchName")).IsEqualTo("it's main");
        await Assert.That(config.Get("other")).IsNull();
        await Assert.That(config.GetString("missing", "key")).IsNull();
    }

    [Test]
    public async Task Yaml_Lists()
    {
        const string yaml =
            """
            flow: [a, 'b c', 3]
            block:
              - one
              - two
            sameIndent:
            - x
            maps:
              - name: first
                value: 1
              - name: second
            spread: [
              left,
              right
            ]
            """;

        var config = Read(yaml);

        await Assert.That(ToText(config.Get("flow"))).IsEqualTo("a|b c|3");
        await Assert.That(ToText(config.Get("block"))).IsEqualTo("one|two");
        await Assert.That(ToText(config.Get("sameIndent"))).IsEqualTo("x");
        await Assert.That(ToText(config.Get("spread"))).IsEqualTo("left|right");

        var maps = (List<object?>) config.Get("maps")!;
        await Assert.That(maps.Count).IsEqualTo(2);
        await Assert.That(((Dictionary<string, object?>) maps[0]!)["value"]).IsEqualTo(1d);
        await Assert.That(((Dictionary<string, object?>) maps[1]!)["name"]).IsEqualTo("second");
    }

    [Test]
    public async Task Yaml_NotAMapping_IsNull() =>
        await Assert.That(ConfigReader.ReadYaml("- just\n- a list")).IsNull();

    // Mermaid swaps single quotes for double ones before parsing; keys may also be bare, and a trailing
    // comma does no harm.
    [Test]
    public async Task Json_Lenient()
    {
        var value = ConfigReader.ReadJson("{ 'theme': 'dark', themeVariables: { \"primaryColor\": \"#ff0000\", }, 'gitGraph': {'showBranches': false, 'mainBranchOrder': 1.5}, }");
        var config = MermaidConfig.Create((Dictionary<string, object?>) value!, []);

        await Assert.That(config.GetString("theme")).IsEqualTo("dark");
        await Assert.That(config.GetString("themeVariables", "primaryColor")).IsEqualTo("#ff0000");
        await Assert.That(config.GetBool("gitGraph", "showBranches")).IsFalse();
        await Assert.That(config.GetNumber("gitGraph", "mainBranchOrder")).IsEqualTo(1.5);
    }

    [Test]
    [Arguments("")]
    [Arguments("{")]
    [Arguments("{ : }")]
    [Arguments("{ 'a': [1, 2 }")]
    [Arguments("]]]")]
    public async Task Json_Malformed_DoesNotThrow(string json) =>
        await Assert.That(() => ConfigReader.ReadJson(json)).ThrowsNothing();

    // Later directives win over earlier ones, and all of them over front matter; mappings merge key by key.
    [Test]
    public async Task Merge_DirectivesOverFrontMatter()
    {
        var frontMatter = (Dictionary<string, object?>) ConfigReader.ReadJson("{'theme': 'forest', 'pie': {'textPosition': 0.5, 'donutHole': 0.2}}")!;
        var first = (Dictionary<string, object?>) ConfigReader.ReadJson("{'pie': {'donutHole': 0.4}}")!;
        var second = (Dictionary<string, object?>) ConfigReader.ReadJson("{'theme': 'dark'}")!;

        var config = MermaidConfig.Create(frontMatter, [first, second]);

        await Assert.That(config.GetString("theme")).IsEqualTo("dark");
        await Assert.That(config.GetNumber("pie", "donutHole")).IsEqualTo(0.4);
        await Assert.That(config.GetNumber("pie", "textPosition")).IsEqualTo(0.5);
    }

    // Values written as text still read as the type asked for.
    [Test]
    public async Task TypedReads_AcceptText()
    {
        var config = MermaidConfig.Create((Dictionary<string, object?>) ConfigReader.ReadJson("{'a': 'false', 'b': '16', 'c': 2024}")!, []);

        await Assert.That(config.GetBool("a")).IsFalse();
        await Assert.That(config.GetNumber("b")).IsEqualTo(16);
        await Assert.That(config.GetString("c")).IsEqualTo("2024");
        await Assert.That(config.GetBool("b")).IsNull();
    }

    // As in Mermaid, an init directive counts wherever it is, also after the diagram's keyword, and the
    // `initialize` spelling works too.
    [Test]
    public async Task Directive_AfterKeyword_AndInitializeSpelling()
    {
        const string withConfig =
            """
            ---
            config:
              gitGraph:
                showBranches: false
            ---
            gitGraph
              commit
              branch develop
              commit
            """;

        const string afterKeyword =
            """
            gitGraph
              %%{init: {'gitGraph': {'showBranches': false}}}%%
              commit
              branch develop
              commit
            """;

        const string initialize =
            """
            %%{initialize: {'gitGraph': {'showBranches': false}}}%%
            gitGraph
              commit
              branch develop
              commit
            """;

        var expected = Mermaid.Render(withConfig);
        await Assert.That(Mermaid.Render(afterKeyword)).IsEqualTo(expected);
        await Assert.That(Mermaid.Render(initialize)).IsEqualTo(expected);
    }

    static MermaidConfig Read(string yaml) =>
        MermaidConfig.Create(ConfigReader.ReadYaml(yaml), []);

    static string ToText(object? list) =>
        string.Join("|", ((List<object?>) list!).Select(_ => Convert.ToString(_, CultureInfo.InvariantCulture)));
}

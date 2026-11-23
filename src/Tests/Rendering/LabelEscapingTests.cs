using Naiad.Diagrams.Flowchart;

// Regression coverage for label escaping: user-supplied diagram text must never reach the rendered SVG as
// live markup. Flowchart node/edge labels travel through the <foreignObject> seam (SvgForeignObject emits
// raw XHTML, so the renderer pre-encodes the text), while other diagrams' labels go through native <text>
// (escaped by SvgText). A crafted label must come out inert either way.
public class LabelEscapingTests
{
    [Test]
    public async Task FlowchartNodeLabel_EscapesMarkup()
    {
        var svg = Mermaid.Render(
            """
            flowchart LR
                A["<script>alert(1)</script>"]
            """);

        await Assert.That(svg).DoesNotContain("<script>");
        await Assert.That(svg).Contains("&lt;script&gt;");
    }

    [Test]
    public async Task FlowchartNodeLabel_EscapesAttributeInjection()
    {
        var svg = Mermaid.Render(
            """
            flowchart LR
                A["<img src=x onerror=alert(1)>"]
            """);

        // The raw <img> tag must not appear; it comes out as encoded text instead.
        await Assert.That(svg).DoesNotContain("<img");
        await Assert.That(svg).Contains("&lt;img");
    }

    [Test]
    public async Task FlowchartEdgeLabel_EscapesMarkup()
    {
        var svg = Mermaid.Render(
            """
            flowchart LR
                A -->|"<script>x</script>"| B
            """);

        await Assert.That(svg).DoesNotContain("<script>");
        await Assert.That(svg).Contains("&lt;script&gt;");
    }

    // Locks the seam contract: SvgForeignObject emits HtmlContent verbatim (callers own escaping). Guards
    // against a well-meaning "fix" that blanket-encodes here — which would corrupt the <p>/<i> markup the
    // renderers legitimately pass.
    [Test]
    public async Task SvgForeignObject_EmitsHtmlContentVerbatim()
    {
        var foreignObject = new SvgForeignObject
        {
            X = 0,
            Y = 0,
            Width = 10,
            Height = 10,
            HtmlContent = "<p>kept &amp; intact</p>"
        };

        var builder = new StringBuilder();
        foreignObject.ToXml(builder);

        await Assert.That(builder.ToString()).Contains("<p>kept &amp; intact</p>");
    }

    // Style values (from style, classDef, linkStyle and sequence rect colours) land in attribute values.
    // A quote in one used to end the attribute, so the rest of the value became attributes or elements of
    // its own: an event handler, or a whole <script> through the coloured link markers.
    [Test]
    [Arguments("style", "flowchart LR\n    A --> B\n    style A fill:red' onload='alert(1),stroke:\"x\"<y>&z")]
    [Arguments("classDef", "flowchart LR\n    A:::bad --> B\n    classDef bad fill:red' onload='alert(1)")]
    [Arguments("linkStyle", "flowchart LR\n    A --> B\n    linkStyle 0 stroke:red'/><script>alert(1)</script><x a='")]
    [Arguments("sequence rect", "sequenceDiagram\n    rect red' onload='alert(1)\n    A->>B: hi\n    end")]
    [Arguments("use case style", "usecase-beta\n    actor A\n    style A fill:red' onload='alert(1)")]
    public async Task StyleValue_CannotBreakOutOfTheAttribute(string name, string input)
    {
        var svg = Mermaid.Render(input);

        var document = XDocument.Parse(svg);
        var injected = document
            .Descendants()
            .Where(_ => _.Name.LocalName == "script" || _.Attribute("onload") is not null)
            .ToList();
        await Assert.That(injected).IsEmpty().Because(name);
    }

    // A label's own colour (`color:` in style or classDef) is written into the <p> that holds an HTML label
    // in its <foreignObject>. It went in as written, so a quote in it ended the paragraph's style attribute
    // and the rest of the value became live HTML in the label. Such a value is no colour, so it is now
    // dropped before it gets that far (CssValue); the renderers still encode what they are given, which
    // LabelColour_IsEncodedByTheRenderer holds them to.
    [Test]
    [Arguments("flowchart style", "flowchart LR\n    A --> B\n    style A color:red\"><img src=x onerror=alert(1)>")]
    [Arguments("flowchart classDef", "flowchart LR\n    A:::bad --> B\n    classDef bad color:red\"><img src=x onerror=alert(1)>")]
    [Arguments("flowchart markdown", "flowchart LR\n    A[\"`**bold**`\"] --> B\n    style A color:red\"><img src=x onerror=alert(1)>")]
    [Arguments("block style", "block-beta\n    a[\"Hello\"]\n    style a color:red\"><img src=x onerror=alert(1)>")]
    [Arguments("use case style", "usecase-beta\n    Formatted(\"`**Formatted**`\")\n    style Formatted color:red\"><img src=x onerror=alert(1)>")]
    public async Task LabelColour_CannotBreakOutOfTheLabel(string name, string input)
    {
        var svg = Mermaid.Render(input);

        var document = XDocument.Parse(svg);
        var injected = document
            .Descendants()
            .Where(_ => _.Name.LocalName == "img" || _.Attribute("onerror") is not null)
            .ToList();
        await Assert.That(injected).IsEmpty().Because(name);
        await Assert.That(svg).DoesNotContain("onerror").Because(name);
    }

    // Whatever colour a label is given reaches its paragraph encoded, should one that is not a colour ever
    // get past the parser.
    [Test]
    public async Task LabelColour_IsEncodedByTheRenderer()
    {
        var model = new FlowchartParser().Parse("flowchart LR\n    A[Hello] --> B").Value;
        model.Nodes.Single(_ => _.Id == "A").Style = new() { Color = "red\"><img src=x onerror=alert(1)>" };

        var document = new FlowchartRenderer().Render(model, new());
        var builder = new StringBuilder();
        document.ToXml(builder);
        var svg = builder.ToString();

        XDocument.Parse(svg);
        await Assert.That(svg).DoesNotContain("<img");
        await Assert.That(svg).Contains("&quot;&gt;&lt;img");
    }

    // A style value that is not a colour (or, where that is wanted, a number or a dash list) is dropped, so
    // it can neither fetch from a host it names nor add declarations of its own. Escaping alone left both.
    [Test]
    [Arguments("flowchart fill", "flowchart LR\n    A --> B\n    style A fill:url(https://attacker.invalid/x)")]
    [Arguments("flowchart classDef", "flowchart LR\n    A:::c --> B\n    classDef c stroke:red;background:url(https://attacker.invalid/x)")]
    [Arguments("flowchart linkStyle", "flowchart LR\n    A --> B\n    linkStyle 0 stroke:url(https://attacker.invalid/x)")]
    [Arguments("flowchart dash array", "flowchart LR\n    A --> B\n    style A stroke-dasharray:5 url(https://attacker.invalid/x)")]
    [Arguments("flowchart escape", "flowchart LR\n    A --> B\n    style A fill:u\\72l(https://attacker.invalid/x)")]
    [Arguments("block", "block-beta\n    a[\"A\"]\n    style a fill:url(https://attacker.invalid/x)")]
    [Arguments("use case", "usecase-beta\n    actor A\n    style A fill:url(https://attacker.invalid/x)")]
    [Arguments("sequence rect", "sequenceDiagram\n    rect url(https://attacker.invalid/x)\n    A->>B: hi\n    end")]
    [Arguments("venn", "venn-beta\n    set A\n    set B\n    style A fill:\"url(https://attacker.invalid/x)\", stroke-width:\"url(https://attacker.invalid/y)\"")]
    [Arguments("xychart palette", "---\nconfig:\n  themeVariables:\n    xyChart:\n      plotColorPalette: \"url(https://attacker.invalid/x)\"\n---\nxychart-beta\n    x-axis [a, b]\n    bar [1, 2]")]
    [Arguments("sankey nodeColors", "---\nconfig:\n  sankey:\n    nodeColors:\n      A: \"url(https://attacker.invalid/x)\"\n---\nsankey-beta\nA,B,1")]
    [Arguments("theme variable", "%%{init: {\"themeVariables\": {\"pieStrokeColor\": \"url(https://attacker.invalid/x)\"}}}%%\npie\n    \"A\" : 1")]
    public async Task StyleValue_ThatIsNotAColour_IsDropped(string name, string input)
    {
        var svg = Mermaid.Render(input);

        XDocument.Parse(svg);
        await Assert.That(svg).DoesNotContain("attacker.invalid").Because(name);
    }

    // The colours Mermaid's documentation and its users write are kept.
    [Test]
    [Arguments("hex", "fill:#f9f,stroke:#333,color:#ffffff,stroke-dasharray: 5 5", "#f9f")]
    [Arguments("named", "fill:lightblue", "lightblue")]
    [Arguments("rgb", "fill:rgb(255 0 0)", "rgb(255 0 0)")]
    [Arguments("hsl", "fill:hsl(120deg 50% 50%)", "hsl(120deg 50% 50%)")]
    [Arguments("oklch", "fill:oklch(70% 0.1 200)", "oklch(70% 0.1 200)")]
    [Arguments("none", "fill:none", "fill='none'")]
    [Arguments("custom property", "fill:var(--accent)", "var(--accent)")]
    public async Task StyleValue_ThatIsAColour_IsKept(string name, string style, string expected)
    {
        var svg = Mermaid.Render($"flowchart LR\n    A --> B\n    style A {style}");

        await Assert.That(svg).Contains(expected).Because(name);
    }

    // A themeVariables value is written into the diagram's stylesheet. It went in as written, so a value
    // could close the <style> element and inject markup, or close its rule and add rules of its own, which
    // in an SVG inlined in a page style the whole page.
    [Test]
    [Arguments("closes the style element", "red}</style><img src=x onerror=alert(1)><style>", "onerror")]
    [Arguments("adds a rule", "red} svg{display:none", "display:none")]
    [Arguments("adds a declaration", "red;fill:url(https://attacker.invalid/x)", "attacker.invalid")]
    [Arguments("spells a brace with the following ;", "red&#125", "red&#125")]
    [Arguments("breaks the XML", "red&x", "red&x")]
    public async Task ThemeVariable_CannotBreakOutOfItsValue(string name, string value, string marker)
    {
        var input =
            "%%{init: {\"themeVariables\": {\"pieStrokeColor\": \"" + value + "\"}}}%%\n" +
            "pie\n    \"A\" : 1\n    \"B\" : 2";

        var svg = Mermaid.Render(input);

        XDocument.Parse(svg);
        await Assert.That(svg).DoesNotContain(marker).Because(name);
        // The theme's own value stands in for the one ignored.
        await Assert.That(svg).Contains("#naiad .pieCircle{stroke:black;").Because(name);
    }

    [Test]
    public async Task ThemeVariable_SafeValueIsApplied()
    {
        var svg = Mermaid.Render(
            """
            %%{init: {"themeVariables": {"pieStrokeColor": "#123456"}}}%%
            pie
                "A" : 1
                "B" : 2
            """);

        await Assert.That(svg).Contains("#naiad .pieCircle{stroke:#123456;");
    }

    // XML 1.0 allows no C0 control character but tab, line feed and carriage return, not even as a
    // character reference. Cynefin unescapes \0, \b, \f and \v into them, and they went into the text as
    // they were, so no XML parser would read the SVG.
    [Test]
    public async Task ControlCharacters_AreDroppedFromText()
    {
        var svg = Mermaid.Render(
            """
            cynefin-beta
              complex
                "a\0b" "c\vd" "e\bf\fg"
            """);

        var texts = XDocument.Parse(svg)
            .Descendants()
            .Where(_ => _.Name.LocalName == "text")
            .Select(_ => _.Value)
            .ToList();
        await Assert.That(texts).Contains("ab");
        await Assert.That(texts).Contains("cd");
        await Assert.That(texts).Contains("efg");
    }

    // An HTML label's text is HTML-encoded, which leaves control characters as they are, and the label's
    // markup is written into its <foreignObject> verbatim, so one in a label left the SVG unreadable.
    [Test]
    [Arguments("flowchart label", "flowchart LR\n    A[a\u0001b]", "ab")]
    [Arguments("mindmap icon", "mindmap\n    root\n        a ::icon(fa x\u0001y)", "fa xy")]
    public async Task ControlCharacters_AreDroppedFromHtmlLabels(string name, string input, string expected)
    {
        var svg = Mermaid.Render(input);

        XDocument.Parse(svg);
        await Assert.That(svg).Contains(expected).Because(name);
    }

    // XML allows neither U+FFFE nor U+FFFF, nor half of a surrogate pair, and they went through the text, the
    // attributes and the HTML labels as they were, so no XML parser would read the SVG. A whole pair, an emoji,
    // is one character and is kept.
    [Test]
    [Arguments("text, U+FFFF", "xychart-beta\n    x-axis [a\uFFFFb, c]\n    bar [1, 2]", "ab")]
    [Arguments("text, U+FFFE", "pie\n    \"a\uFFFEb\" : 1", "ab")]
    [Arguments("text, lone high surrogate", "pie\n    \"a\uD800b\" : 1", "ab")]
    [Arguments("text, lone low surrogate", "mindmap\n    root\n        a\uDC00b", "ab")]
    [Arguments("description", "pie\n    accDescr {\n        a\uD800w\n    }\n    \"A\" : 1", "aw")]
    [Arguments("HTML label", "flowchart LR\n    A[a\uFFFFb]", "ab")]
    [Arguments("emoji kept", "flowchart LR\n    A[a\uD83D\uDE00b]", "a\uD83D\uDE00b")]
    public async Task CharactersXmlDoesNotAllow_AreDropped(string name, string input, string expected)
    {
        foreach (var html in new[] { true, false })
        {
            var svg = Mermaid.Render(input, new() { AllowHtmlElements = html });

            var shown = string.Concat(XDocument.Parse(svg).DescendantNodes().OfType<XText>().Select(_ => _.Value));
            await Assert.That(shown).Contains(expected).Because(name);
        }
    }
}

// The SVG must not depend on the culture it is rendered under. A number formatted with the current
// culture comes out as "12,6" under German, which is not a valid SVG or CSS number.
public class CultureTests
{
    [Test]
    [Arguments("wardley title", "wardley-beta\ntitle Value chain\ncomponent A [0.5, 0.5]")]
    [Arguments("venn shrunk label", "venn-beta\n  set A[\"Supercalifragilisticexpialidocious\"]:20\n  set B:3\n  union A,B:1")]
    [Arguments("railroad repeat label", "railroad-abnf-beta\nr = 5*2DIGIT ;")]
    [Arguments("treemap", "treemap-beta\n\"Root\"\n  \"A\": 5\n  \"B\": 10")]
    public async Task Svg_IsTheSameUnderAnyCulture(string name, string input)
    {
        var invariant = RenderUnder(CultureInfo.InvariantCulture, input);
        var german = RenderUnder(new("de-DE"), input);

        await Assert.That(german).IsEqualTo(invariant).Because(name);
    }

    static string RenderUnder(CultureInfo culture, string input)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = culture;
        try
        {
            return Mermaid.Render(input);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}

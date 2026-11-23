// What CssValue takes for a colour, a dash list and a number: the forms CSS gives them, and nothing that
// could fetch from elsewhere or add declarations of its own.
public class CssValueTests
{
    [Test]
    [Arguments("#f9f")]
    [Arguments("#f9fa")]
    [Arguments("#ff99ff")]
    [Arguments("#ff99ff80")]
    [Arguments("red")]
    [Arguments("LightBlue")]
    [Arguments("transparent")]
    [Arguments("none")]
    [Arguments("currentColor")]
    [Arguments("rgb(255, 0, 0)")]
    [Arguments("rgb(255 0 0 / 50%)")]
    [Arguments("rgba(0,0,255,.1)")]
    [Arguments("hsl(120deg 50% 50%)")]
    [Arguments("hsla(120, 50%, 50%, 0.3)")]
    [Arguments("oklch(70% 0.1 200)")]
    [Arguments("lab(52% 40 59)")]
    [Arguments("hwb(194 0% 0%)")]
    [Arguments("var(--accent)")]
    [Arguments("  #333  ")]
    public async Task Colour_IsAccepted(string value) =>
        await Assert.That(CssValue.IsColor(value)).IsTrue();

    [Test]
    [Arguments("")]
    [Arguments(" ")]
    [Arguments("url(https://attacker.invalid/x)")]
    [Arguments("URL(https://attacker.invalid/x)")]
    [Arguments("u\\72l(https://attacker.invalid/x)")]
    [Arguments("red;display:none")]
    [Arguments("red display:none")]
    [Arguments("red\"><img src=x onerror=alert(1)>")]
    [Arguments("rgb(1 2 3) url(x)")]
    [Arguments("rgb(url(x))")]
    [Arguments("image-set(url(x) 1x)")]
    [Arguments("var(--x, url(x))")]
    [Arguments("expression(alert(1))")]
    [Arguments("#ggg")]
    [Arguments("#12345")]
    [Arguments("notacolour")]
    public async Task NotAColour_IsRejected(string value) =>
        await Assert.That(CssValue.IsColor(value)).IsFalse();

    [Test]
    [Arguments("5 5", true)]
    [Arguments("5,5", true)]
    [Arguments("5, 10", true)]
    [Arguments("5px 10px", true)]
    [Arguments("2.5 1.25", true)]
    [Arguments("none", true)]
    [Arguments("5 url(x)", false)]
    [Arguments("5;display:none", false)]
    [Arguments("", false)]
    public async Task DashArray(string value, bool accepted) =>
        await Assert.That(CssValue.IsDashArray(value)).IsEqualTo(accepted);

    [Test]
    [Arguments("2", true)]
    [Arguments("0.1", true)]
    [Arguments(".5", true)]
    [Arguments("4px", true)]
    [Arguments("2;fill:red", false)]
    [Arguments("url(x)", false)]
    [Arguments("-1", false)]
    public async Task Number(string value, bool accepted) =>
        await Assert.That(CssValue.IsNumber(value)).IsEqualTo(accepted);
}

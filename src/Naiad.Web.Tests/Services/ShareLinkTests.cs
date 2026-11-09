// Small diagrams round-trip through the URL fragment; large ones get no link.
public class ShareLinkTests
{
    [Test]
    public async Task RoundTrips()
    {
        var source = "flowchart TD\n  A[Zürich] --> B";
        var fragment = ShareLink.Encode(source);
        await Assert.That(fragment).IsNotNull();
        await Assert.That(ShareLink.TryDecode("#" + fragment, out var decoded)).IsTrue();
        await Assert.That(decoded).IsEqualTo(source);
    }

    [Test]
    public async Task EmptySourceHasNoLink() =>
        await Assert.That(ShareLink.Encode("")).IsNull();

    [Test]
    public async Task LargeSourceHasNoLink()
    {
        // Random text barely compresses, so 20k characters cannot fit the fragment limit.
        var random = new Random(1);
        var source = new string(Enumerable.Range(0, 20000).Select(_ => (char) random.Next('!', '~')).ToArray());
        await Assert.That(ShareLink.Encode(source)).IsNull();
    }

    [Test]
    public async Task RejectsGarbage()
    {
        await Assert.That(ShareLink.TryDecode("#code=!!!", out _)).IsFalse();
        await Assert.That(ShareLink.TryDecode("#code=AAAA", out _)).IsFalse();
        await Assert.That(ShareLink.TryDecode("#other", out _)).IsFalse();
        await Assert.That(ShareLink.TryDecode(null, out _)).IsFalse();
    }
}

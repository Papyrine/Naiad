/// <summary>Resolved entry: {vs, i, barycenter?, weight?}.</summary>
sealed class ResolvedEntry
{
    // Always given by whoever creates the entry, so not also allocated here to be thrown away.
    public List<string> Vs = null!;
    public int I;
    public double? Barycenter;
    public double? Weight;
}
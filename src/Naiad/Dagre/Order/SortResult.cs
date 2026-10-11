/// <summary>Result of <see cref="Sort"/>: {vs, barycenter?, weight?}.</summary>
sealed class SortResult
{
    public List<string> Vs = null!;
    public double? Barycenter;
    public double? Weight;
}
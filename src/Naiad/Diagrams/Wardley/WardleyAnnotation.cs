namespace Naiad.Diagrams.Wardley;

/// <summary>A numbered marker on the map, whose text is listed in the annotations box.</summary>
public class WardleyAnnotation
{
    public int Number { get; init; }

    public double Visibility { get; init; }

    public double Evolution { get; init; }

    public string? Text { get; init; }
}

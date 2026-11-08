namespace Naiad.Diagrams.Wardley;

/// <summary>A force speeding evolution up (an accelerator) or holding it back (a deaccelerator).</summary>
public class WardleyAccelerator
{
    public required string Name { get; init; }

    public double Visibility { get; init; }

    public double Evolution { get; init; }
}

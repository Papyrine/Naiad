namespace Naiad.Diagrams.XYChart;

public class XYChartModel : DiagramBase
{
    public string? XAxisLabel { get; set; }
    public List<string> XAxisCategories { get; } = [];

    // A numeric x-axis (x-axis 1 --> 12) rather than categories.
    public double? XAxisMin { get; set; }
    public double? XAxisMax { get; set; }
    public string? YAxisLabel { get; set; }
    public double? YAxisMin { get; set; }
    public double? YAxisMax { get; set; }
    public List<ChartSeries> Series { get; } = [];

    // Bars lie along the x-axis, which runs down the left, rather than standing on it.
    public bool Horizontal { get; set; }
}
namespace Naiad.Diagrams.XYChart;

public class ChartSeries
{
    public ChartSeriesType Type { get; init; }
    public List<double> Data { get; init; } = [];

    // The name given in `bar "Sales" [...]`, shown in the chart's legend.
    public string? Name { get; init; }
}
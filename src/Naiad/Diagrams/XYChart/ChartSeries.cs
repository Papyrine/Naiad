namespace Naiad.Diagrams.XYChart;

public class ChartSeries
{
    public ChartSeriesType Type { get; init; }
    public List<double> Data { get; init; } = [];

    // The name given in `bar "Sales" [...]`. Mermaid draws no legend for it, so neither does Naiad.
    public string? Name { get; init; }
}
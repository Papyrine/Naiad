namespace Naiad.Diagrams.Gantt;

public class GanttTask
{
    public required string Name { get; init; }
    public string? Id { get; set; }
    public DateTime? StartDate { get; set; }
    public string? AfterTaskId { get; set; }
    public TimeSpan? Duration { get; set; }

    // The calendar part of a duration (`2M`, `1y`), which has no fixed length and so cannot be a TimeSpan.
    public int DurationMonths { get; set; }
    public DateTime? EndDate { get; set; }
    public GanttTaskStatus Status { get; set; } = GanttTaskStatus.None;
    public bool IsCritical { get; set; }
    public bool IsMilestone { get; set; }

    // Computed properties for rendering
    public DateTime ComputedStart { get; set; }
    public DateTime ComputedEnd { get; set; }
    public string? SectionName { get; set; }
}
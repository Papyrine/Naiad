namespace Naiad.Diagrams.Kanban;

public class KanbanTask
{
    public required string Id { get; init; }
    public required string Name { get; init; }

    /// <summary>Who the task is assigned to, from <c>@{ assigned: 'name' }</c>.</summary>
    public string? Assigned { get; set; }

    /// <summary>The task's ticket number, from <c>@{ ticket: ABC-123 }</c>.</summary>
    public string? Ticket { get; set; }

    /// <summary>The task's priority, from <c>@{ priority: 'High' }</c>.</summary>
    public string? Priority { get; set; }
}
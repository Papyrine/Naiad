namespace Naiad.Diagrams.EventModeling;

public class EventModelingModel : DiagramBase
{
    /// <summary>The time frames and reset frames, in the order they were written, which is their order on the timeline.</summary>
    public List<EventModelingFrame> Frames { get; } = [];

    /// <summary>The <c>data</c> blocks that frames refer to with <c>[[name]]</c>.</summary>
    public List<EventModelingDataBlock> DataBlocks { get; } = [];

    /// <summary>The <c>note</c> blocks attached to frames. Mermaid parses these but does not draw them, and neither does Naiad.</summary>
    public List<EventModelingNote> Notes { get; } = [];
}

public enum EventModelingEntityKind
{
    Ui,
    Processor,
    Command,
    ReadModel,
    Event
}

public class EventModelingFrame
{
    /// <summary>The frame's number as written (<c>01</c>), unique across the timeline and compared as text.</summary>
    public required string Id { get; init; }

    /// <summary>A reset frame (<c>rf</c>) is not connected to the frame before it unless it says so with <c>-&gt;&gt;</c>.</summary>
    public bool IsReset { get; init; }

    public EventModelingEntityKind Kind { get; init; }

    /// <summary>The entity identifier as written, namespace included (<c>Inventory.ItemAdded</c>).</summary>
    public required string EntityIdentifier { get; init; }

    /// <summary>
    /// The namespace, which gives the entity a swimlane of its own. As in Mermaid, only an identifier of exactly
    /// two parts has one; any other is all name.
    /// </summary>
    public string? Namespace
    {
        get
        {
            var parts = EntityIdentifier.Split('.');
            return parts.Length == 2 ? parts[0] : null;
        }
    }

    /// <summary>The name drawn in the frame's box: the identifier without its namespace.</summary>
    public string Name
    {
        get
        {
            var parts = EntityIdentifier.Split('.');
            return parts.Length == 2 ? parts[1] : EntityIdentifier;
        }
    }

    /// <summary>The frames named with <c>-&gt;&gt;</c>, which replace the relation that would otherwise be inferred.</summary>
    public List<string> SourceFrameIds { get; } = [];

    /// <summary>The name of the data block given in <c>[[...]]</c>, if any.</summary>
    public string? DataReference { get; init; }

    /// <summary>The data type given in backticks before inline data (<c>json</c>, <c>md</c>, ...). Not drawn differently.</summary>
    public string? DataType { get; init; }

    /// <summary>The inline data without its enclosing braces or quotes, if any.</summary>
    public string? InlineData { get; init; }

    /// <summary>The 1-based line the frame is on, for error messages.</summary>
    public int Line { get; init; }
}

public class EventModelingDataBlock
{
    public required string Name { get; init; }

    public string? DataType { get; init; }

    /// <summary>The lines between the braces, with their indentation kept.</summary>
    public required string Content { get; init; }
}

public class EventModelingNote
{
    public required string FrameId { get; init; }

    public string? DataType { get; init; }

    public required string Content { get; init; }
}

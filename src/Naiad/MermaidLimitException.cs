namespace Naiad;

/// <summary>
/// A diagram is beyond one of the <see cref="RenderOptions.Limits"/>: it is too long, or its graph too large or
/// too deeply nested, to be rendered under them.
/// </summary>
public class MermaidLimitException(string message) :
    MermaidException(message);

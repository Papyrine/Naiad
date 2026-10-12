namespace Naiad;

/// <summary>
/// What a diagram parser made of its input: the model it read, or the reason it could not read one.
/// </summary>
public readonly struct DiagramParseResult<TModel>
    where TModel : DiagramBase
{
    readonly TModel? value;

    internal DiagramParseResult(TModel value)
    {
        this.value = value;
        Success = true;
    }

    internal DiagramParseResult(string error) =>
        Error = error;

    [MemberNotNullWhen(false, nameof(Error))]
    public bool Success { get; }

    /// <summary>
    /// The parsed model. Only a successful parse has one.
    /// </summary>
    public TModel Value
    {
        get
        {
            if (value is null)
            {
                throw new InvalidOperationException($"The parse failed, so it has no value: {Error}");
            }

            return value;
        }
    }

    /// <summary>
    /// Where and why the parse failed, or <see langword="null"/> when it did not.
    /// </summary>
    public string? Error { get; }
}

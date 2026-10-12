namespace Naiad;

public interface IDiagramParser<TModel> where TModel : DiagramBase
{
    DiagramParseResult<TModel> Parse(string input);
}

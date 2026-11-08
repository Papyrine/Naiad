namespace Naiad.Diagrams.UseCase;

/// <summary>A leaf of a JSON table: its path from the root and its value as Mermaid displays it.</summary>
public record JsonRow(string Key, string Value);

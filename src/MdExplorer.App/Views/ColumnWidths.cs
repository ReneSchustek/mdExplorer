namespace MdExplorer.App.Views;

/// <summary>Die ausgerechneten Breiten.</summary>
/// <param name="Folder">Linke Spalte.</param>
/// <param name="TagCloud">Tag-Cloud; 0, wenn sie verborgen ist.</param>
internal sealed record ColumnWidths(double Folder, double TagCloud);

namespace MdExplorer.App.Views;

/// <summary>Wunsch- und Mindestbreite einer Spalte.</summary>
/// <param name="Wanted">Die Breite, die die Spalte bei genug Platz hat.</param>
/// <param name="Minimum">Schmaler wird sie nicht.</param>
internal sealed record ColumnWish(double Wanted, double Minimum);

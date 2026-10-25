namespace MdExplorer.App.Tests.Fakes;

/// <summary>Erzeugt die Fehler, mit denen ein Lesezugriff auf den Index scheitern kann.</summary>
/// <remarks>
/// Die Attrappen warfen früher nur <see cref="InvalidOperationException"/>. Ein Fang, der die
/// Datenbank-Ausnahme übersah, fiel deshalb in keinem Test auf; jeder Fehlerpfad läuft hier mit
/// beiden Arten. Die Art kommt als Text, weil eine Theorie in einer öffentlichen Testklasse
/// keinen internen Typ als Parameter nehmen kann.
/// </remarks>
internal static class TestFailures
{
    /// <summary>SQLite meldet sich, etwa mit „database is locked“.</summary>
    public const string Database = "database";

    /// <summary>EF Core weist den Zugriff zurück, etwa auf einen schon entsorgten Kontext.</summary>
    public const string InvalidState = "invalid-state";

    public static Exception Of(string kind) => kind switch
    {
        Database => new TestDbException("database is locked"),
        InvalidState => new InvalidOperationException("Der Kontext ist bereits entsorgt."),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unbekannte Fehlerart."),
    };
}

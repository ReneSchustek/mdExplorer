using System.Data.Common;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Eine Datenbank-Ausnahme, die sich erzeugen lässt.
/// </summary>
/// <remarks>
/// <see cref="DbException"/> ist abstrakt; die Fehlerpfade, die auf sie hören, brauchen
/// trotzdem eine. Herausgezogen am 16.08.2026, als der zweite Testfall dieselbe Ausprägung
/// gebraucht hätte.
/// </remarks>
internal sealed class TestDbException : DbException
{
    public TestDbException()
    {
    }

    public TestDbException(string message)
        : base(message)
    {
    }

    public TestDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

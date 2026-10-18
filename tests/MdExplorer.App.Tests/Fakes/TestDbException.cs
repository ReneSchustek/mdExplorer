using System.Data.Common;

namespace MdExplorer.App.Tests.Fakes;

/// <summary>
/// Eine Datenbank-Ausnahme, die sich erzeugen lässt.
/// </summary>
/// <remarks>
/// <see cref="DbException"/> ist abstrakt; die Fehlerpfade, die auf sie hören, brauchen
/// trotzdem eine. Als eigene Klasse dient sie jedem Testfall, der diese Ausprägung braucht.
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

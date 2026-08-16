using System.Data.Common;

namespace MdExplorer.Indexer.Tests.Fakes;

/// <summary>
/// Eine Datenbank-Ausnahme für Tests.
/// </summary>
/// <remarks>
/// <see cref="DbException"/> ist abstrakt, und der Anbieter-eigene Typ zieht eine Abhängigkeit
/// auf SQLite in ein Testprojekt, das ohne Datenbank auskommt. Der Indexer unterscheidet
/// ohnehin nach dem Basistyp.
/// </remarks>
internal sealed class FakeDbException : DbException
{
    public FakeDbException()
        : base("Datenbank vorübergehend belegt.")
    {
    }

    public FakeDbException(string message)
        : base(message)
    {
    }

    public FakeDbException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

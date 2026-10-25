using System.Data.Common;

namespace MdExplorer.TagCloud.Tests.Fakes;

/// <summary><see cref="DbException"/> ist abstrakt; für den Fehlerpfad braucht es eine eigene Ausprägung.</summary>
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

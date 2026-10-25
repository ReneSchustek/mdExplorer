using System.Data.Common;

namespace MdExplorer.Data;

/// <summary>
/// Ein Datenbankfehler, den EF Core nicht als <see cref="DbException"/> meldet: ein gescheitertes
/// Speichern (<c>DbUpdateException</c>) oder ein erschöpfter Wiederholungsversuch
/// (<c>RetryLimitExceededException</c>).
/// </summary>
/// <remarks>
/// Die Dienste über der Datenschicht kennen EF Core nicht und fangen <see cref="DbException"/>.
/// Ohne diese Übersetzung liefen Schreibfehler an allen Fängen vorbei. Die ursprüngliche Ausnahme
/// steht in <see cref="Exception.InnerException"/>.
/// </remarks>
public sealed class DatabaseOperationException : DbException
{
    /// <summary>Erzeugt die Ausnahme ohne Meldung.</summary>
    public DatabaseOperationException()
    {
    }

    /// <summary>Erzeugt die Ausnahme mit Meldung.</summary>
    /// <param name="message">Beschreibung des Fehlers.</param>
    public DatabaseOperationException(string message)
        : base(message)
    {
    }

    /// <summary>Erzeugt die Ausnahme mit Meldung und ursprünglicher Ausnahme.</summary>
    /// <param name="message">Beschreibung des Fehlers.</param>
    /// <param name="innerException">Die Ausnahme, die EF Core geworfen hat.</param>
    public DatabaseOperationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

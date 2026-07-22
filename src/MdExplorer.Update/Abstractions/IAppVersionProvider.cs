using MdExplorer.Update.Models;

namespace MdExplorer.Update.Abstractions;

/// <summary>Liefert die installierte (laufende) Version der Anwendung als <see cref="SemanticVersion"/>.</summary>
public interface IAppVersionProvider
{
    /// <summary>Die aktuelle Version. Fällt auf <c>0.0.0</c> zurück, wenn sie nicht ermittelbar ist.</summary>
    SemanticVersion CurrentVersion { get; }
}

using System.IO.Pipes;

namespace MdExplorer.App.Hosting;

/// <summary>Übergibt einen Auftrag über eine Named Pipe an die laufende Instanz.</summary>
/// <remarks>
/// <see cref="PipeOptions.CurrentUserOnly"/> verbindet nur mit einem Kanal, den derselbe Benutzer
/// angelegt hat. Ein fremder Prozess unter dem Namen bekommt den Pfad nicht.
/// </remarks>
internal sealed class NamedPipeInstanceClient : IInstanceChannelClient
{
    private readonly string _pipeName;

    /// <summary>Erzeugt den Client.</summary>
    /// <param name="pipeName">Name des Kanals.</param>
    public NamedPipeInstanceClient(string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        _pipeName = pipeName;
    }

    /// <inheritdoc />
    public async Task SendAsync(string? absolutePath, CancellationToken cancellationToken)
    {
        NamedPipeClientStream pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await using (pipe.ConfigureAwait(false))
        {
            // Ist der Kanal gerade mit einer anderen Übergabe belegt, wartet das Verbinden, bis er
            // frei wird; die Zeitgrenze setzt der Aufrufer über den Abbruch.
            await pipe.ConnectAsync(cancellationToken).ConfigureAwait(false);
            await InstanceMessageCodec.WriteRequestAsync(pipe, absolutePath, cancellationToken).ConfigureAwait(false);
            await InstanceMessageCodec.ReadAcknowledgementAsync(pipe, cancellationToken).ConfigureAwait(false);
        }
    }
}

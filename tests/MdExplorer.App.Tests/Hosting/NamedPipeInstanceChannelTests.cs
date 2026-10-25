using System.Collections.Concurrent;
using System.IO.Pipes;
using MdExplorer.App.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Der echte Kanal zwischen zwei Instanzen, hier beide Seiten im selben Prozess. Jeder Test
/// benutzt einen eigenen Namen, damit parallele Läufe sich nicht gegenseitig bedienen.
/// </summary>
/// <remarks>
/// Gewartet wird auf Ereignisse, nicht auf Zeit: Die Obergrenze in <see cref="WaitAsync"/> hält nur
/// einen Test an, der sonst nie endete, und sagt nichts über die Geschwindigkeit.
/// </remarks>
public sealed class NamedPipeInstanceChannelTests : IDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _pipeName = "MdExplorer-Test-" + Guid.NewGuid().ToString("N");
    private readonly BlockingCollection<string?> _received = [];

    [Fact]
    public async Task SendAsync_ReachesTheServerAndIsConfirmed()
    {
        await using NamedPipeInstanceServer server = StartServer();

        await new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\Notizen\Plan.md", Token()).ConfigureAwait(true);

        Assert.Equal(@"C:\Notizen\Plan.md", Take(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task SendAsync_SeveralInARow_AreServedOneAfterTheOther()
    {
        await using NamedPipeInstanceServer server = StartServer();
        NamedPipeInstanceClient client = new(_pipeName);

        await client.SendAsync(@"C:\a.md", Token()).ConfigureAwait(true);
        await client.SendAsync(absolutePath: null, Token()).ConfigureAwait(true);
        await client.SendAsync(@"C:\b.md", Token()).ConfigureAwait(true);

        string?[] expected = [@"C:\a.md", null, @"C:\b.md"];
        string?[] actual = [Take(), Take(), Take()];
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task SendAsync_WithoutAServer_WaitsUntilCancelled()
    {
        // Lauscht niemand, kehrt das Verbinden nicht von selbst zurück; die Zeitgrenze kommt vom
        // Aufrufer (InstanceForwarder).
        using CancellationTokenSource cancel = new();
        Task sending = new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\a.md", cancel.Token);
        Assert.False(sending.IsCompleted);

        await cancel.CancelAsync().ConfigureAwait(true);

        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending).ConfigureAwait(true);
    }

    [Fact]
    public async Task Server_AfterAClientThatSendsGarbage_KeepsServing()
    {
        await using NamedPipeInstanceServer server = StartServer();
        NamedPipeClientStream rogue = await ConnectRawAsync().ConfigureAwait(true);
        await using (rogue.ConfigureAwait(true))
        {
            await rogue.WriteAsync(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F }, Token()).ConfigureAwait(true);
            // Der Server weist die Länge ab und trennt, ohne zu bestätigen.
            Assert.Equal(0, await rogue.ReadAsync(new byte[1], Token()).ConfigureAwait(true));
        }

        await new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\danach.md", Token()).ConfigureAwait(true);

        Assert.Equal(@"C:\danach.md", Take(), StringComparer.Ordinal);
    }

    [Fact]
    public async Task Server_AfterAClientThatHangsUpEarly_KeepsServing()
    {
        await using NamedPipeInstanceServer server = StartServer();
        NamedPipeClientStream rogue = await ConnectRawAsync().ConfigureAwait(true);
        await using (rogue.ConfigureAwait(true))
        {
            await rogue.WriteAsync(new byte[] { 10, 0 }, Token()).ConfigureAwait(true);
        }

        await new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\danach.md", Token()).ConfigureAwait(true);

        Assert.Equal(@"C:\danach.md", Take(), StringComparer.Ordinal);
    }

    [Fact]
    public void ConnectionTimeout_KeepsASilentClientFromBlockingTheChannelForLong()
    {
        Assert.InRange(NamedPipeInstanceServer.ConnectionTimeout, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task DisposeAsync_StopsListening()
    {
        NamedPipeInstanceServer server = StartServer();
        await new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\a.md", Token()).ConfigureAwait(true);
        _ = Take();

        await server.DisposeAsync().ConfigureAwait(true);
        await server.DisposeAsync().ConfigureAwait(true);

        using CancellationTokenSource cancel = new();
        Task sending = new NamedPipeInstanceClient(_pipeName).SendAsync(@"C:\b.md", cancel.Token);
        await cancel.CancelAsync().ConfigureAwait(true);
        _ = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending).ConfigureAwait(true);
        Assert.Empty(_received);
    }

    [Fact]
    public async Task DisposeAsync_WithoutStart_DoesNothing()
    {
        NamedPipeInstanceServer server = new(_pipeName, _received.Add, NullLogger<NamedPipeInstanceServer>.Instance);

        await server.DisposeAsync().ConfigureAwait(true);

        Assert.Empty(_received);
    }

    [Fact]
    public async Task Server_WhenTheNameIsTaken_GivesUpWithoutThrowing()
    {
        // Ein anderer Prozess hält den Namen schon; die Anwendung läuft trotzdem, nur ohne Übergabe.
        NamedPipeServerStream squatter = new(_pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        await using (squatter.ConfigureAwait(true))
        {
            NamedPipeInstanceServer server = StartServer();

            // Hätte das Lauschen eine Ausnahme nach außen gelassen, käme sie hier an.
            await server.DisposeAsync().ConfigureAwait(true);
        }

        Assert.Empty(_received);
    }

    [Fact]
    public void Constructor_RejectsMissingParts()
    {
        _ = Assert.Throws<ArgumentException>(() => new NamedPipeInstanceServer(" ", _received.Add, NullLogger<NamedPipeInstanceServer>.Instance));
        _ = Assert.Throws<ArgumentNullException>(() => new NamedPipeInstanceServer(_pipeName, null!, NullLogger<NamedPipeInstanceServer>.Instance));
        _ = Assert.Throws<ArgumentNullException>(() => new NamedPipeInstanceServer(_pipeName, _received.Add, null!));
        _ = Assert.Throws<ArgumentException>(() => new NamedPipeInstanceClient(""));
    }

    public void Dispose() => _received.Dispose();

    private static CancellationToken Token() => TestContext.Current.CancellationToken;

    private static async Task WaitAsync(Task task)
    {
        await task.WaitAsync(Patience, Token()).ConfigureAwait(true);
    }

    private NamedPipeInstanceServer StartServer()
    {
        NamedPipeInstanceServer server = new(_pipeName, _received.Add, NullLogger<NamedPipeInstanceServer>.Instance);
        server.Start();
        return server;
    }

    private async Task<NamedPipeClientStream> ConnectRawAsync()
    {
        NamedPipeClientStream pipe = new(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await WaitAsync(pipe.ConnectAsync(Token())).ConfigureAwait(true);
        return pipe;
    }

    private string? Take()
    {
        Assert.True(_received.TryTake(out string? path, Patience), "Der Server hat nichts empfangen.");
        return path;
    }
}

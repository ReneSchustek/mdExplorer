using System.IO;
using MdExplorer.App.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace MdExplorer.App.Tests.Hosting;

/// <summary>
/// Die zweite Instanz wartet höchstens bis zur Zeitgrenze auf die laufende. Ob die hängt, belegt
/// ist oder sich gerade beendet: Die zweite meldet das, statt still zu verschwinden oder ewig zu
/// warten.
/// </summary>
public sealed class InstanceForwarderTests
{
    private const string NotePath = @"C:\Notizen\Plan.md";

    private readonly FakeTimeProvider _time = new();

    [Fact]
    public async Task ForwardAsync_WhenTheRunningInstanceConfirms_Succeeds()
    {
        RecordingClient client = new();

        bool delivered = await CreateSut(client).ForwardAsync(NotePath).ConfigureAwait(true);

        Assert.True(delivered);
        Assert.Equal([NotePath], client.Sent);
    }

    [Fact]
    public async Task ForwardAsync_WithoutAFile_AsksOnlyForTheWindow()
    {
        RecordingClient client = new();

        Assert.True(await CreateSut(client).ForwardAsync(absolutePath: null).ConfigureAwait(true));
        Assert.Equal([null], client.Sent);
    }

    [Fact]
    public async Task ForwardAsync_WhenTheRunningInstanceHangs_GivesUpAtTheTimeout()
    {
        HangingClient client = new();
        Task<bool> forwarding = CreateSut(client).ForwardAsync(NotePath);

        _time.Advance(InstanceForwarder.Timeout - TimeSpan.FromMilliseconds(1));
        Assert.False(forwarding.IsCompleted);

        _time.Advance(TimeSpan.FromMilliseconds(1));

        Assert.False(await forwarding.ConfigureAwait(true));
        Assert.True(client.SawCancellation);
    }

    [Fact]
    public void Timeout_LeavesTimeForABusyMachineButNotForever()
    {
        Assert.InRange(InstanceForwarder.Timeout, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(EndOfStreamException))]
    [InlineData(typeof(InvalidDataException))]
    [InlineData(typeof(UnauthorizedAccessException))]
    public async Task ForwardAsync_WhenTheChannelFails_ReportsFailure(Type failure)
    {
        FailingClient client = new((Exception)Activator.CreateInstance(failure)!);

        Assert.False(await CreateSut(client).ForwardAsync(NotePath).ConfigureAwait(true));
    }

    [Fact]
    public async Task ForwardAsync_WithAnUnexpectedFailure_LetsItThrough()
    {
        // Was nicht zum Kanal gehört, ist ein Programmfehler und kein Grund für die Meldung
        // „antwortet nicht“; der Start meldet ihn als solchen.
        FailingClient client = new(new InvalidOperationException("kaputt"));

        _ = await Assert.ThrowsAsync<InvalidOperationException>(() => CreateSut(client).ForwardAsync(NotePath)).ConfigureAwait(true);
    }

    [Fact]
    public void Constructor_RejectsMissingParts()
    {
        _ = Assert.Throws<ArgumentNullException>(() => new InstanceForwarder(null!, _time, NullLogger<InstanceForwarder>.Instance));
        _ = Assert.Throws<ArgumentNullException>(() => new InstanceForwarder(new RecordingClient(), null!, NullLogger<InstanceForwarder>.Instance));
        _ = Assert.Throws<ArgumentNullException>(() => new InstanceForwarder(new RecordingClient(), _time, null!));
    }

    private InstanceForwarder CreateSut(IInstanceChannelClient client) =>
        new(client, _time, NullLogger<InstanceForwarder>.Instance);

    /// <summary>Bestätigt sofort und merkt sich, was übergeben wurde.</summary>
    private sealed class RecordingClient : IInstanceChannelClient
    {
        public List<string?> Sent { get; } = [];

        public Task SendAsync(string? absolutePath, CancellationToken cancellationToken)
        {
            Sent.Add(absolutePath);
            return Task.CompletedTask;
        }
    }

    /// <summary>Antwortet nie; so sieht eine hängende oder belegte Instanz von außen aus.</summary>
    private sealed class HangingClient : IInstanceChannelClient
    {
        public bool SawCancellation { get; private set; }

        public async Task SendAsync(string? absolutePath, CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                SawCancellation = true;
                throw;
            }
        }
    }

    /// <summary>Scheitert mit dem vorgegebenen Fehler.</summary>
    private sealed class FailingClient(Exception failure) : IInstanceChannelClient
    {
        public Task SendAsync(string? absolutePath, CancellationToken cancellationToken) => Task.FromException(failure);
    }
}

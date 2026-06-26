using CommunityToolkit.Mvvm.Messaging;
using MdExplorer.App.Services;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Messaging;
using MdExplorer.Core.Models;
using MdExplorer.Indexer.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace MdExplorer.App.Tests.Services;

/// <summary>Unit-Tests der <see cref="SettingsChangeBridge"/>.</summary>
public sealed class SettingsChangeBridgeTests
{
    [Fact]
    public async Task StartAsync_SubscribesToSettingsChanged()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new();
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            int received = 0;
            messenger.Register<SettingsChangedMessage>(this, (_, _) => received++);

            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);

            Assert.Equal(1, received);
            messenger.UnregisterAll(this);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StopAsync_UnsubscribesAndCancelsRescan()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new();
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);

        int received = 0;
        messenger.Register<SettingsChangedMessage>(this, (_, _) => received++);

        settings.RaiseChanged(AppSettings.Default, AppSettings.Default);

        Assert.Equal(0, received);
        messenger.UnregisterAll(this);
    }

    [Fact]
    public async Task OnSettingsChanged_SendsMessageAndTriggersRescan()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new();
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            SettingsChangedMessage? received = null;
            messenger.Register<SettingsChangedMessage>(this, (_, message) => received = message);

            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            Assert.NotNull(received);
            Assert.Equal(1, indexer.CallCount);

            messenger.UnregisterAll(this);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task TriggerRescanAsync_OnOperationCanceledException_DoesNotThrow()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new() { ThrowOnNextCall = new OperationCanceledException() };
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            Assert.Equal(1, indexer.CallCount);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task TriggerRescanAsync_OnArgumentException_LogsAndContinues()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new() { ThrowOnNextCall = new ArgumentException("simulated arg error from indexer") };
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            Assert.Equal(1, indexer.CallCount);

            // Bridge laeuft weiter, ArgumentException wird konsumiert.
            indexer.ThrowOnNextCall = null;
            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(true);
            Assert.Equal(2, indexer.CallCount);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task TriggerRescanAsync_OnIOException_LogsAndContinues()
    {
        FakeSettingsService settings = new();
        FakeIndexer indexer = new() { ThrowOnNextCall = new System.IO.IOException("disk error") };
        StrongReferenceMessenger messenger = new();
        SettingsChangeBridge sut = CreateSut(settings, indexer, messenger);

        await sut.StartAsync(CancellationToken.None).ConfigureAwait(true);
        try
        {
            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 1, TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            Assert.Equal(1, indexer.CallCount);

            // Zweiter Lauf — Bridge laeuft weiter, Exception ist konsumiert.
            indexer.ThrowOnNextCall = null;
            settings.RaiseChanged(AppSettings.Default, AppSettings.Default);
            await WaitForAsync(() => indexer.CallCount >= 2, TimeSpan.FromSeconds(5)).ConfigureAwait(true);

            Assert.Equal(2, indexer.CallCount);
        }
        finally
        {
            await sut.StopAsync(CancellationToken.None).ConfigureAwait(true);
        }
    }

    private static SettingsChangeBridge CreateSut(
        FakeSettingsService settings,
        FakeIndexer indexer,
        IMessenger messenger) =>
        new(settings, messenger, indexer, NullLogger<SettingsChangeBridge>.Instance);

    private static async Task WaitForAsync(Func<bool> condition, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(10).ConfigureAwait(true);
        }
        if (!condition())
        {
            throw new TimeoutException($"Bedingung nicht innerhalb von {timeout.TotalSeconds:F1}s erfuellt.");
        }
    }

    private sealed class FakeSettingsService : ISettingsService
    {
        public AppSettings Current { get; private set; } = AppSettings.Default;

        public event EventHandler<SettingsChangedEventArgs>? SettingsChanged;

        public Task<AppSettings> LoadAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Current);

        public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken)
        {
            Current = settings;
            return Task.CompletedTask;
        }

        public void RaiseChanged(AppSettings previous, AppSettings current) =>
            SettingsChanged?.Invoke(this, new SettingsChangedEventArgs(previous, current));
    }

    private sealed class FakeIndexer : IIndexer
    {
        private int _callCount;

        public event EventHandler<IndexerScanProgressEventArgs>? InitialScanProgress;

        public int CallCount => _callCount;

        public Exception? ThrowOnNextCall { get; set; }

        public Task RunInitialScanAsync(CancellationToken cancellationToken)
        {
            _ = Interlocked.Increment(ref _callCount);
            Exception? toThrow = ThrowOnNextCall;
            if (toThrow is not null)
            {
                ThrowOnNextCall = null;
                return Task.FromException(toThrow);
            }
            return Task.CompletedTask;
        }

        public void Raise(IndexerScanProgressEventArgs args) =>
            InitialScanProgress?.Invoke(this, args);
    }
}

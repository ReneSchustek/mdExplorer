using MdExplorer.Update.Abstractions;
using MdExplorer.Update.Models;

namespace MdExplorer.Update.Tests.Fakes;

/// <summary>Liefert eine feste Version für Tests.</summary>
internal sealed class FakeAppVersionProvider : IAppVersionProvider
{
    /// <summary>Erzeugt den Provider mit einer festen Version.</summary>
    public FakeAppVersionProvider(SemanticVersion current) => CurrentVersion = current;

    /// <inheritdoc />
    public SemanticVersion CurrentVersion { get; }
}

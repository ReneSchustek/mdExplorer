using MdExplorer.Core.Abstractions;

namespace MdExplorer.Core.Diagnostics;

/// <summary>
/// Prozessweiter Zustandshalter für <see cref="IParseFailureStatus"/>. Als Singleton
/// registriert: Der Parser läuft im Hintergrund und schreibt, die Oberfläche liest —
/// deshalb ist der Zugriff gesperrt und <see cref="Changed"/> feuert außerhalb der Sperre.
/// </summary>
public sealed class ParseFailureStatus : IParseFailureStatus
{
    private readonly object _gate = new();
    private int _unparsableFileCount;

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public int UnparsableFileCount
    {
        get
        {
            lock (_gate)
            {
                return _unparsableFileCount;
            }
        }
    }

    /// <inheritdoc />
    public void Update(int unparsableFileCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(unparsableFileCount);

        bool changed;
        lock (_gate)
        {
            changed = _unparsableFileCount != unparsableFileCount;
            _unparsableFileCount = unparsableFileCount;
        }
        if (changed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}

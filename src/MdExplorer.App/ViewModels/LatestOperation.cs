namespace MdExplorer.App.ViewModels;

/// <summary>
/// Lässt von mehreren gleichartigen Vorgängen nur den jüngsten gelten.
/// </summary>
/// <remarks>
/// <para>
/// Jeder Klick in einer Liste startet einen Ladevorgang. Ohne Abstimmung trifft die Antwort des
/// ersten Klicks womöglich nach der des zweiten ein, etwa wenn ihre Datenbankabfrage langsamer
/// war, und die Anzeige zeigt dann eine Datei, die gar nicht mehr gewählt ist.
/// </para>
/// <para>
/// Ein neuer Vorgang bricht den vorigen über dessen Merker ab. Wer nach einem <c>await</c> etwas
/// anzeigt, prüft vorher den Merker oder <see cref="Ticket.IsCurrent"/>; ein überholter Vorgang
/// endet dann still, ohne etwas zu überschreiben. Den Schein gibt der Aufrufer frei, nicht diese
/// Klasse: Nur er weiß, wann sein Vorgang zu Ende ist.
/// </para>
/// </remarks>
internal sealed class LatestOperation : IDisposable
{
    private readonly Lock _gate = new();
    private long _currentNumber;
    private Action? _cancelCurrent;
    private bool _disposed;

    /// <summary>Startet einen Vorgang und bricht den laufenden ab.</summary>
    /// <param name="outerToken">Übergeordneter Merker, etwa das Beenden der Anwendung.</param>
    /// <returns>Der Schein des neuen Vorgangs; der Aufrufer gibt ihn am Ende frei.</returns>
    public Ticket Begin(CancellationToken outerToken)
    {
        Action? cancelPrevious;
        Ticket next;
        bool startCancelled;
        lock (_gate)
        {
            _currentNumber++;
            next = new Ticket(this, _currentNumber, CancellationTokenSource.CreateLinkedTokenSource(outerToken));
            cancelPrevious = _cancelCurrent;
            _cancelCurrent = next.Cancel;
            startCancelled = _disposed;
        }

        cancelPrevious?.Invoke();
        if (startCancelled)
        {
            next.Cancel();
        }
        return next;
    }

    /// <summary>Bricht den laufenden Vorgang ab; danach startet jeder neue schon abgebrochen.</summary>
    public void Dispose()
    {
        Action? cancelRunning;
        lock (_gate)
        {
            _disposed = true;
            _currentNumber++;
            cancelRunning = _cancelCurrent;
            _cancelCurrent = null;
        }

        cancelRunning?.Invoke();
    }

    private bool IsCurrentNumber(long number)
    {
        lock (_gate)
        {
            return !_disposed && _currentNumber == number;
        }
    }

    /// <summary>Ein einzelner Vorgang und sein Abbruchmerker.</summary>
    internal sealed class Ticket : IDisposable
    {
        private readonly LatestOperation _owner;
        private readonly long _number;
        private readonly CancellationTokenSource _source;
        private bool _released;

        internal Ticket(LatestOperation owner, long number, CancellationTokenSource source)
        {
            _owner = owner;
            _number = number;
            _source = source;
            Token = source.Token;
        }

        /// <summary>Merker, der anschlägt, sobald ein neuerer Vorgang beginnt.</summary>
        public CancellationToken Token { get; }

        /// <summary>Ob dieser Vorgang noch der jüngste ist.</summary>
        public bool IsCurrent => !Token.IsCancellationRequested && _owner.IsCurrentNumber(_number);

        /// <summary>Gibt den Merker frei. Ein bereits gelesener Merker bleibt abfragbar.</summary>
        public void Dispose()
        {
            lock (_source)
            {
                _released = true;
                _source.Dispose();
            }
        }

        internal void Cancel()
        {
            // Ein beendeter Vorgang hat seinen Merker schon freigegeben; es gibt nichts mehr
            // abzubrechen. Die Sperre verhindert, dass Abbruch und Freigabe sich kreuzen.
            lock (_source)
            {
                if (!_released)
                {
                    _source.Cancel();
                }
            }
        }
    }
}

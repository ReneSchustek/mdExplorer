using System.Data.Common;
using System.Diagnostics.CodeAnalysis;
using MdExplorer.Core.Abstractions;
using MdExplorer.Core.Models;
using MdExplorer.Data.Repositories;
using MdExplorer.Data.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MdExplorer.Data.Tests;

/// <summary>
/// Grobe Wächter für die Geschwindigkeit der heißen Abfragen, ohne Stoppuhr.
/// </summary>
/// <remarks>
/// <para>
/// Ein Zeitbudget im normalen Testlauf schlägt unter paralleler Last an, ohne dass sich am Code
/// etwas geändert hat. Geprüft wird deshalb, was die Laufzeit bestimmt und von der Last nicht
/// abhängt: der Weg, den SQLite nimmt (<c>EXPLAIN QUERY PLAN</c>), und die Zahl der Abfragen je
/// Vorgang. Ein Suchschritt, der zum Durchlauf über alle Zeilen wird, oder eine Abfrage je Datei
/// fällt hier auf, lange bevor ein Nutzer sie im Bestand von tausenden Notizen spürt.
/// </para>
/// <para>
/// Die eigentlichen Zahlen misst das Projekt <c>MdExplorer.Benchmarks</c>.
/// </para>
/// </remarks>
public sealed class QueryPlanGuardTests : IAsyncDisposable
{
    private static readonly Guid FirstFileId = new("11111111-1111-1111-1111-111111111111");

    private readonly SqliteConnection _connection;
    private readonly CommandRecorder _recorder = new();
    private readonly MdExplorerDbContext _dbContext;

    public QueryPlanGuardTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        DbContextOptions<MdExplorerDbContext> options = new DbContextOptionsBuilder<MdExplorerDbContext>()
            .UseSqlite(_connection)
            .AddInterceptors(new SqlitePragmaInterceptor(), _recorder)
            .Options;

        _dbContext = new MdExplorerDbContext(options);
        // Volle Migration statt EnsureCreated: Indizes und die FTS5-Tabelle entstehen erst dort.
        _dbContext.Database.Migrate();
    }

    public async ValueTask DisposeAsync()
    {
        await _dbContext.DisposeAsync().ConfigureAwait(false);
        await _connection.DisposeAsync().ConfigureAwait(false);
    }

    [Fact]
    public async Task FullTextSearch_UsesTheFtsIndex()
    {
        IReadOnlyList<string> plan = await ExplainAsync(
            SqliteSearchIndexStorage.SelectWithoutPathFilterSql,
            ("$snippetTokens", 16),
            ("$wTitle", 10.0),
            ("$wBody", 1.0),
            ("$wTags", 5.0),
            ("$wFrontmatter", 1.0),
            ("$match", "\"rechnung\""),
            ("$take", 20),
            ("$skip", 0)).ConfigureAwait(true);

        Assert.True(UsesFtsMatch(plan), "Die Suche läuft nicht über den FTS-Index: " + string.Join(" | ", plan));
    }

    [Fact]
    public async Task BodiesById_ReadTheFtsTableOnceForAllIds()
    {
        // "MarkdownFileId" ist UNINDEXED; ein Durchlauf ist unvermeidlich. Er darf aber nur einmal
        // stattfinden: Die Kennungen kommen als eine Unterabfrage über json_each und nicht je
        // Kennung als eigene Anweisung.
        IReadOnlyList<string> plan = await ExplainAsync(
            SqliteSearchIndexStorage.SelectBodiesByIdsSql,
            ("$ids", "[\"11111111-1111-1111-1111-111111111111\"]")).ConfigureAwait(true);

        Assert.Equal(1, plan.Count(line => line.StartsWith("SCAN MarkdownSearchIndex", StringComparison.Ordinal)));
        Assert.Contains(plan, line => line.Contains("json_each", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PathLookup_UsesAnIndex()
    {
        _recorder.Clear();
        _ = await new MarkdownFileRepository(_dbContext)
            .GetByAbsolutePathAsync(@"C:\notizen\a.md", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        IReadOnlyList<string> plan = await ExplainRecordedAsync().ConfigureAwait(true);

        Assert.False(ScansATable(plan), "Die Pfadsuche liest die ganze Tabelle: " + string.Join(" | ", plan));
    }

    /// <remarks>
    /// Der Abgleich liest je Wurzel alle Dateien darunter. <c>StartsWith</c> wird zu einem
    /// <c>LIKE</c> auf der NOCASE-Spalte; nur mit passender Sortierfolge darf SQLite dafür den
    /// Index nehmen. Ändert jemand Spalte oder Übersetzung, liest der Abgleich wieder alles.
    /// </remarks>
    [Fact]
    public async Task RootListing_UsesAnIndex()
    {
        _recorder.Clear();
        _ = await new MarkdownFileRepository(_dbContext)
            .GetAllUnderRootAsync(@"C:\notizen", TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        IReadOnlyList<string> plan = await ExplainRecordedAsync().ConfigureAwait(true);

        Assert.False(ScansATable(plan), "Die Wurzelabfrage liest die ganze Tabelle: " + string.Join(" | ", plan));
    }

    [Fact]
    public async Task IdLookup_UsesAnIndex()
    {
        _recorder.Clear();
        _ = await new MarkdownFileRepository(_dbContext)
            .GetByIdAsync(FirstFileId, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        IReadOnlyList<string> plan = await ExplainRecordedAsync().ConfigureAwait(true);

        Assert.False(ScansATable(plan), "Die Suche nach der Kennung liest die ganze Tabelle: " + string.Join(" | ", plan));
    }

    [Fact]
    public async Task GraphFiles_LoadInOneQueryRegardlessOfTheStock()
    {
        await SeedFilesAsync(30).ConfigureAwait(true);
        _recorder.Clear();

        IReadOnlyList<GraphSourceFile> files = await new GraphSourceProvider(_dbContext)
            .LoadFilesAsync(TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        Assert.Equal(30, files.Count);
        _ = Assert.Single(_recorder.Commands);
    }

    [Fact]
    public async Task FileTagReplacement_LoadsTheLinksOfAWholeBatchInOneQuery()
    {
        // Der Parser ersetzt die Zuordnungen eines ganzen Stapels. Eine Abfrage je Datei wäre bei
        // einem Erstlauf über tausende Dateien tausendfach dieselbe Rundreise.
        await SeedFilesAsync(30).ConfigureAwait(true);
        Dictionary<Guid, IReadOnlyCollection<Guid>> tagIdsByFile = (await _dbContext.Set<MarkdownFile>()
            .Select(file => file.Id)
            .ToListAsync(TestContext.Current.CancellationToken).ConfigureAwait(true))
            .ToDictionary(id => id, _ => (IReadOnlyCollection<Guid>)[]);
        _recorder.Clear();

        await new TagRepository(_dbContext)
            .ReplaceFileTagsAsync(tagIdsByFile, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = Assert.Single(_recorder.Commands);
    }

    [Fact]
    public async Task TopTags_AreOneQuery()
    {
        await SeedFilesAsync(30).ConfigureAwait(true);
        _recorder.Clear();

        _ = await new TagStatisticsQuery(_dbContext)
            .GetTopTagsAsync(50, TestContext.Current.CancellationToken)
            .ConfigureAwait(true);

        _ = Assert.Single(_recorder.Commands);
    }

    // Die Gegenprobe: Ohne sie stünden die Wächter auch dann grün, wenn die Erkennung nie
    // anschlüge. Eine Abfrage auf eine Spalte ohne Index muss als Durchlauf gelten, eine
    // Volltextabfrage ohne MATCH nicht als Indexzugriff, und drei Aufrufe zählen als drei Abfragen.
    [Fact]
    public async Task TheDetection_FlagsAScanAndAMissingMatch()
    {
        IReadOnlyList<string> scan = await ExplainAsync(
            "SELECT \"Id\" FROM \"MarkdownFiles\" WHERE \"ContentHash\" = $hash",
            ("$hash", "x")).ConfigureAwait(true);
        IReadOnlyList<string> withoutMatch = await ExplainAsync(
            "SELECT \"MarkdownFileId\" FROM \"MarkdownSearchIndex\" WHERE \"Path\" = $path",
            ("$path", "a.md")).ConfigureAwait(true);

        Assert.True(ScansATable(scan), string.Join(" | ", scan));
        Assert.False(UsesFtsMatch(withoutMatch), string.Join(" | ", withoutMatch));
    }

    [Fact]
    public async Task TheRecorder_CountsEveryQuery()
    {
        MarkdownFileRepository repository = new(_dbContext);
        _recorder.Clear();

        for (int i = 0; i < 3; i++)
        {
            _ = await repository.GetByIdAsync(FirstFileId, TestContext.Current.CancellationToken).ConfigureAwait(true);
        }

        Assert.Equal(3, _recorder.Commands.Count);
    }

    // "SCAN <Tabelle>" ohne Index heißt: jede Zeile lesen. Ein Zugriff über Index oder
    // Primärschlüssel heißt "SEARCH … USING …". Geprüft wird jede SCAN-Zeile, nicht der
    // Tabellenname: EF Core spricht die Tabelle über einen Alias an ("SCAN m").
    private static bool ScansATable(IReadOnlyList<string> plan) =>
        plan.Any(line => line.StartsWith("SCAN ", StringComparison.Ordinal)
            && !line.Contains(" USING ", StringComparison.Ordinal)
            && !line.Contains("VIRTUAL TABLE", StringComparison.Ordinal));

    // FTS5 meldet einen MATCH-Zugriff als virtuellen Index, dessen Beschreibung ein "M" trägt;
    // ohne MATCH liest auch die Volltexttabelle alles.
    private static bool UsesFtsMatch(IReadOnlyList<string> plan) =>
        plan.Any(line => line.Contains("MarkdownSearchIndex VIRTUAL TABLE INDEX", StringComparison.Ordinal)
            && line[(line.LastIndexOf(':') + 1)..].Contains('M', StringComparison.Ordinal));

    private async Task<IReadOnlyList<string>> ExplainRecordedAsync()
    {
        RecordedCommand command = Assert.Single(_recorder.Commands);
        return await ExplainAsync(command.Text, [.. command.Parameters]).ConfigureAwait(true);
    }

    [SuppressMessage(
        "Security",
        "CA2100:Review SQL queries for security vulnerabilities",
        Justification = "Der Text ist eine Konstante des Produktivcodes oder eine von EF Core erzeugte und aufgezeichnete Abfrage; im Test gibt es keine Nutzereingabe. Ohne zusammengesetzten Text ließe sich der Plan einer erzeugten Abfrage nicht erfragen.")]
    private async Task<IReadOnlyList<string>> ExplainAsync(string sql, params (string Name, object Value)[] parameters)
    {
        using SqliteCommand command = _connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN " + sql;
        foreach ((string name, object value) in parameters)
        {
            _ = command.Parameters.AddWithValue(name, value);
        }

        List<string> plan = [];
        using SqliteDataReader reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        while (await reader.ReadAsync(TestContext.Current.CancellationToken).ConfigureAwait(true))
        {
            plan.Add(reader.GetString(3));
        }

        return plan;
    }

    private async Task SeedFilesAsync(int count)
    {
        for (int i = 0; i < count; i++)
        {
            _ = _dbContext.Set<MarkdownFile>().Add(new MarkdownFile
            {
                Id = new Guid(i + 1, 0, 0, new byte[8]),
                AbsolutePath = $@"C:\notizen\datei-{i}.md",
                RelativePath = $"datei-{i}.md",
                FileNameWithoutExtension = $"datei-{i}",
                ContentHash = "h",
            });
        }

        _ = await _dbContext.SaveChangesAsync(TestContext.Current.CancellationToken).ConfigureAwait(true);
        _dbContext.ChangeTracker.Clear();
    }

    // Eine aufgezeichnete Abfrage mit ihren Parametern, so wie EF Core sie an SQLite schickt.
    private sealed record RecordedCommand(string Text, IReadOnlyList<(string Name, object Value)> Parameters);

    // Hält jede Abfrage fest, die EF Core ausführt, samt Parametern, damit ihr Plan sich mit
    // denselben Werten erfragen lässt.
    private sealed class CommandRecorder : DbCommandInterceptor
    {
        private readonly List<RecordedCommand> _commands = [];

        public List<RecordedCommand> Commands => _commands;

        public void Clear() => _commands.Clear();

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(command);
            _commands.Add(new RecordedCommand(
                command.CommandText,
                [.. command.Parameters.Cast<DbParameter>().Select(parameter => (parameter.ParameterName, parameter.Value ?? DBNull.Value))]));
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}

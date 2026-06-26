using Xunit;

// SQLite-Datei-I/O und Performance-Messungen werden bei paralleler Ausführung instabil —
// daher Test-Parallelisierung auf Assembly-Ebene abschalten.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

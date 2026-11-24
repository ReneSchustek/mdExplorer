using Xunit.Sdk;
using Xunit.v3;

// SQLite-Datei-I/O und Performance-Messungen werden bei paralleler Ausführung instabil,
// daher Test-Parallelisierung auf Assembly-Ebene abschalten.
[assembly: Parallelization(Mode = ParallelMode.None)]

# tools/

Portable Werkzeuge die NICHT als NuGet-Pakete kommen.

## gitleaks

Secret-Scanner (nutzt Shannon-Entropie zur Erkennung hochentroper Strings).
Binary ist via `*.exe` gitignored — vor dem ersten Lauf installieren:

```powershell
powershell tools/install-gitleaks.ps1
```

Lokaler Aufruf:

```powershell
tools/gitleaks.exe detect --source . --config .gitleaks.toml            # Arbeitsbaum + Historie
tools/gitleaks.exe detect --source . --config .gitleaks.toml --no-git   # nur Arbeitsbaum
```

CI nutzt stattdessen die `gitleaks-action` (siehe `.github/workflows/security.yml`),
braucht das lokale Binary nicht.

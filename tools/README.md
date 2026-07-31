# tools/

Portable Werkzeuge, die nicht als NuGet-Paket kommen. **Die Binaries werden hier
nicht eingecheckt** — sie sind über `tools/*.exe` ignoriert und werden lokal
installiert.

## gitleaks

Secret-Scanner; erkennt hochentrope Zeichenketten über die Shannon-Entropie.

Binary von <https://github.com/gitleaks/gitleaks/releases> herunterladen und als
`tools/gitleaks.exe` ablegen. Lokaler Aufruf:

```powershell
tools/gitleaks.exe detect --source . --config .gitleaks.toml            # Arbeitsbaum + Historie
tools/gitleaks.exe detect --source . --config .gitleaks.toml --no-git   # nur Arbeitsbaum
```

In der CI läuft der Scan über die offizielle `gitleaks-action`
(siehe `.github/workflows/security.yml`) — über die vollständige Historie und
zusätzlich nach Zeitplan. Das lokale Binary wird dafür nicht gebraucht.

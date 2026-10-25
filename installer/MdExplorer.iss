; Inno Setup 6 Skript für MdExplorer.
; Erzeugt eine Setup.exe aus dem self-contained win-x64 Publish-Output.
; Der Publish liefert eine einzige MdExplorer.exe (native Bibliotheken sind
; eingebettet), daher genügt es, genau diese Datei zu installieren.
;
; Aufruf (Schritte im README unter „Veröffentlichen und Installer“):
;   ISCC /DSourceDir="<publish-pfad>" /DMyAppVersion=<Inhalt von VERSION> installer\MdExplorer.iss

#define MyAppName "MdExplorer"
; Keine Rückfallversion: Ein Setup mit anderer Versionsnummer als die Programmdatei ließe die
; Update-Prüfung falsch vergleichen. Die Version kommt aus VERSION, beim Release aus dem Tag.
#ifndef MyAppVersion
  #error MyAppVersion fehlt: ISCC mit /DMyAppVersion=<Inhalt von VERSION> aufrufen.
#endif
#define MyAppPublisher "Rene Schustek"
#define MyAppURL "https://github.com/ReneSchustek/mdExplorer"
#define MyAppExeName "MdExplorer.exe"
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; AppId identifiziert das Programm für Updates/Deinstallation — stabil halten.
AppId={{B7E6F4C2-4E2A-4C9E-9B3F-2D1A8C5E0F77}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
OutputDir=..\dist
OutputBaseFilename=MdExplorer-{#MyAppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Ziel ist {autopf}, also "Programme"; dorthin schreibt nur ein Administrator.
PrivilegesRequired=admin
; Die Zuordnung zu .md und .markdown unten ändert, was der Explorer anbietet; ohne diese Angabe
; sähe er sie erst nach einer Neuanmeldung.
ChangesAssociations=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; Abwählbar, aber vorausgewählt — Inno merkt sich die Auswahl je AppId und übernimmt
; sie bei jedem weiteren Update.
[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

; Dateizuordnung. Die Wahl des Standardprogramms trifft unter Windows der Nutzer selbst, einmal
; über „Öffnen mit“ und „Immer“; ein Setup darf sie nicht erzwingen. Eingetragen wird deshalb nur,
; dass MdExplorer diese Dateien öffnen kann. HKA ist bei einer Installation mit Adminrechten
; HKLM, die Zuordnung gilt also für alle Benutzer des Rechners.
;
; Beim Deinstallieren gehen die eigenen Schlüssel ganz (uninsdeletekey). Unter .md und .markdown
; geht nur der eigene Wert (uninsdeletevalue): Diese Schlüssel teilen sich alle Programme, die
; Markdown öffnen.
[Registry]
Root: HKA; Subkey: "Software\Classes\MdExplorer.Markdown"; ValueType: string; ValueName: ""; ValueData: "Markdown-Dokument"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\MdExplorer.Markdown\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\MdExplorer.Markdown\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\.md\OpenWithProgids"; ValueType: string; ValueName: "MdExplorer.Markdown"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\.markdown\OpenWithProgids"; ValueType: string; ValueName: "MdExplorer.Markdown"; ValueData: ""; Flags: uninsdeletevalue
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}"; ValueType: string; ValueName: "FriendlyAppName"; ValueData: "{#MyAppName}"; Flags: uninsdeletekey
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\DefaultIcon"; ValueType: string; ValueName: ""; ValueData: "{app}\{#MyAppExeName},0"
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#MyAppExeName}"" ""%1"""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".md"; ValueData: ""
Root: HKA; Subkey: "Software\Classes\Applications\{#MyAppExeName}\SupportedTypes"; ValueType: string; ValueName: ".markdown"; ValueData: ""

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

; Inno Setup 6 Skript fuer MdExplorer.
; Erzeugt eine Setup.exe aus dem self-contained win-x64 Publish-Output.
; Der Publish liefert eine einzige MdExplorer.exe (native Bibliotheken sind
; eingebettet), daher genuegt es, genau diese Datei zu installieren.
;
; Build:  installer\build-installer.ps1   (publisht + kompiliert)
; Manuell: ISCC /DSourceDir="<publish-pfad>" /DMyAppVersion=0.9.0 installer\MdExplorer.iss

#define MyAppName "MdExplorer"
#ifndef MyAppVersion
  #define MyAppVersion "0.9.0"
#endif
#define MyAppPublisher "Rene Schustek"
#define MyAppURL "https://github.com/ReneSchustek/mdExplorer"
#define MyAppExeName "MdExplorer.exe"
#ifndef SourceDir
  #define SourceDir "..\publish"
#endif

[Setup]
; AppId identifiziert das Programm fuer Updates/Deinstallation — stabil halten.
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
PrivilegesRequired=admin

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

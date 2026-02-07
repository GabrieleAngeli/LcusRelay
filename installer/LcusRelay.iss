; Inno Setup script for LcusRelay

#define AppName "LcusRelay"
#define AppPublisher "LcusRelay"
#define AppExeName "LcusRelay.Tray.exe"

; These are overridden by /D switches in CI when available
#if !defined(AppVersion)
  #define AppVersion "1.0.0"
#endif
#if !defined(PublishDir)
  #define PublishDir "..\\artifacts\\publish"
#endif
#if !defined(OutputDir)
  #define OutputDir "..\\artifacts\\installer"
#endif
#if !defined(IconFile)
  #define IconFile "..\\src\\LcusRelay.Tray\\assets\\lcusrelay.ico"
#endif

[Setup]
AppId={{D7B40D80-AC7C-4F5D-9C1E-2E8C1A5F7B7A}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={pf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputBaseFilename={#AppName}-Setup
OutputDir={#OutputDir}
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#AppExeName}
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
WizardStyle=modern

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch {#AppName}"; Flags: nowait runhidden runasoriginaluser

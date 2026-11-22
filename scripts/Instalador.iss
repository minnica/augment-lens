#define AppExe "AumentosLoL.exe"
#define AppVersion GetVersionNumbersString(AddBackslash(PublishDir) + AppExe)

[Setup]
AppId=AumentosLoL
AppName=Aumentos LoL
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\AumentosLoL
DisableDirPage=yes
DisableProgramGroupPage=yes
DisableReadyPage=yes
PrivilegesRequired=lowest
SetupArchitecture=x64
ArchitecturesAllowed=x64os
MinVersion=10.0.19041
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes
RestartApplications=no
OutputBaseFilename=Instalar-AumentosLoL-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "inicioautomatico"; Description: "Iniciar Aumentos LoL en la bandeja al iniciar sesión en Windows"; GroupDescription: "Inicio de Windows"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,\x86\*"; Flags: ignoreversion recursesubdirs
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{userstartup}\Aumentos LoL.lnk"; Tasks: not inicioautomatico

[Icons]
Name: "{userprograms}\Aumentos LoL"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{userstartup}\Aumentos LoL"; Filename: "{app}\{#AppExe}"; Parameters: "--bandeja"; WorkingDir: "{app}"; Tasks: inicioautomatico

[Run]
Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Description: "Abrir Aumentos LoL"; Flags: nowait postinstall skipifsilent

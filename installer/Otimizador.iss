; Instalador do Otimizador (Inno Setup 6). Gere com: tools\build-release.ps1
; Antes: dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o publish-folder

#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif

[Setup]
; AppId fixo: é o que faz uma versão nova atualizar a anterior em vez de instalar uma segunda cópia
AppId={{B6C5F5A1-6E64-4E55-9C0D-5B2D7C9E1A11}
AppName=Otimizador
AppVersion={#AppVersion}
AppPublisher=Juan
DefaultDirName={autopf}\Otimizador
DefaultGroupName=Otimizador
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
OutputDir=..\dist
OutputBaseFilename=Otimizador-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\Otimizador.exe
SetupIconFile=..\Assets\Otimizador.ico
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
; driver da temperatura de CPUs AMD Ryzen (já vem dentro do instalador); só aparece se ainda não estiver instalado
Name: "pawnio"; Description: "Instalar o driver PawnIO (temperatura da CPU AMD Ryzen)"; GroupDescription: "Componentes:"; Check: not PawnIoInstalled

[Files]
Source: "..\publish-folder\*"; DestDir: "{app}"; Excludes: "portable.txt,Dados\*"; Flags: recursesubdirs ignoreversion
Source: "..\README.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\Otimizador"; Filename: "{app}\Otimizador.exe"
Name: "{autodesktop}\Otimizador"; Filename: "{app}\Otimizador.exe"; Tasks: desktopicon

[Run]
; driver PawnIO (instalador oficial, sem alteração). Ele pede a permissão de administrador sozinho.
Filename: "{app}\Drivers\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Instalando o driver PawnIO..."; Flags: waituntilterminated runhidden; Tasks: pawnio; Check: not PawnIoInstalled
; instalação normal: oferece abrir no fim
Filename: "{app}\Otimizador.exe"; Description: "{cm:LaunchProgram,Otimizador}"; Flags: nowait postinstall skipifsilent
; atualização automática (o app roda o instalador com /relaunch=1): abre de novo sozinho
Filename: "{app}\Otimizador.exe"; Flags: nowait; Check: ShouldRelaunch

[Code]
function PawnIoInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function ShouldRelaunch: Boolean;
begin
  Result := ExpandConstant('{param:relaunch|0}') = '1';
end;

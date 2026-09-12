; Camledian Drive installer.
;
; Expects the published, framework-dependent win-x64 build (the same output
; produced by `dotnet publish`, including the bundled tools\rclone.exe and
; dependencies\winfsp.msi) to already exist at the folder passed via
; /DSourceDir. Build it in CI with, for example:
;
;   iscc installer\CamledianDrive.iss /DAppVersion=1.2.3 /DSourceDir=artifacts\win-x64
;
; What this installer does beyond copying files:
;   - installs the .NET Desktop Runtime the app needs if it isn't present
;     (via winget first, falling back to Microsoft's official installer)
;   - installs WinFsp if it isn't present, from the copy already bundled
;     into the published output (no separate download at install time)

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\win-x64"
#endif

#define DotNetDesktopRuntimeMajor "10"

[Setup]
AppId={{ED0148B1-63BA-4110-A211-90E57BAC4104}
AppName=Camledian Drive
AppVersion={#AppVersion}
AppPublisher=Camledian
DefaultDirName={autopf}\Camledian Drive
DefaultGroupName=Camledian Drive
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\CamledianDrive.exe
SetupIconFile=..\src\CamledianDrive\Assets\ikona-tray.ico
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\artifacts\installer
OutputBaseFilename=CamledianDrive-Setup

[Languages]
Name: "cz"; MessagesFile: "compiler:Languages\Czech.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Camledian Drive"; Filename: "{app}\CamledianDrive.exe"
Name: "{autodesktop}\Camledian Drive"; Filename: "{app}\CamledianDrive.exe"; Tasks: desktopicon
Name: "{group}\{cm:UninstallProgram,Camledian Drive}"; Filename: "{uninstallexe}"

[Run]
Filename: "{app}\CamledianDrive.exe"; Description: "{cm:LaunchProgram,Camledian Drive}"; Flags: nowait postinstall skipifsilent

[Code]

function IsDotNetDesktopRuntimeInstalled(const MajorVersion: string): Boolean;
var
  ResultCode: Integer;
  OutputFile: string;
  Lines: TArrayOfString;
  I: Integer;
  NeedlePrefix: string;
begin
  Result := False;
  OutputFile := ExpandConstant('{tmp}\dotnet-runtimes.txt');
  if Exec(ExpandConstant('{cmd}'),
       '/C dotnet --list-runtimes > "' + OutputFile + '" 2>&1',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if LoadStringsFromFile(OutputFile, Lines) then
    begin
      NeedlePrefix := 'Microsoft.WindowsDesktop.App ' + MajorVersion + '.';
      for I := 0 to GetArrayLength(Lines) - 1 do
      begin
        if Pos(NeedlePrefix, Lines[I]) = 1 then
        begin
          Result := True;
          Break;
        end;
      end;
    end;
  end;
end;

function IsWinFspInstalled: Boolean;
begin
  Result :=
    RegKeyExists(HKLM, 'SOFTWARE\WOW6432Node\WinFsp') or
    RegKeyExists(HKLM, 'SOFTWARE\WinFsp') or
    FileExists(ExpandConstant('{commonpf64}\WinFsp\bin\winfsp-x64.dll')) or
    FileExists(ExpandConstant('{commonpf32}\WinFsp\bin\winfsp-x64.dll'));
end;

procedure InstallDotNetDesktopRuntime;
var
  ResultCode: Integer;
  InstallerPath: string;
  Downloaded: Int64;
begin
  // Prefer winget: it ships by default on Windows 10 2004+/11, needs no
  // hardcoded download link, and always resolves to the current release.
  if Exec(ExpandConstant('{cmd}'),
       '/C winget install --id Microsoft.DotNet.DesktopRuntime.' + '{#DotNetDesktopRuntimeMajor}' +
       ' -e --silent --accept-package-agreements --accept-source-agreements',
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Exit;

  // Fall back to Microsoft's official evergreen redirect for machines
  // without winget (older Windows images missing the App Installer).
  Downloaded := 0;
  try
    Downloaded := DownloadTemporaryFile(
      'https://aka.ms/dotnet/' + '{#DotNetDesktopRuntimeMajor}' + '.0/windowsdesktop-runtime-win-x64.exe',
      'windowsdesktop-runtime-win-x64.exe', '', nil);
  except
    Downloaded := 0;
  end;

  if Downloaded > 0 then
  begin
    InstallerPath := ExpandConstant('{tmp}\windowsdesktop-runtime-win-x64.exe');
    if not (Exec(InstallerPath, '/install /quiet /norestart', '', SW_HIDE,
         ewWaitUntilTerminated, ResultCode) and (ResultCode in [0, 3010])) then
    begin
      MsgBox('Automatická instalace .NET Desktop Runtime se nezdařila. ' +
        'Po dokončení instalace Camledian Drive si ho prosím doinstaluj ručně ' +
        'z https://dotnet.microsoft.com/download/dotnet a appku pak spusť znovu.',
        mbInformation, MB_OK);
    end;
  end
  else
  begin
    MsgBox('Nepodařilo se stáhnout .NET Desktop Runtime. ' +
      'Po dokončení instalace Camledian Drive si ho prosím doinstaluj ručně ' +
      'z https://dotnet.microsoft.com/download/dotnet a appku pak spusť znovu.',
      mbInformation, MB_OK);
  end;
end;

procedure InstallWinFsp;
var
  ResultCode: Integer;
  MsiPath: string;
begin
  // Installed alongside the app under {app}\dependencies - kept there
  // (rather than a temp-extracted copy) so Camledian Drive's own repair
  // path (DependencyService.InstallBundledWinFspAsync) keeps working if
  // this silent install is ever skipped by security software.
  MsiPath := ExpandConstant('{app}\dependencies\winfsp.msi');

  if not (Exec('msiexec.exe', '/i "' + MsiPath + '" /qn /norestart', '',
       SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode in [0, 3010, 1641])) then
  begin
    MsgBox('Instalace WinFsp se nezdařila (kód ' + IntToStr(ResultCode) + '). ' +
      'Camledian Drive to při prvním připojení zkusí nainstalovat znovu.',
      mbInformation, MB_OK);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Runs after the app files are already copied, so InstallWinFsp can use
  // the bundled msi directly from the install directory.
  if CurStep = ssPostInstall then
  begin
    if not IsDotNetDesktopRuntimeInstalled('{#DotNetDesktopRuntimeMajor}') then
      InstallDotNetDesktopRuntime;

    if not IsWinFspInstalled then
      InstallWinFsp;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM CamledianDrive.exe', '',
      SW_HIDE, ewWaitUntilTerminated, ResultCode);
  end;
end;

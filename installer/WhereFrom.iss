#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef ExtensionId
  #error ExtensionId is required
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifdef InstallerTest
  #define Identity "WhereFrom-InstallerTest"
  #define EnvironmentKey "Software\WhereFrom\InstallerTest\Environment"
  #define StateKey "Software\WhereFrom\InstallerTest\State"
  #define SetupName "WhereFrom-InstallerTest"
  #define ChromeHostKey "Software\WhereFrom\InstallerTest\ChromeNativeMessagingHosts\org.wherefrom.provenance"
  #define EdgeHostKey "Software\WhereFrom\InstallerTest\EdgeNativeMessagingHosts\org.wherefrom.provenance"
  #define ExplorerKey "Software\WhereFrom\InstallerTest\Classes\*\shell\WhereFrom"
#else
  #define Identity "WhereFrom"
  #define EnvironmentKey "Environment"
  #define StateKey "Software\WhereFrom\Installer"
  #define SetupName "WhereFrom-preview-win-x64-Setup"
  #define ChromeHostKey "Software\Google\Chrome\NativeMessagingHosts\org.wherefrom.provenance"
  #define EdgeHostKey "Software\Microsoft\Edge\NativeMessagingHosts\org.wherefrom.provenance"
  #define ExplorerKey "Software\Classes\*\shell\WhereFrom"
#endif

[Setup]
AppId={#Identity}
AppName=WhereFrom
AppVersion=0.1.1
AppPublisher=WhereFrom
AppPublisherURL=https://github.com/opsorart/WhereFrom
DefaultDirName={localappdata}\Programs\WhereFrom
DisableDirPage=auto
UsePreviousAppDir=yes
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDir}
OutputBaseFilename={#SetupName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
UninstallDisplayIcon={app}\GUI\WhereFrom.App.exe
LicenseFile={#PackageDir}\LICENSE
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\WhereFrom"; Filename: "{app}\GUI\WhereFrom.App.exe"
Name: "{autoprograms}\WhereFrom browser setup"; Filename: "{app}\BROWSER-SETUP.txt"

[Run]
Filename: "{sys}\notepad.exe"; Parameters: """{app}\BROWSER-SETUP.txt"""; Description: "View browser setup instructions"; Flags: postinstall skipifsilent nowait

[Messages]
FinishedLabel=WhereFrom is installed. Close all terminal windows and open a new terminal, then run wherefrom --version from any directory.

[Code]
const
  EnvKey = '{#EnvironmentKey}';
  StateKey = '{#StateKey}';
  ExplorerKey = '{#ExplorerKey}';

function RegOpenKeyEx(hKey: Integer; SubKey: String; Options, Access: LongWord; var Handle: Integer): Integer;
  external 'RegOpenKeyExW@advapi32.dll stdcall';
function RegQueryValueEx(Handle: Integer; Name: String; Reserved: Integer; var Kind: LongWord; Data: Integer; var Size: LongWord): Integer;
  external 'RegQueryValueExW@advapi32.dll stdcall';
function RegCloseKey(Handle: Integer): Integer;
  external 'RegCloseKey@advapi32.dll stdcall';
function ExpandEnvironmentStrings(Source, Destination: String; Size: LongWord): LongWord;
  external 'ExpandEnvironmentStringsW@kernel32.dll stdcall';

function Normalized(Value: String): String;
var Buffer: String; Count: LongWord;
begin
  Value := RemoveQuotes(Trim(Value));
  SetLength(Buffer, 32768);
  Count := ExpandEnvironmentStrings(Value, Buffer, 32768);
  if (Count > 0) and (Count <= 32768) then begin
    SetLength(Buffer, Count - 1);
    Value := Buffer;
  end;
  StringChangeEx(Value, '/', '\', True);
  while (Length(Value) > 3) and (Value[Length(Value)] = '\') do
    Delete(Value, Length(Value), 1);
  Result := Lowercase(Value);
end;

function HasEntry(Value, Entry: String): Boolean;
var Parts: TArrayOfString; I: Integer;
begin
  Result := False;
  Parts := StringSplit(Value, [';'], stAll);
  for I := 0 to GetArrayLength(Parts) - 1 do
    if Normalized(Parts[I]) = Normalized(Entry) then begin
      Result := True;
      Exit;
    end;
end;

function WithoutEntry(Value, Entry: String): String;
var Parts: TArrayOfString; I: Integer; First: Boolean;
begin
  Result := '';
  First := True;
  Parts := StringSplit(Value, [';'], stAll);
  for I := 0 to GetArrayLength(Parts) - 1 do
    if Normalized(Parts[I]) <> Normalized(Entry) then begin
      if not First then Result := Result + ';';
      Result := Result + Parts[I];
      First := False;
    end;
end;

function PathKind: LongWord;
var Handle, Code: Integer; Size, Kind: LongWord;
begin
  Result := 2;
  if not RegValueExists(HKCU, EnvKey, 'Path') then Exit;
  if RegOpenKeyEx(HKCU, EnvKey, 0, 1, Handle) <> 0 then
    RaiseException('Unable to read the user PATH.');
  try
    Size := 0;
    Code := RegQueryValueEx(Handle, 'Path', 0, Kind, 0, Size);
    if Code <> 0 then RaiseException('Unable to read the user PATH type.');
    if (Kind <> 1) and (Kind <> 2) then
      RaiseException('The user PATH is not a string. It has not been changed.');
    Result := Kind;
  finally
    RegCloseKey(Handle);
  end;
end;

procedure WritePath(Value: String; Kind: LongWord);
var OK: Boolean;
begin
  if Kind = 1 then OK := RegWriteStringValue(HKCU, EnvKey, 'Path', Value)
  else OK := RegWriteExpandStringValue(HKCU, EnvKey, 'Path', Value);
  if not OK then RaiseException('Unable to update the user PATH.');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Kind: LongWord;
begin
  Result := '';
  if Pos(';', ExpandConstant('{app}')) > 0 then begin
    Result := 'The installation directory cannot contain a semicolon.';
    Exit;
  end;
  try
    Kind := PathKind;
  except
    Result := GetExceptionMessage;
  end;
end;

function HostKey(Browser: String): String;
begin
  if Browser = 'chrome' then Result := '{#ChromeHostKey}'
  else Result := '{#EdgeHostKey}';
end;

function HostManifestPath(Browser: String): String;
begin
  Result := ExpandConstant('{app}\NativeHost\native-host-' + Browser + '.json');
end;

function HostManifest: String;
var HostExe: String;
begin
  HostExe := ExpandConstant('{app}\NativeHost\WhereFrom.NativeHost.exe');
  StringChangeEx(HostExe, '\', '\\', True);
  Result := '{"name":"org.wherefrom.provenance","description":"WhereFrom browser download provenance",' +
    '"path":"' + HostExe + '","type":"stdio",' +
    '"allowed_origins":["chrome-extension://{#ExtensionId}/"]}';
end;

procedure RegisterHost(Browser: String);
var Key, ManifestPath, Existing, BackupName: String;
begin
  Key := HostKey(Browser);
  ManifestPath := HostManifestPath(Browser);
  BackupName := 'Previous' + Browser + 'Host';
  if not FileExists(ExpandConstant('{app}\NativeHost\WhereFrom.NativeHost.exe')) then
    RaiseException('Native Host executable is missing.');
  if RegQueryStringValue(HKCU, Key, '', Existing) and
     (Normalized(Existing) <> Normalized(ManifestPath)) and
     not RegValueExists(HKCU, StateKey, BackupName) then
    if not RegWriteStringValue(HKCU, StateKey, BackupName, Existing) then
      RaiseException('Unable to preserve the previous Native Host registration.');
  if not SaveStringToFile(ManifestPath, Utf8Encode(HostManifest), False) then
    RaiseException('Unable to write the Native Host manifest.');
  if not RegWriteStringValue(HKCU, Key, '', ManifestPath) then
    RaiseException('Unable to register the Native Host.');
end;

procedure UnregisterHost(Browser: String);
var Key, ManifestPath, Registered, Previous, BackupName: String;
    Contents: AnsiString;
begin
  Key := HostKey(Browser);
  ManifestPath := HostManifestPath(Browser);
  BackupName := 'Previous' + Browser + 'Host';
  if RegQueryStringValue(HKCU, Key, '', Registered) and
     (Normalized(Registered) = Normalized(ManifestPath)) then begin
    if RegQueryStringValue(HKCU, StateKey, BackupName, Previous) then begin
      if not RegWriteStringValue(HKCU, Key, '', Previous) then
        RaiseException('Unable to restore the previous Native Host registration.');
    end else begin
      RegDeleteValue(HKCU, Key, '');
      RegDeleteKeyIfEmpty(HKCU, Key);
    end;
  end;
  RegDeleteValue(HKCU, StateKey, BackupName);
  if LoadStringFromFile(ManifestPath, Contents) and
     (Contents = Utf8Encode(HostManifest)) then
    DeleteFile(ManifestPath);
end;

function ExplorerCommand: String;
begin
  Result := '"' + ExpandConstant('{app}\GUI\WhereFrom.App.exe') + '" "%1"';
end;

procedure RegisterExplorer;
var Command, Existing, Owned: String;
begin
  Command := ExplorerCommand;
  { Do not take over an existing user-defined menu. }
  if RegKeyExists(HKCU, ExplorerKey) then begin
    if not RegQueryStringValue(HKCU, StateKey, 'ExplorerCommand', Owned) or
       not RegQueryStringValue(HKCU, ExplorerKey + '\command', '', Existing) or
       (Existing <> Owned) then Exit;
  end;
  if not FileExists(ExpandConstant('{app}\GUI\WhereFrom.App.exe')) then
    RaiseException('GUI executable is missing.');
  if not RegWriteStringValue(HKCU, StateKey, 'ExplorerCommand', Command) or
     not RegWriteStringValue(HKCU, ExplorerKey, '', '查看文件来源 (WhereFrom)') or
     not RegWriteStringValue(HKCU, ExplorerKey, 'Icon',
       ExpandConstant('{app}\GUI\WhereFrom.App.exe')) or
     not RegWriteStringValue(HKCU, ExplorerKey, 'MultiSelectModel', 'Single') or
     not RegWriteStringValue(HKCU, ExplorerKey + '\command', '', Command) then
    RaiseException('Unable to register the Explorer menu.');
end;

procedure UnregisterExplorer;
var Owned, Existing: String;
begin
  if not RegQueryStringValue(HKCU, StateKey, 'ExplorerCommand', Owned) then Exit;
  if RegQueryStringValue(HKCU, ExplorerKey + '\command', '', Existing) and
     (Existing = Owned) then begin
    RegDeleteValue(HKCU, ExplorerKey + '\command', '');
    RegDeleteKeyIfEmpty(HKCU, ExplorerKey + '\command');
    if RegQueryStringValue(HKCU, ExplorerKey, '', Existing) and
       (Existing = '查看文件来源 (WhereFrom)') then
      RegDeleteValue(HKCU, ExplorerKey, '');
    if RegQueryStringValue(HKCU, ExplorerKey, 'Icon', Existing) and
       (Existing = ExpandConstant('{app}\GUI\WhereFrom.App.exe')) then
      RegDeleteValue(HKCU, ExplorerKey, 'Icon');
    if RegQueryStringValue(HKCU, ExplorerKey, 'MultiSelectModel', Existing) and
       (Existing = 'Single') then
      RegDeleteValue(HKCU, ExplorerKey, 'MultiSelectModel');
    RegDeleteKeyIfEmpty(HKCU, ExplorerKey);
  end;
  RegDeleteValue(HKCU, StateKey, 'ExplorerCommand');
end;
procedure CurStepChanged(Step: TSetupStep);
var Value, Entry: String; Kind: LongWord;
begin
  if Step <> ssPostInstall then Exit;
  Entry := ExpandConstant('{app}');
  Kind := PathKind;
  Value := '';
  if RegValueExists(HKCU, EnvKey, 'Path') and
     not RegQueryStringValue(HKCU, EnvKey, 'Path', Value) then
    RaiseException('Unable to read the user PATH.');
  if not HasEntry(Value, Entry) then begin
    { Ownership survives reinstall; a pre-existing entry is never claimed. }
    if not RegWriteStringValue(HKCU, StateKey, 'AddedPath', Entry) then
      RaiseException('Unable to record PATH ownership.');
    if Value = '' then Value := Entry
    else Value := Value + ';' + Entry;
    WritePath(Value, Kind);
  end;
  RegisterHost('chrome');
  RegisterHost('edge');
  RegisterExplorer;
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var Value, Entry: String; Kind: LongWord;
begin
  if Step <> usUninstall then Exit;
  UnregisterHost('chrome');
  UnregisterHost('edge');
  UnregisterExplorer;
  if RegQueryStringValue(HKCU, StateKey, 'AddedPath', Entry) then begin
    Kind := PathKind;
    if RegValueExists(HKCU, EnvKey, 'Path') then begin
      if not RegQueryStringValue(HKCU, EnvKey, 'Path', Value) then
        RaiseException('Unable to read the user PATH.');
      WritePath(WithoutEntry(Value, Entry), Kind);
    end;
  end;
  RegDeleteValue(HKCU, StateKey, 'AddedPath');
  RegDeleteKeyIfEmpty(HKCU, StateKey);
end;

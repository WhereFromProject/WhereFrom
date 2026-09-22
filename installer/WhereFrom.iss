#ifndef PackageDir
  #error PackageDir is required
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif
#ifdef InstallerTest
  #define Identity "WhereFrom-InstallerTest"
  #define EnvironmentKey "Software\WhereFrom\InstallerTest\Environment"
  #define StateKey "Software\WhereFrom\InstallerTest\State"
  #define SetupName "WhereFrom-InstallerTest"
#else
  #define Identity "WhereFrom"
  #define EnvironmentKey "Environment"
  #define StateKey "Software\WhereFrom\Installer"
  #define SetupName "WhereFrom-0.1.0-win-x64-Setup"
#endif

[Setup]
AppId={#Identity}
AppName=WhereFrom
AppVersion=0.1.0
AppPublisher=WhereFrom
AppPublisherURL=https://github.com/strategist0/WhereFrom
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
UninstallDisplayIcon={app}\wherefrom.exe
LicenseFile={#PackageDir}\LICENSE
CloseApplications=yes
RestartApplications=no

[Files]
Source: "{#PackageDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Messages]
FinishedLabel=WhereFrom is installed. Close all terminal windows and open a new terminal, then run wherefrom --version from any directory.

[Code]
const
  EnvKey = '{#EnvironmentKey}';
  StateKey = '{#StateKey}';

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
end;

procedure CurUninstallStepChanged(Step: TUninstallStep);
var Value, Entry: String; Kind: LongWord;
begin
  if Step <> usUninstall then Exit;
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

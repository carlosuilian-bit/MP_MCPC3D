; =============================================================================
; Civil 3D MCP Bridge — Inno Setup Installer
; =============================================================================
; Installs:
;   1. .NET plugin bundle → Autodesk ApplicationPlugins (Autoloader)
;   2. Python MCP server  → Program Files\Civil3dMcp\
;   3. Optional: Claude Desktop config snippet
;
; BUILD THIS INSTALLER:
;   1. Install Inno Setup 6 from https://jrsoftware.org/isinfo.php
;   2. Place compiled files in the source dirs (see [Files] section)
;   3. Open this .iss in Inno Setup Compiler → Build → Compile
;   4. Output: installer\Output\Civil3dMcpBridge-Setup.exe
;
; PRE-BUILT DEPENDENCY NOTES:
;   - The .NET plugin (Civil3dMcpBridge.dll) must be compiled first
;     using build.bat or Visual Studio
;   - Python must be installed on the target machine (3.10+)
;   - The installer will pip install fastmcp and httpx
; =============================================================================

#define MyAppName "Civil 3D MCP Bridge"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "Kevin"
#define MyAppURL "https://github.com/youraccount/civil3d-mcp"

[Setup]
AppId={{B7E3F2A1-8C4D-4F5E-9A6B-1D2E3F4A5B6C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf}\Civil3dMcp
DefaultGroupName={#MyAppName}
; No start menu entries needed — this is a background service
AllowNoIcons=yes
; Output location
OutputDir=Output
OutputBaseFilename=Civil3dMcpBridge-Setup-{#MyAppVersion}
; Require admin for writing to AppData\Autodesk and Program Files
PrivilegesRequired=admin
; Compression
Compression=lzma2
SolidCompression=yes
; Modern look
WizardStyle=modern
; License (optional — create a LICENSE.txt or remove this line)
; LicenseFile=..\LICENSE.txt
; Minimum Windows version (Win 10+)
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Types]
Name: "full"; Description: "Full installation (plugin + MCP server)"
Name: "plugin_only"; Description: "Plugin only (I'll set up the MCP server myself)"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "plugin"; Description: "Civil 3D .NET Plugin (Autoloader bundle)"; Types: full plugin_only custom; Flags: fixed
Name: "mcpserver"; Description: "Python MCP Server"; Types: full custom
Name: "mcpserver\pipdeps"; Description: "Install Python dependencies (fastmcp, httpx)"; Types: full custom
Name: "claudeconfig"; Description: "Configure Claude Desktop (adds MCP server entry)"; Types: full custom

; =============================================================================
; DETECT CIVIL 3D VERSIONS — find the ApplicationPlugins folder
; =============================================================================

[Code]
var
  Civil3dVersion: String;
  PluginDestDir: String;
  PythonPath: String;
  ClaudeConfigPath: String;

function FindPython(): String;
var
  PythonPaths: array of String;
  I: Integer;
begin
  Result := '';
  // Check common Python locations
  SetArrayLength(PythonPaths, 4);
  PythonPaths[0] := ExpandConstant('{localappdata}\Programs\Python\Python312\python.exe');
  PythonPaths[1] := ExpandConstant('{localappdata}\Programs\Python\Python311\python.exe');
  PythonPaths[2] := ExpandConstant('{localappdata}\Programs\Python\Python310\python.exe');
  PythonPaths[3] := 'C:\Python312\python.exe';

  for I := 0 to GetArrayLength(PythonPaths) - 1 do
  begin
    if FileExists(PythonPaths[I]) then
    begin
      Result := PythonPaths[I];
      Exit;
    end;
  end;

  // Try PATH
  if Result = '' then
  begin
    if FileExists(ExpandConstant('{sys}\python.exe')) then
      Result := 'python.exe'
    else if FileExists(ExpandConstant('{sys}\python3.exe')) then
      Result := 'python3.exe';
  end;
end;

function GetAutoloaderDir(): String;
var
  BaseDir: String;
begin
  // Autodesk's standard ApplicationPlugins directory
  BaseDir := ExpandConstant('{commonappdata}\Autodesk\ApplicationPlugins');
  Result := BaseDir + '\Civil3dMcpBridge.bundle';
end;

function GetClaudeConfigDir(): String;
begin
  Result := ExpandConstant('{localappdata}\Claude');
end;

function InitializeSetup(): Boolean;
begin
  // Find Python
  PythonPath := FindPython();
  PluginDestDir := GetAutoloaderDir();
  ClaudeConfigPath := GetClaudeConfigDir();
  Result := True;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ConfigFile: String;
  ConfigContent: AnsiString;
  McpServerPath: String;
  NewEntry: String;
  ResultCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    // --- Install pip dependencies ---
    if IsComponentSelected('mcpserver\pipdeps') and (PythonPath <> '') then
    begin
      Log('Installing Python dependencies...');
      Exec(PythonPath, '-m pip install fastmcp httpx --quiet',
           '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
      if ResultCode <> 0 then
        Log('Warning: pip install returned code ' + IntToStr(ResultCode));
    end;

    // --- Configure Claude Desktop ---
    if IsComponentSelected('claudeconfig') then
    begin
      ConfigFile := ClaudeConfigPath + '\claude_desktop_config.json';
      McpServerPath := ExpandConstant('{app}\mcp-server\server.py');

      // Replace backslashes with forward slashes for JSON
      StringChangeEx(McpServerPath, '\', '/', True);

      if FileExists(ConfigFile) then
      begin
        Log('Claude Desktop config exists — please add the MCP entry manually.');
        // We don't want to corrupt an existing config, so we write a helper file
        NewEntry :=
          '{' + #13#10 +
          '  "mcpServers": {' + #13#10 +
          '    "civil3d": {' + #13#10 +
          '      "command": "' + PythonPath + '",' + #13#10 +
          '      "args": ["' + McpServerPath + '"],' + #13#10 +
          '      "env": {' + #13#10 +
          '        "CAD_BRIDGE_URL": "http://localhost:8270",' + #13#10 +
          '        "CAD_APP_NAME": "Civil3D"' + #13#10 +
          '      }' + #13#10 +
          '    }' + #13#10 +
          '  }' + #13#10 +
          '}';
        SaveStringToFile(ExpandConstant('{app}\claude_config_snippet.json'),
                         NewEntry, False);
      end
      else
      begin
        // No existing config — create fresh
        ForceDirectories(ClaudeConfigPath);
        NewEntry :=
          '{' + #13#10 +
          '  "mcpServers": {' + #13#10 +
          '    "civil3d": {' + #13#10 +
          '      "command": "' + PythonPath + '",' + #13#10 +
          '      "args": ["' + McpServerPath + '"],' + #13#10 +
          '      "env": {' + #13#10 +
          '        "CAD_BRIDGE_URL": "http://localhost:8270",' + #13#10 +
          '        "CAD_APP_NAME": "Civil3D"' + #13#10 +
          '      }' + #13#10 +
          '    }' + #13#10 +
          '  }' + #13#10 +
          '}';
        SaveStringToFile(ConfigFile, NewEntry, False);
        Log('Created Claude Desktop config at ' + ConfigFile);
      end;
    end;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = wpSelectComponents then
  begin
    // Warn if Python not found but MCP server selected
    if IsComponentSelected('mcpserver') and (PythonPath = '') then
    begin
      if MsgBox('Python 3.10+ was not detected on this system.' + #13#10 +
                'The MCP server requires Python to run.' + #13#10 + #13#10 +
                'Continue anyway? (You can install Python later)',
                mbConfirmation, MB_YESNO) = IDNO then
        Result := False;
    end;
  end;
end;

// Show post-install instructions
procedure CurPageChanged(CurPageID: Integer);
begin
  if CurPageID = wpFinished then
  begin
    WizardForm.FinishedLabel.Caption :=
      'Civil 3D MCP Bridge has been installed.' + #13#10 + #13#10 +
      'To get started:' + #13#10 +
      '  1. Open Civil 3D' + #13#10 +
      '  2. Type MCPSTART at the command line' + #13#10 +
      '  3. Open Claude Desktop — the civil3d tools should appear' + #13#10 + #13#10 +
      'The plugin loads automatically with Civil 3D via the Autoloader.';
  end;
end;

[/Code]

; =============================================================================
; FILES — what gets installed where
; =============================================================================

[Files]
; --- .NET Plugin Bundle (Autoloader) ---
; Destination: %ProgramData%\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle\
Source: "..\plugin\bin\Release\Civil3dMcpBridge.dll"; \
  DestDir: "{commonappdata}\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle\Contents"; \
  Flags: ignoreversion; Components: plugin

Source: "..\plugin\bin\Release\Newtonsoft.Json.dll"; \
  DestDir: "{commonappdata}\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle\Contents"; \
  Flags: ignoreversion; Components: plugin

Source: "PackageContents.xml"; \
  DestDir: "{commonappdata}\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle"; \
  Flags: ignoreversion; Components: plugin

; --- Python MCP Server ---
Source: "..\mcp-server\server.py"; \
  DestDir: "{app}\mcp-server"; \
  Flags: ignoreversion; Components: mcpserver

Source: "..\mcp-server\requirements.txt"; \
  DestDir: "{app}\mcp-server"; \
  Flags: ignoreversion; Components: mcpserver

; --- Documentation ---
Source: "..\docs\ARCHITECTURE.md"; \
  DestDir: "{app}\docs"; \
  Flags: ignoreversion

Source: "..\docs\MICROSURVEY-ADAPTATION.md"; \
  DestDir: "{app}\docs"; \
  Flags: ignoreversion

; --- Uninstall helper — removes Autoloader bundle on uninstall ---

[UninstallDelete]
Type: filesandordirs; Name: "{commonappdata}\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle"

; =============================================================================
; REGISTRY — optional: mark as installed for detection
; =============================================================================

[Registry]
Root: HKCU; Subkey: "Software\Civil3dMcp"; ValueType: string; \
  ValueName: "InstallDir"; ValueData: "{app}"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Civil3dMcp"; ValueType: string; \
  ValueName: "Version"; ValueData: "{#MyAppVersion}"
Root: HKCU; Subkey: "Software\Civil3dMcp"; ValueType: string; \
  ValueName: "PluginDir"; \
  ValueData: "{commonappdata}\Autodesk\ApplicationPlugins\Civil3dMcpBridge.bundle"

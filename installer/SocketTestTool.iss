; SYN/ACK - Socket Test Tool 윈도우 설치 파일 스크립트 (Inno Setup 6)
;
; 만드는 법 (반드시 이 순서):
;   1) dotnet publish -p:PublishProfile=FolderProfile
;   2) "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" installer\SocketTestTool.iss
;
; 결과물: installer\dist\SYNACK_Setup_v<버전>.exe
;
; .NET 런타임을 따로 깔 필요가 없습니다. FolderProfile.pubxml이 자체 포함
; 단일 파일(SelfContained + PublishSingleFile)로 내보내기 때문에, 런타임이
; exe 안에 들어 있습니다. 그래서 [Files]에 넣는 것도 exe 하나뿐입니다.

#define AppExe "..\bin\Release\net8.0-windows\publish\win-x64\SocketTestTool.exe"

; 버전은 여기 한 곳만 고칩니다. csproj의 <Version>과 같아야 합니다.
#define AppVersion "2.2.0"

; 빌드된 exe와 위 버전이 어긋나면 여기서 멈춥니다.
; (publish를 다시 하지 않고 설치 파일만 만드는 실수를 막습니다)
#if !FileExists(AppExe)
  #error 배포본이 없습니다. 먼저 실행하세요: dotnet publish -p:PublishProfile=FolderProfile
#endif
#if GetVersionNumbersString(AppExe) != AppVersion + ".0"
  #error 버전 불일치 - iss는 AppVersion, exe는 GetVersionNumbersString(AppExe) 입니다. publish를 다시 하세요.
#endif

; 검증용으로 만들 때는 ISCC에 /DTEST 를 주세요.
;
;   "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DTEST installer\SocketTestTool.iss
;
; AppId가 달라져서 실제 설치본과 완전히 분리됩니다. 이게 없으면, 무인 설치/제거로
; 설치 파일을 검증하는 순간 **같은 AppId를 쓰는 실제 설치본의 등록 정보와 시작 메뉴
; 바로가기가 함께 지워집니다.** (실제로 겪은 사고입니다. QA-HISTORY.md 결함 #17)
#ifdef TEST
  #define SetupAppId "{{7A3E1C42-9B55-4D18-AE30-2F6C8D1B4E97}"
  #define SetupAppName "SYNACK - Socket Test Tool (TEST BUILD)"
  #define SetupDirName "SYNACK Socket Test Tool TEST"
  #define SetupOutBase "SYNACK_TESTONLY_v" + AppVersion
#else
  #define SetupAppId "{{C620F4D8-682E-474A-A372-47F157833633}"
  #define SetupAppName "SYNACK - Socket Test Tool"
  #define SetupDirName "SYNACK Socket Test Tool"
  #define SetupOutBase "SYNACK_Setup_v" + AppVersion
#endif

[Setup]
; 배포용 AppId는 절대 바꾸지 마세요. 이 값이 같아야 새 버전이 기존 설치를 덮어씁니다.
AppId={#SetupAppId}
AppName={#SetupAppName}
AppVersion={#AppVersion}
AppVerName={#SetupAppName} {#AppVersion}
AppPublisher=J2S
AppPublisherURL=https://github.com/jjscan/synack-socket-test-tool
AppSupportURL=https://github.com/jjscan/synack-socket-test-tool/issues
AppUpdatesURL=https://github.com/jjscan/synack-socket-test-tool/releases
VersionInfoVersion={#AppVersion}

; 앱 자체가 requireAdministrator(app.manifest)라 설치도 관리자로 받습니다.
PrivilegesRequired=admin

; win-x64 자체 포함 빌드라 64비트에서만 설치됩니다.
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

DefaultDirName={autopf64}\{#SetupDirName}
DefaultGroupName={#SetupDirName}
DisableProgramGroupPage=yes
UninstallDisplayName={#SetupAppName}
UninstallDisplayIcon={app}\SocketTestTool.exe

OutputBaseFilename={#SetupOutBase}
OutputDir=dist
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

SetupIconFile=..\Assets\app.ico
InfoBeforeFile=..\Assets\LICENSE_MIT_en_ko.rtf

[Languages]
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"

[Files]
; .pdb는 넣지 않습니다. 실행에 필요 없고 크기만 늘립니다.
Source: "{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#SetupAppName}"; Filename: "{app}\SocketTestTool.exe"
Name: "{autodesktop}\{#SetupAppName}"; Filename: "{app}\SocketTestTool.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "바탕 화면에 아이콘 만들기"; GroupDescription: "추가 아이콘:"

[Run]
; skipifsilent이 없으면 /SILENT·/VERYSILENT 무인 설치에서도 앱이 떠 버립니다.
; 그러면 곧바로 이어서 제거할 때 실행 중인 exe를 지우지 못해 156 MB가 남습니다.
Filename: "{app}\SocketTestTool.exe"; Description: "프로그램 실행"; Flags: nowait postinstall shellexec skipifsilent
Filename: "{app}"; Description: "설치 폴더 열기"; Flags: postinstall shellexec unchecked skipifsilent

[UninstallDelete]
; 앱이 실행 위치에 만드는 파일들입니다. 지우지 않으면 빈 폴더가 남습니다.
Type: files; Name: "{app}\theme.json"
Type: files; Name: "{app}\recent-sessions.json"
Type: filesandordirs; Name: "{app}\Logs"

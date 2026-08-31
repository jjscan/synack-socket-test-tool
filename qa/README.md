# 검증 하네스 (qa)

앱을 **실제로 띄우고 실제 소켓을 열어** 확인하는 검증 도구입니다.
모킹이나 서비스 단위 테스트가 아니라, 실제 `MainWindow` / `MainViewModel`을 그대로 쓰고
사용자가 누르는 것과 같은 커맨드를 실행합니다.

| 프로젝트 | 내용 | 검사 수 | 소요 |
| --- | --- | ---: | ---: |
| `FunctionalQa` | 앱이 제공하는 모든 기능 (17개 구간) | 199 | 약 50초 |
| `StressTest` | 부하와 자원 한계 (12개 구간) | 60 | 약 125초 |

## 실행 방법

**본체를 Debug로 먼저 빌드해야 합니다.** 하네스는 빌드된 DLL을 참조합니다.

```bash
dotnet build
```

```bash
dotnet run --project qa/FunctionalQa
```

```bash
dotnet run --project qa/StressTest
```

결과는 화면이 아니라 파일로 남습니다. 종료 코드는 실패가 없으면 `0`입니다.

- `%TEMP%\fullqa-result.txt` — 기능 QA 결과
- `%TEMP%\stress-result.txt` — 부하 테스트 결과 (구간마다 갱신되므로 도중에 멈춰도 남습니다)
- `%TEMP%\fullqa-*.png` — 화면 캡처 (빈 상태, 라이트/다크 테마)

## 설치 파일을 시험 설치할 때

**반드시 `/DTEST` 로 만든 설치 파일을 쓰세요.**

```bash
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" /DTEST installer\SocketTestTool.iss
```

배포용 설치 파일로 시험 설치·제거를 하면, 같은 AppId를 쓰기 때문에 **이 PC에 깔려 있는 실제 설치본의
등록 정보와 시작 메뉴 바로가기가 함께 지워집니다.** 실제로 겪은 사고입니다 — [../QA-HISTORY.md](../QA-HISTORY.md) 결함 #17.

## 알아 둘 것

- **창이 실제로 뜹니다.** 화면 캡처와 메뉴 팝업 검증 때문에 화면 위에 올라옵니다. 실행 중에는 마우스·키보드를 건드리지 마세요.
- **실제 포트를 씁니다.** 127.0.0.1의 동적 포트를 쓰며, `StressTest`는 최대 12,000개 연결을 엽니다.
- **`StressTest`에는 자동 중단 한계선이 있습니다.** `Infra.cs` 위쪽 상수로 노출돼 있습니다.

  | 상수 | 기본값 | 의미 |
  | --- | ---: | --- |
  | `AbortWhenSystemFreeBelowMb` | 4096 | 시스템 여유 메모리가 이보다 적어지면 중단 |
  | `AbortWhenProcessMemoryAboveMb` | 4096 | 프로세스 메모리 상한 |
  | `AbortWhenHandlesAbove` | 50000 | 핸들 상한 |
  | `AbortWhenThreadsAbove` | 3000 | 스레드 상한 |
  | `AbortWhenUiStalledMs` | 5000 | UI가 이만큼 응답 없으면 멈춘 것으로 간주 |
  | `MaxConnectionsToTry` | 6000 | 연결쌍 절대 상한 |

  더 강하게 밀어붙이려면 이 값들을 올리면 됩니다. 다만 TCP 포트 범위(약 16,384개)를 소진하면
  **이 PC의 다른 프로그램 인터넷 연결도 잠시 실패**합니다.

## 왜 ProjectReference가 아니라 DLL 참조인가

본체가 `<SelfContained>true</SelfContained>`라서, 자체 포함이 아닌 실행 파일에서 `ProjectReference`로 참조하면
`NETSDK1150` 오류가 납니다. 그래서 빌드 산출물을 직접 참조합니다.

```xml
<Reference Include="SocketTestTool">
  <HintPath>..\..\bin\Debug\net8.0-windows\win-x64\SocketTestTool.dll</HintPath>
</Reference>
```

경로에 `win-x64`가 들어가는 것에 주의하세요. `<SelfContained>` 때문에 산출물이 RID 폴더 하위로 갑니다.
상위 폴더에는 오래된 바이너리가 남아 있을 수 있습니다.

## 본체 빌드에서 제외돼 있습니다

.NET SDK는 하위 폴더의 `.cs`를 전부 끌어옵니다. 제외하지 않으면 하네스의 `Main`이 함께 컴파일되어
본체 빌드가 깨집니다. `SocketTestTool.csproj`에 다음이 들어 있습니다.

```xml
<Compile Remove="qa\**" />
<Page Remove="qa\**" />
<None Remove="qa\**" />
<EmbeddedResource Remove="qa\**" />
```

## 하네스를 고칠 때

검사를 추가하거나 고치기 전에 **[../QA-HISTORY.md](../QA-HISTORY.md) 7장(하네스 함정)** 을 읽으세요.
로그 단언에 `WaitUntil`이 필요한 이유, CPU 측정 창을 초기화하면 안 되는 이유,
모달 대화상자를 리플렉션으로 다루는 방법 등 실제로 시간을 잡아먹었던 것들이 정리돼 있습니다.

# AGENTS.md

이 저장소를 처음 만지는 사람(또는 AI 에이전트)이 **먼저 읽어야 할 것**을 모아 둔 안내입니다.

## 이 프로젝트

**SYN/ACK - Socket Test Tool** — TCP 서버·클라이언트를 여러 개 만들어 동시에 구동하는 Windows 데스크톱 통신 테스트 도구.
WPF / .NET 8 (`net8.0-windows`), MVVM을 프레임워크 없이 직접 구현.

- git 저장소가 아니고 CI도 없습니다. 검증은 `qa/` 하네스로 합니다(아래 "검증" 절).
- UI 문구와 코드 주석은 **한국어**, 런타임 로그·상태 문자열은 **영어**입니다. 이 구분을 유지하세요.

## 반드시 먼저 읽을 문서

| 문서 | 내용 | 언제 필요한가 |
| --- | --- | --- |
| [README.md](README.md) | 이 도구가 무엇이고 어떻게 쓰는지 (사용자용) | 앱의 기능을 파악할 때 |
| **[QA-HISTORY.md](QA-HISTORY.md)** | 검증 이력, 발견해 고친 결함 9건과 그 이유, **고치면 안 되는 의도된 동작**, 하네스 함정 | 코드를 고치기 전 / 버그처럼 보이는 동작을 만났을 때 |
| **[VERSIONING.md](VERSIONING.md)** | 버전 자리를 올리는 기준과 실제 적용 사례, 버전을 고쳐야 하는 세 곳 | 릴리스할 때 |

> QA-HISTORY.md의 **4장(고치면 안 되는 동작)** 을 건너뛰지 마세요.
> 주기 전송의 15.6 ms 하한, 표시용 문자열 4 KB 상한, Windows 10에서 동작하지 않는 다크 제목 표시줄 등
> **버그로 오해하기 쉬운 의도된 동작**이 정리돼 있습니다.

## 빌드와 실행

```bash
dotnet build
```

- **산출물은 `bin\Debug\net8.0-windows\win-x64\` 하위입니다.** (`<SelfContained>true</SelfContained>` 때문)
  상위 폴더에는 오래된 바이너리가 남아 있어, 그것을 실행하면 변경사항이 반영되지 않은 채 정상처럼 보입니다.
- 정상 기준선은 **오류 0개 / 경고 0개**입니다. 경고가 하나라도 생기면 회귀로 보고 고칩니다.
  nullable 경고는 `NoWarn`으로 덮지 않고 **주석(`?`)과 초기화로 실제 해결**해 두었습니다. 같은 방식으로 유지하세요.
- `app.manifest`가 `requireAdministrator`라 **관리자 권한으로만 실행**됩니다. 디버깅하려면 IDE도 관리자로 띄워야 합니다.

## 변경할 때의 규칙

- **소켓 처리는 UI 스레드 밖에서.** `Task.Run` + 모든 await에 `ConfigureAwait(false)` + 상태 갱신은 `Dispatcher.BeginInvoke`.
  이 규칙을 어기면 앱이 '응답 없음'으로 멈춥니다. (QA-HISTORY 5장 #1)
- **서비스에서 UI를 띄우지 마세요.** 실패는 이벤트로 올리고 ViewModel이 인라인 배너로 표시합니다. `MessageBox.Show`를 서비스에 되돌리지 마세요.
- **색은 `Themes/Light.xaml` / `Themes/Dark.xaml`에 같은 키로 양쪽 모두** 추가하고, 스타일에서는 반드시 `DynamicResource`로 참조하세요. `StaticResource`로 참조하면 테마를 바꿔도 그 부분만 이전 색으로 남습니다.
- **릴리스 노트는 데이터입니다.** `Models/ReleaseNote.cs`의 `ReleaseHistory.All`에 추가하세요. 마크업이 아닙니다.
- 새 `.cs` / `.xaml`은 **UTF-8 with BOM**으로 작성하세요.
- XAML의 XML 주석 안에는 `--`를 쓸 수 없습니다(빌드 오류 MC3000). 구분선은 `=`로.

## 검증

기능 QA 225개 / 부하 테스트 60개를 통과한 상태입니다(v2.2.0).
소켓·ViewModel·테마를 건드렸다면 손으로 확인하지 말고 **저장소에 들어 있는 하네스를 돌리세요.**

```bash
dotnet build
dotnet run --project qa/FunctionalQa   # 기능 225개, 약 55초
dotnet run --project qa/StressTest     # 부하 60개, 약 125초
```

자세한 내용은 [qa/README.md](qa/README.md).
빌드가 통과하는 것은 UI에 대해 거의 아무것도 보장하지 않습니다 — XAML 오류는 창을 실제로 띄울 때만 드러납니다.

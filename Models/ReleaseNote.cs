using System.Collections.Generic;

namespace SocketTestTool.Models
{
    /// <summary>
    /// 릴리스 노트의 항목 한 줄입니다. 제목과 설명으로 이루어집니다.
    /// </summary>
    public class ReleaseNoteItem
    {
        public string Title { get; set; }
        public string Description { get; set; }

        public ReleaseNoteItem(string title, string description)
        {
            Title = title;
            Description = description;
        }
    }

    /// <summary>
    /// 한 버전의 릴리스 노트 전체입니다.
    /// 기존에는 RichTextBox의 FlowDocument에 직접 적혀 있던 내용을 데이터로 옮긴 것입니다. (목업 1g)
    /// </summary>
    public class ReleaseNote
    {
        public string Version { get; set; } = "";
        public string ReleaseDate { get; set; } = "";
        public string? Tagline { get; set; }
        public bool IsCurrent { get; set; }

        public List<ReleaseNoteItem> Features { get; set; } = new List<ReleaseNoteItem>();
        public List<ReleaseNoteItem> Improvements { get; set; } = new List<ReleaseNoteItem>();
        public List<ReleaseNoteItem> BugFixes { get; set; } = new List<ReleaseNoteItem>();

        #region Display Helpers

        /// <summary>목록 왼쪽에 보여 줄 부제입니다. (예: "2025-10-15 · latest")</summary>
        public string ListSubtitle => IsCurrent ? $"{ReleaseDate} · latest" : (Tagline ?? ReleaseDate);

        public int FeatureCount => Features.Count;
        public int ImprovementCount => Improvements.Count;
        public int BugFixCount => BugFixes.Count;

        public bool HasFeatures => Features.Count > 0;
        public bool HasImprovements => Improvements.Count > 0;
        public bool HasBugFixes => BugFixes.Count > 0;

        #endregion
    }

    /// <summary>
    /// 앱의 전체 릴리스 기록입니다. 최신 버전이 맨 앞에 옵니다.
    /// </summary>
    public static class ReleaseHistory
    {
        public static IReadOnlyList<ReleaseNote> All { get; } = new List<ReleaseNote>
        {
            // 자리를 올리는 기준은 VERSIONING.md를 따릅니다.
            // v2.0.1은 재설계 이후에 나온 '원래 되어야 했던 것'들만 고친 패치입니다.
            new ReleaseNote
            {
                Version = "v2.2.0",
                ReleaseDate = "2026-08-31",
                IsCurrent = true,
                Tagline = "클라이언트가 먼저 말 걸기",
                Improvements =
                {
                    new ReleaseNoteItem("클라이언트가 접속하면 먼저 보낼 수 있습니다",
                        "연결 추가 창의 '접속하면 먼저 보내기'를 켜고 보낼 데이터를 넣으면, 서버에 붙자마자 그 내용을 한 번 보냅니다. 자동 응답과 따로 동작하므로 '접속 → 로그인 전문 → 서버 응답 → 클라이언트 회신' 같은 대화를 통째로 흉내낼 수 있습니다.")
                },
                BugFixes =
                {
                    new ReleaseNoteItem("클라이언트 자동 응답 설정이 저장되지 않던 문제",
                        "v2.1.0에서 클라이언트의 자동 응답과 규칙을 설정해도 실제 연결에 반영되지 않아 동작하지 않았습니다. 설정한 값이 그대로 적용됩니다.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.1.0",
                ReleaseDate = "2026-08-31",
                Tagline = "클라이언트 자동 응답",
                Improvements =
                {
                    new ReleaseNoteItem("클라이언트도 받으면 자동으로 회신할 수 있습니다",
                        "지금까지 자동 응답은 서버에서만 됐습니다. 이제 클라이언트도 상대가 보내오면 정해진 값으로 회신하거나, 받은 내용에 따라 다른 값으로 회신할 수 있습니다. 연결 추가 창의 '자동 응답'에서 켭니다. 주기 전송과 함께 써도 됩니다."),
                    new ReleaseNoteItem("클라이언트도 쪼개져 오는 전문을 합쳐서 받습니다",
                        "'수신 대기' 시간을 주면 그 시간만큼 조용해질 때까지 기다렸다가 한 건으로 합칩니다. 0이면 지금까지처럼 받는 대로 처리하므로, 예전에 만든 연결은 동작이 그대로입니다.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.0.5",
                ReleaseDate = "2026-08-30",
                Tagline = "사용성 수정",
                BugFixes =
                {
                    new ReleaseNoteItem("저장할 연결이 없을 때 빈 세션이 저장되던 문제",
                        "연결을 하나도 만들지 않은 상태에서 'Save Session'을 누르면 내용이 없는 파일이 만들어졌습니다. 이제 연결이 없으면 메뉴가 비활성으로 표시됩니다."),
                    new ReleaseNoteItem("연결 확인 결과의 긴 문구가 잘리던 문제",
                        "포트를 점유한 프로세스 이름이 길면 뒷부분이 화면 밖으로 잘려 보이지 않았습니다. 이제 상자 안에서 줄이 바뀌어 전부 보입니다. 안내 문구의 중복된 표현도 함께 다듬었습니다.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.0.4",
                ReleaseDate = "2026-08-26",
                Tagline = "보안 강화",
                BugFixes =
                {
                    new ReleaseNoteItem("자동 전달 대기 큐 메모리 상한 추가",
                        "전달 대상이 꺼져 있을 때 대기 큐가 '건수(1,000건)'로만 제한돼, 큰 데이터가 계속 들어오면 수 GB를 붙들 수 있던 문제 수정. 총 용량(64 MB) 상한을 함께 적용해 오래된 것부터 버립니다."),
                    new ReleaseNoteItem("동시 접속 수 제한",
                        "데이터 없이 접속만 반복하는 공격으로 자원이 고갈될 수 있던 문제 수정. 서버당 동시 접속을 512개로 제한하고, 초과분은 해당 접속만 거부하며 서버는 계속 동작합니다."),
                    new ReleaseNoteItem("연결 삭제 시 통계 메모리 누수",
                        "연결을 지우거나 세션을 바꿔도 처리량 계산용 내부 데이터가 남아 조금씩 쌓이던 문제 수정.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.0.3",
                ReleaseDate = "2026-08-26",
                Tagline = "보안 강화",
                BugFixes =
                {
                    new ReleaseNoteItem("악의적 무한 스트림에 의한 메모리 고갈 차단",
                        "쉬지 않고 데이터를 보내는 클라이언트가 서버의 수신 누적을 무한히 키워 프로그램을 메모리 부족으로 죽일 수 있던 문제 수정. 한 프레임 누적에 16 MB 상한을 두고, 로그가 보관하는 원본 바이트도 표시 상한(4 KB)까지만 남기도록 변경. 자동 전달로 나가는 데이터는 잘리기 전 원본 전량 그대로입니다.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.0.2",
                ReleaseDate = "2026-08-26",
                Tagline = "보안 강화",
                BugFixes =
                {
                    new ReleaseNoteItem("로그 경로 보안 강화",
                        "신뢰할 수 없는 세션 파일이 시스템·시작프로그램 등 보호된 위치에 로그 파일을 만들 수 있던 문제 차단. 관리자 권한과 결합된 자동실행 지속성·권한 상승 경로를 막았습니다. 사용자 폴더로의 로그 저장은 그대로 동작합니다."),
                    new ReleaseNoteItem("탐색기 열기 인자 처리 강화",
                        "'폴더 열기' 동작에서 파일 경로를 문자열로 이어 붙이지 않고 인자 목록으로 전달하도록 변경.")
                }
            },

            new ReleaseNote
            {
                Version = "v2.0.1",
                ReleaseDate = "2026-08-26",
                BugFixes =
                {
                    new ReleaseNoteItem("다크 모드에서 메뉴가 보이지 않던 문제",
                        "메뉴·컨텍스트 메뉴·툴팁 팝업이 시스템 기본 흰 배경으로 그려져, 다크 테마의 밝은 글자와 겹쳐 읽을 수 없던 문제 수정. 팝업 표면도 테마 색을 따르도록 스타일 추가."),
                    new ReleaseNoteItem("중지할 때 오류로 표시되던 문제",
                        "서버를 중지하면 대기 중이던 Accept가 예외로 깨어나는데, 이를 시작 실패로 오인해 상태가 Error로 남고 가짜 '포트 사용 중' 배너가 뜨던 문제 수정."),
                    new ReleaseNoteItem("연속 시작 시 가짜 포트 충돌",
                        "소켓이 열리기 전에 시작 요청이 한 번 더 들어오면 같은 포트에 두 번 바인딩을 시도하던 문제를, 시작 즉시 'Starting' 상태로 표시해 방지."),
                    new ReleaseNoteItem("Hex 문자열 끝 공백",
                        "16진수 변환 결과 끝에 불필요한 공백이 붙던 문제 수정."),
                    new ReleaseNoteItem("대용량 메시지 수신 시 멈춤",
                        "1 MB급 메시지 한 건에 화면이 10초 이상 멈추고 메모리가 800 MB까지 치솟던 문제 수정. 원본 바이트는 그대로 보관하되 화면·로그 표시는 앞 4 KB까지만 만들고, 수신 버퍼 복사를 바이트 단위 열거에서 배열 복사로 교체.")
                }
            },

            // 화면 구성과 조작 흐름이 바뀌어 기존 사용자가 다시 익혀야 하므로 MAJOR입니다.
            // 함께 들어온 새 기능들은 가장 높은 자리 하나에 흡수했습니다.
            new ReleaseNote
            {
                Version = "v2.0.0",
                ReleaseDate = "2026-08-25",
                Tagline = "UI/UX 전면 재설계",
                Features =
                {
                    new ReleaseNoteItem("수신 데이터 자동 전달",
                        "연결에서 수신한 원본 바이트를 지정한 다른 소켓 서버로 자동 중계. 대상이 꺼져 있어도 최대 1,000건을 큐에 보관했다가 재접속 시 순서대로 재전송."),
                    new ReleaseNoteItem("Windows 11 Fluent UI",
                        "연결 목록을 카드와 상태 필로, 로그를 시간·방향·본문·길이 표로 재구성. 빈 상태 화면과 인라인 알림 배너 추가."),
                    new ReleaseNoteItem("처리량 지표",
                        "연결별 초당 메시지 수(msg/s)와 누적 메시지 수를 실시간 표시. 검색 시 일치 개수를 '일치/전체' 형태로 표시."),
                    new ReleaseNoteItem("최근 세션",
                        "최근에 저장하거나 불러온 세션 파일을 기억해 빈 상태 화면에서 바로 열 수 있음."),
                    new ReleaseNoteItem("다크 테마",
                        "보기 > 테마에서 라이트/다크를 고르거나 Windows 앱 모드 설정을 따르도록 설정. 선택은 다음 실행에도 유지되며, 전환은 다시 시작 없이 즉시 반영됨.")
                },
                Improvements =
                {
                    new ReleaseNoteItem("서버 기본 주소",
                        "서버 생성 시 IP 기본값을 0.0.0.0으로 변경하여, 별도 수정 없이 모든 네트워크 인터페이스에서 접속을 받도록 개선."),
                    new ReleaseNoteItem("기본 인코딩",
                        "서버 및 클라이언트 생성 시 기본 인코딩을 EUC-KR에서 ASCII로 변경."),
                    new ReleaseNoteItem("연결 추가 대화상자",
                        "응답 패턴을 라디오 카드로, 규칙을 표 형태로 재구성. 서버/클라이언트를 대화상자 안에서 전환 가능. Check 결과를 창 안에 인라인으로 표시하고 ICMP 응답 시간을 함께 보여 줌."),
                    new ReleaseNoteItem("소스 인코딩 통일",
                        "CP949로 저장돼 주석이 깨져 있던 소스 파일 2개를 UTF-8로 변환.")
                },
                BugFixes =
                {
                    new ReleaseNoteItem("UI 멈춤(응답 없음) 해결",
                        "소켓 송수신 처리가 UI 스레드에서 실행되던 구조를 바로잡아, 모든 await 재개 지점을 백그라운드 스레드로 분리."),
                    new ReleaseNoteItem("Check 버튼 멈춤",
                        "연결 생성 창의 포트 확인이 UI 스레드에서 동기 실행되어 창 전체가 멈추던 문제 수정."),
                    new ReleaseNoteItem("Edit 충돌 시 소켓 누수",
                        "Edit 중 IP/Port 충돌로 취소될 때 실행 중이던 리스너가 회수되지 않고 포트를 계속 점유하던 문제 수정."),
                    new ReleaseNoteItem("전달 중 데이터 1건 유실",
                        "대상 서버가 끊기는 순간의 1건이 전송된 것으로 오인되어 큐에서 사라지던 문제를, 쓰기 전 상대 종료 감지로 해결."),
                    new ReleaseNoteItem("모달 대화상자 제거",
                        "포트 점유·접속 실패를 MessageBox 대신 창 안의 배너로 알리고, 재시도·포트 변경·점유 프로세스 확인 동작을 함께 제공.")
                }
            },

            new ReleaseNote
            {
                Version = "v1.0.1",
                ReleaseDate = "2025-10-15",
                Features =
                {
                    new ReleaseNoteItem("다중 소켓 관리",
                        "여러 개의 TCP 서버와 클라이언트 연결을 생성하고 목록으로 동시 관리."),
                    new ReleaseNoteItem("서버 응답 패턴",
                        "Echo · Send Once on Connect · Reply After Receive · Listen Only(수동 응답)."),
                    new ReleaseNoteItem("규칙 기반 응답",
                        "서버가 특정 문자열을 수신했을 때, 미리 정의된 규칙에 따라 지정된 메시지를 자동으로 응답."),
                    new ReleaseNoteItem("주기적 전송",
                        "지정된 시간 간격(ms)으로 데이터를 자동으로 반복 전송."),
                    new ReleaseNoteItem("실시간 파일 로깅",
                        "각 소켓의 통신 내용을 별도의 로그 파일로 실시간 저장. 사용자 지정 경로 및 ON/OFF 옵션 지원."),
                    new ReleaseNoteItem("ASCII 제어 문자 지원",
                        "[STX], [ETX] 등 텍스트 태그를 실제 제어 바이트로 변환하여 전송."),
                    new ReleaseNoteItem("세션 관리",
                        "현재의 모든 연결 설정을 .json 파일로 저장하고 다시 불러오는 기능.")
                },
                Improvements =
                {
                    new ReleaseNoteItem("MVVM 패턴 리팩토링",
                        "코드 비하인드 로직을 MainViewModel로 이전하여 View와 ViewModel을 명확히 분리."),
                    new ReleaseNoteItem("폴더 구조 정리",
                        "Views, ViewModels, Models, Services, Common 폴더로 프로젝트 구조 체계화."),
                    new ReleaseNoteItem("다중 선택 및 일괄 제어",
                        "Ctrl·Shift 다중 선택과 Start/Stop/Remove 일괄 처리."),
                    new ReleaseNoteItem("동적 버튼 활성화",
                        "선택된 소켓의 상태에 따라 관련 버튼이 자동으로 활성화·비활성화."),
                    new ReleaseNoteItem("로그 뷰 개선",
                        "Sent·Received 색상 구분, 기호 보기·Hex 보기, 자동 스크롤, 필터링 및 검색."),
                    new ReleaseNoteItem("상태 표시줄",
                        "전체 연결 수 및 활성화된 서버·클라이언트 수를 실시간 표시."),
                    new ReleaseNoteItem("실시간 바이트 카운터",
                        "입력창의 텍스트 길이를 선택된 인코딩 기준으로 실시간 계산."),
                    new ReleaseNoteItem("연결별 인코딩 설정",
                        "각 소켓이 독립적인 인코딩 설정을 갖도록 수정."),
                    new ReleaseNoteItem("연결 사전 체크",
                        "ICMP Ping과 TCP Ping으로 대상 호스트의 연결 가능성을 3색 신호등으로 사전 진단."),
                    new ReleaseNoteItem("서버 생성 충돌 방지",
                        "동일한 IP·포트 서버의 중복 생성을 막고, 외부 프로세스와의 포트 충돌도 사전 감지."),
                    new ReleaseNoteItem("고속 통신 안정화",
                        "대량 수신 시 UI 병목과 메모리 급증을 생산자-소비자 패턴(ConcurrentQueue + DispatcherTimer)으로 해결."),
                    new ReleaseNoteItem("순환 버퍼",
                        "UI에 표시되는 로그를 1,000개로 제한하여 메모리 사용량 최적화.")
                },
                BugFixes =
                {
                    new ReleaseNoteItem("다중 선택 문제 해결",
                        "Attached Behavior를 도입하여 ListView의 SelectedItems를 ViewModel과 안정적으로 동기화."),
                    new ReleaseNoteItem("ContextMenu 바인딩 오류",
                        "ContextMenu의 독립된 스코프 문제로 발생하던 ElementName 바인딩 오류를 PlacementTarget으로 해결."),
                    new ReleaseNoteItem("Style 내 이벤트 연결",
                        "EventSetter를 사용하여 Style 안에서 이벤트 핸들러를 올바르게 연결."),
                    new ReleaseNoteItem("로그 옵션 오동작",
                        "다중 스레드 환경에서 로그 저장 옵션이 다른 소켓에 영향을 주던 클로저 문제를 핸들러 분리로 해결."),
                    new ReleaseNoteItem("서버 옵션 오동작",
                        "UI 텍스트 변경 시 응답 패턴이 항상 Echo로 동작하던 문제를 Tag 속성 기반 로직으로 해결."),
                    new ReleaseNoteItem("Edit 기능 안정화",
                        "Edit 후 소켓의 이전 상태가 유지되지 않거나 중복 포트 설정이 가능했던 문제 수정."),
                    new ReleaseNoteItem("주기적 전송 상태 초기화",
                        "연결이 끊기거나 Edit 시 Stop Periodic 버튼 상태가 초기화되지 않던 문제 수정."),
                    new ReleaseNoteItem("소켓 시작 예외 처리",
                        "서버 시작 시 SocketException을 상세히 분기하여 종료 대신 명확한 오류 메시지를 표시."),
                    new ReleaseNoteItem("정상 중단 예외 처리",
                        "정상적인 소켓 중단 시 발생하는 OperationAborted를 오류로 처리하지 않도록 수정."),
                    new ReleaseNoteItem("한글 깨짐 문제",
                        "각 연결의 인코딩 설정에 맞춰 디코딩하도록 수정하여 로그 창의 텍스트 깨짐 해결.")
                }
            },

            new ReleaseNote
            {
                Version = "v1.0.0",
                ReleaseDate = "2025-10-05",
                Tagline = "initial release",
                Features =
                {
                    new ReleaseNoteItem("최초 릴리스",
                        "TCP 서버와 클라이언트를 만들어 데이터를 주고받는 기본 기능.")
                }
            }
        };
    }
}

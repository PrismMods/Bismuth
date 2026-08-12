using System.Collections.Generic;
using UnityEngine;

namespace Bismuth
{
    /* Panel localization. English IS the key, so an incomplete table ships fine — anything
       without an entry renders as the English it always was, and the widget factories in
       UIBuilder do the lookup, so page code never mentions localization at all.

       Two things this also fixes, rather than fights:
       - The game runs a source-text pass that rewrites on-screen text whose ENGLISH value
         matches a localization entry (it turned Bismuth's "Misc" tab into 기타). Localized
         text no longer matches those English sources, so the pass stops touching it, and
         TabLabelGuard now pins the localized string instead of the English one.
       - Search indexes the English alongside the localized label, so both still find a
         setting regardless of the panel's language.

       KEYS MUST MATCH THE RUNTIME STRING EXACTLY, including "\n" and any concatenation —
       three help texts below are built from several source literals and are keyed on the
       joined result. */
    internal static class Loc
    {
        /* Panel language. Defaults to following the game (LanguageChangePatch rebuilds the
           panel when that changes), but can be pinned — a Korean player may still want the
           mod's English terms, and vice versa. */
        internal static SystemLanguage Current
        {
            get
            {
                int pinned = MainClass.Settings?.PanelLanguage ?? 0;
                if (pinned == 1) return SystemLanguage.English;
                if (pinned == 2) return SystemLanguage.Korean;
                try { return RDString.language; } catch { return SystemLanguage.English; }
            }
        }

        internal static string T(string english)
        {
            if (string.IsNullOrEmpty(english)) return english;
            var table = TableFor(Current);
            return table != null && table.TryGetValue(english, out string s) ? s : english;
        }

        private static Dictionary<string, string> TableFor(SystemLanguage lang)
            => lang == SystemLanguage.Korean ? Korean : null;

        /* Korean. Terms follow ADOFAI Korean community usage (판정 for judgements, 키 뷰어 for
           the key viewer); 기타 for Misc matches the game's own table. Grouped by where each
           string appears, for review. */
        private static readonly Dictionary<string, string> Korean = new Dictionary<string, string>
        {
            // ── Tabs ────────────────────────────────────────────────────────
            { "Overlay",                            "오버레이" },
            { "Key Viewer",                         "키 뷰어" },
            { "Game UI",                            "게임 UI" },
            { "Appearance",                         "환경설정" },
            { "Input",                              "입력" },
            { "Misc",                               "기타" },

            // ── Overlay tab ─────────────────────────────────────────────────
            { "Stats",                              "통계" },
            { "Timing",                             "타이밍" },
            { "Level Info",                         "레벨 정보" },
            { "Display",                            "표시" },
            { "Color",                              "색상" },
            { "Main",                               "기본" },
            { "Label",                              "라벨" },
            { "Count",                              "카운트" },
            { "Pulse animation",                    "펄스 애니메이션" },
            { "Progress",                           "진행도" },
            { "Progress Bar",                       "진행 바" },
            { "Accuracy",                           "정확도" },
            { "X-Accuracy",                         "X 정확도" },
            { "BPM",                                "BPM" },
            { "Tile BPM",                           "타일 BPM" },
            { "KPS",                                "KPS" },
            { "FPS",                                "FPS" },
            { "Best %",                             "최고 %" },
            { "Attempts",                           "시도 횟수" },
            { "Duration",                           "길이" },
            { "Song Duration",                      "곡 길이" },
            { "Level Duration",                     "레벨 길이" },
            { "Timing Scale",                       "타이밍 스케일" },
            { "Combo Display",                      "콤보 표시" },
            { "Overlay scale",                      "오버레이 크기" },
            { "Row spacing",                        "행 간격" },
            { "Decimal places",                     "소수점 자리" },
            { "Gradient max",                       "그라디언트 최대값" },
            { "Text shadow",                        "텍스트 그림자" },
            { "Shadow X",                           "그림자 X" },
            { "Shadow Y",                           "그림자 Y" },
            { "Shadow color",                       "그림자 색상" },
            { "Y offset",                           "Y 오프셋" },
            { "Show attempts",                      "시도 횟수 표시" },
            { "Show full attempts",                 "전체 시도 횟수 표시" },
            { "Show in attempts block",             "시도 횟수 칸에 표시" },
            { "Count autoplay tiles",               "자동플레이 타일 포함" },
            { "Use colors from Accuracy",           "정확도 색상 사용" },
            { "Use colors from BPM",                "BPM 색상 사용" },
            { "Use colors from Tile BPM",           "타일 BPM 색상 사용" },
            { "Apply master font to all overlays",  "모든 오버레이에 기본 글꼴 적용" },
            { "Reset current level",                "현재 레벨 초기화" },
            { "Reset all levels",                   "모든 레벨 초기화" },
            { "Click a card to show or hide that stat in game\n(highlighted = shown).\nClick the ··· button on a card for its settings:\nlabel, position, and colors.",
              "카드를 눌러 해당 통계를 게임에 표시하거나 숨깁니다\n(강조 표시 = 표시 중).\n카드의 ··· 버튼을 누르면 라벨, 위치, 색상\n설정을 열 수 있습니다." },
            { "Click a card to show or hide that element in game\n(highlighted = shown).\nClick the ··· button on a card for its settings.",
              "카드를 눌러 해당 요소를 게임에 표시하거나 숨깁니다\n(강조 표시 = 표시 중).\n카드의 ··· 버튼을 누르면 설정을 열 수 있습니다." },

            // ── Overlay tab › Positions ─────────────────────────────────────
            { "Positions",                          "위치" },
            { "Edit positions on screen",           "화면에서 위치 편집" },
            { "Reset all positions",                "모든 위치 초기화" },
            { "Drag elements directly on screen to adjust positions.",
              "화면에서 요소를 직접 끌어 위치를 조정합니다." },

            // ── Key Viewer tab ──────────────────────────────────────────────
            { "Hand",                               "손" },
            { "Foot",                               "발" },
            { "Rows",                               "행" },
            { "Row",                                "행" },
            { "Key",                                "키" },
            { "Background",                         "배경" },
            { "Border",                             "테두리" },
            { "Label Text",                         "라벨 텍스트" },
            { "Count Text",                         "카운트 텍스트" },
            { "Key Rain",                           "키 레인" },
            { "Ghost Keys",                         "고스트 키" },
            { "Enable",                             "사용" },
            { "Enabled",                            "사용" },
            { "Edit",                               "편집" },
            { "Visible",                            "표시" },
            { "Hide in level editor",               "레벨 에디터에서 숨기기" },
            { "Hide in main menu",                  "메인 메뉴에서 숨기기" },
            { "Key width",                          "키 너비" },
            { "Radius",                             "모서리 둥글기" },
            { "Gap",                                "간격" },
            { "Height",                             "높이" },
            { "Width",                              "너비" },
            { "Width step",                         "너비 감소량" },
            { "Font size",                          "글꼴 크기" },
            { "Show rain",                          "레인 표시" },
            { "Rain color",                         "레인 색상" },
            { "Rain width (0 = auto)",              "레인 너비 (0=자동)" },
            { "Font size (0 = preset)",             "글꼴 크기 (0=기본)" },
            { "Custom rain color",                  "사용자 지정 레인 색상" },
            { "Fade start",                         "페이드 시작" },
            { "Track length",                       "트랙 길이" },
            { "Speed (px/sec)",                     "속도 (px/초)" },
            { "Corner radius",                      "모서리 둥글기" },
            { "Track",                              "트랙" },
            { "Shape",                              "모양" },
            { "Shadow",                             "그림자" },
            { "Glow",                               "발광" },
            { "Tint",                               "색조" },
            { "The tint multiplies the rain color, so white glows\nin each key's own color.",
              "색조는 레인 색상에 곱해집니다. 흰색이면 각 키의\n원래 색으로 빛납니다." },
            { "Persist counts",                     "카운트 유지" },
            { "Style",                              "스타일" },
            { "Slots",                              "슬롯" },
            { "Click a card to turn that part on or off\n(highlighted = on).\nClick the ··· button on a card for its settings.",
              "카드를 눌러 해당 요소를 켜거나 끕니다\n(강조 표시 = 켜짐).\n카드의 ··· 버튼을 누르면 설정이 열립니다." },
            { "Reset counters for this preset",     "이 프리셋의 카운터 초기화" },
            { "+ Add Row",                          "+ 행 추가" },
            { "Delete this cell",                   "이 셀 삭제" },
            { "Rebind mode: click keys and press their new binds.\nClick: cell settings (bind, display text, width)\nRight Click: change key bind\nDrag: change key position\nClick Settings on a row for height + rain options.",
              "재지정 모드: 키를 누른 뒤 새 키를 입력합니다.\n클릭: 셀 설정 (키, 표시 텍스트, 너비)\n우클릭: 키 재지정\n드래그: 키 위치 변경\n행의 설정을 누르면 높이와 레인 옵션이 열립니다." },
            { "Ghost keys spawn rain at the matching top-row position\nbut don't count as input.\nWithholding them from the game needs the key limiter\non (Input tab) — with it off, they still spawn rain\nbut also hit tiles.",
              "고스트 키는 윗행의 같은 위치에 레인만 생성하고\n입력으로는 세지 않습니다.\n게임에 입력이 전달되지 않게 하려면 키 제한이\n켜져 있어야 합니다 (입력 탭).\n꺼져 있으면 레인은 나오지만 타일도 함께 칩니다." },

            // ── Input tab ───────────────────────────────────────────────────
            { "Menu",                               "메뉴" },
            { "Key Limiter",                        "키 제한" },
            { "Chatter Blocker",                    "채터링 방지" },
            { "Block game inputs while menu is open", "메뉴가 열려 있는 동안 게임 입력 차단" },
            { "Use Key Viewer keys (active preset)", "키 뷰어의 키 사용 (활성 프리셋)" },
            { "Threshold (ms)",                     "임계값 (ms)" },

            // ── Game UI tab › Hide ──────────────────────────────────────────
            { "Hide UI",                            "UI 숨기기" },
            { "Hide all UI",                        "모든 UI 숨기기" },
            { "Individual",                         "개별" },
            { "Hide judgements",                    "판정 숨기기" },
            { "All judgements",                     "모든 판정" },
            { "Judgements",                         "판정" },
            { "Perfects",                           "퍼펙트" },
            { "E/LPerfects",                        "빠름/느림" },
            { "Early/Late",                         "빠름!/느림!" },
            { "Misses",                             "미스" },
            { "Deaths",                             "사망" },
            { "Hit error meter",                    "판정선 미터" },
            { "Autoplay controls",                  "자동플레이 컨트롤" },
            { "Autoplay icon",                      "자동플레이 아이콘" },
            { "Autoplay text",                      "자동플레이 텍스트" },
            { "Beta build text",                    "베타 빌드 텍스트" },
            { "Song title",                         "곡 제목" },
            { "Difficulty",                         "난이도" },
            { "No-Fail",                            "노페일" },
            { "Click a card to hide that game element\n(highlighted = hidden).",
              "카드를 눌러 해당 게임 요소를 숨깁니다\n(강조 표시 = 숨김)." },
            { "Click a card to hide those judgement popups\n(highlighted = hidden).\n\"All judgements\" hides every type at once.",
              "카드를 눌러 해당 판정 표시를 숨깁니다\n(강조 표시 = 숨김).\n\"모든 판정\"은 모든 종류를 한 번에 숨깁니다." },

            // ── Game UI tab › Layout & elements ─────────────────────────────
            { "Layout",                             "레이아웃" },
            { "Game text",                          "게임 텍스트" },
            { "Game text size",                     "게임 텍스트 크기" },
            { "Level stats size",                   "레벨 통계 크기" },
            { "Line spacing",                       "줄 간격" },
            { "Elements",                           "요소" },
            { "Level Name",                         "레벨 이름" },
            { "Error Meter",                        "판정선" },
            { "Edit game UI on screen",             "화면에서 게임 UI 편집" },
            { "Override position",                  "위치 재정의" },
            { "Position X",                         "위치 X" },
            { "Position Y",                         "위치 Y" },
            { "Size",                               "크기" },
            { "Reset to default",                   "기본값으로 초기화" },
            { "Reset layout to Bismuth defaults",   "Bismuth 기본 레이아웃으로 초기화" },
            { "Reset layout to game defaults",      "게임 기본 레이아웃으로 초기화" },
            { "Drag and resize elements directly on screen.\nPrecise controls live in each element's page under Elements.",
              "화면에서 요소를 직접 끌어 옮기고 크기를 조정합니다.\n세부 설정은 요소 항목의 각 페이지에 있습니다." },
            { "Click a card to show or hide that game element\n(highlighted = shown).\nClick the ··· button on a card for position, scale,\nweight and alignment.\nJudgements, Level Name and Error Meter can't be\ntoggled here — hide them from the Hide UI tab.",
              "카드를 눌러 해당 게임 요소를 표시하거나 숨깁니다\n(강조 표시 = 표시 중).\n카드의 ··· 버튼에서 위치, 크기, 굵기, 정렬을\n설정할 수 있습니다.\n판정, 레벨 이름, 판정선는 여기서 끌 수 없습니다 —\nUI 숨기기에서 숨기세요." },

            // ── Appearance tab ──────────────────────────────────────────────
            { "Scale",                              "크기" },
            { "UI scale",                           "UI 크기" },
            { "Font",                               "글꼴" },
            { "Accent",                             "강조 색" },
            { "Accent color",                       "강조 색상" },
            { "Language",                           "언어" },
            { "Active",                             "사용 중" },
            { "Follow game",                        "게임 설정 따름" },
            { "English",                            "English" },
            { "Korean",                             "한국어" },
            { "Use overlay font",                   "오버레이 글꼴 사용" },
            { "Released",                           "뗐을 때" },
            { "Pressed",                            "눌렀을 때" },
            { "Custom color",                       "사용자 지정 색상" },
            { "Use custom color",                   "사용자 지정 색상 사용" },
            { "Apply accent as theme color",        "강조 색을 테마 색상으로 적용" },

            // ── Misc tab ────────────────────────────────────────────────────
            { "Custom levels",                      "커스텀 레벨" },
            { "Preview volume %",                   "미리듣기 음량 %" },
            { "Profiles",                           "프로필" },
            { "Load",                               "불러오기" },
            { "Save current settings as profile",   "현재 설정을 프로필로 저장" },
            { "Rescan profiles folder",             "프로필 폴더 다시 검색" },
            { "Open profiles folder",               "프로필 폴더 열기" },
            { "A profile snapshots ALL Bismuth settings.\nLoad applies it and rebuilds the panel.\nProfiles are .xml files in the Profiles folder —\nshare them, or drop one in and Rescan to import.",
              "프로필은 Bismuth의 모든 설정을 저장합니다.\n불러오기를 누르면 적용되고 패널이 다시 만들어집니다.\n프로필은 Profiles 폴더의 .xml 파일입니다 —\n공유하거나, 파일을 넣고 다시 검색으로 가져오세요." },
            { "Optimizations",                      "최적화" },
            { "Spectrum Throttle (every 2nd frame)", "스펙트럼 제한 (2프레임마다)" },
            { "Texture Non-Readable",               "텍스처 읽기 불가 처리" },
            { "DXT Compression (lossy)",            "DXT 압축 (손실)" },
            { "Physics NonAlloc",                   "물리 NonAlloc" },
            { "Volume Track DOTween Fix",           "볼륨 트랙 DOTween 수정" },
            { "Skip No-Op Screen Filters",          "빈 화면 필터 건너뛰기" },
            { "Leak Guard",                         "메모리 누수 방지" },
            { "Fast Bloom",                         "빠른 블룸" },
            { "Render All Hit Sounds",              "히트사운드 전체 렌더링" },
            { "Pre-mixes hit sounds on a background thread instead of scheduling one voice each. Experimental — check timing by ear.",
              "히트사운드를 하나씩 예약하는 대신 백그라운드 스레드에서 미리 믹싱합니다. 실험적 기능이며, 타이밍은 직접 들어서 확인하세요." },

            // ── Weight rows, text inputs ────────────────────────────────────
            { "Label weight",                       "라벨 굵기" },
            { "Value weight",                       "값 굵기" },
            { "Count weight",                       "카운트 굵기" },
            { "Separator text",                     "구분 문자" },
            { "Display text",                       "표시 텍스트" },
            { "New profile name",                   "새 프로필 이름" },
            { "Component type",                     "컴포넌트 종류" },
            { "Filter",                             "필터" },
            { "Name",                               "이름" },
            { "Text",                               "텍스트" },

            { "Unload Assets on Scene Change",      "Scene 전환 시 에셋 해제" },
            { "Debug",                              "디버그" },
            { "Debug mode",                         "디버그 모드" },
            { "Force reload",                       "강제 새로고침" },
            { "View log",                           "로그 보기" },
            { "Trace font sweep",                   "글꼴 스윕 추적" },
            { "Dump texts",                         "텍스트 덤프" },
            { "Dump images",                        "이미지 덤프" },
            { "Dump assets (sprites/textures)",     "에셋 덤프 (스프라이트/텍스처)" },
            { "Dump components",                    "컴포넌트 덤프" },

            // ── Gradient editor (UIBuilder) ─────────────────────────────────
            { "Solid",                              "단색" },
            { "Position",                           "위치" },
            { "Perfect color (t=1)",                "퍼펙트 색상 (t=1)" },
            { "+ Add stop",                         "+ 스톱 추가" },
            { "Remove this stop",                   "이 스톱 제거" },

            // ── Log viewer / update popup ───────────────────────────────────
            { "Clear",                              "지우기" },
            { "Close",                              "닫기" },
            { "Refresh",                            "새로고침" },
            { "Open in File Manager",               "파일 관리자에서 열기" },
            { "Later",                              "나중에" },
            { "Manual update",                      "수동 업데이트" },
            { "Update now (requires restart)",      "지금 업데이트 (재시작 필요)" },

            // ── Bespoke widgets, breadcrumbs, editors ───────────────────────
            { "← Back",                             "← 뒤로" },
            { "Search settings...",                 "설정 검색..." },
            { "Settings",                           "설정" },
            { "Bound key",                          "지정된 키" },
            { "Cell",                               "셀" },
            { "Copied",                             "복사됨" },
            { "Weight",                             "굵기" },
            { "  Weight",                           "  굵기" },
            { "Title weight",                       "제목 굵기" },
            { "Panel font",                         "패널 글꼴" },
            { "Master font",                        "기본 글꼴" },
            { "Stats font",                         "통계 글꼴" },
            { "Game font",                          "게임 글꼴" },

            // ── Panel chrome, prompts, empty states ─────────────────────────
            { "Mod enabled",                        "모드 사용" },
            { " to toggle",                         " 로 열기/닫기" },
            { "Undo",                               "실행 취소" },
            { "Click again to confirm",             "한 번 더 누르면 확정됩니다" },
            { "● Listen",                           "● 입력 대기" },
            { "■ Stop",                             "■ 중지" },
            { "Rebind Keys",                        "키 재지정" },
            { "Rebind mode ON — click a key, press its new bind. Click here to finish.",
              "재지정 모드 켜짐 — 키를 누른 뒤 새 키를 입력하세요. 여기를 누르면 종료됩니다." },
            { "Change key — click, then press the new key",
              "키 변경 — 누른 뒤 새 키를 입력하세요" },
            { "Press a key… (Esc cancels)",         "키를 누르세요… (Esc로 취소)" },
            { "+ Add Hand Preset",                  "+ 손 프리셋 추가" },
            { "+ Add Foot Preset",                  "+ 발 프리셋 추가" },
            { "Hand / ",                            "손 / " },
            { "Foot / ",                            "발 / " },
            { "Hand preset: ",                      "손 프리셋: " },
            { "Foot preset: ",                      "발 프리셋: " },
            { "Delete this row",                    "이 행 삭제" },
            { "Delete this row (last row — disabled)", "이 행 삭제 (마지막 행 — 불가)" },
            { "(top row has no key cells)",         "(윗행에 키 셀이 없습니다)" },
            { "Click a stop on the strip to edit it", "막대의 스톱을 눌러 편집합니다" },
            { "No stops yet — add one below",       "스톱이 없습니다 — 아래에서 추가하세요" },
            { "Delete the unused copy?\n",          "사용하지 않는 사본을 삭제할까요?\n" },
            { "Deleted the unused copy.",           "사용하지 않는 사본을 삭제했습니다." },
            { "Deleted. This session keeps running; restart the game with your selected loader.",
              "삭제했습니다. 이번 세션은 계속 실행되며, 선택한 로더로 게임을 다시 시작하세요." },
            { "Delete failed: ",                    "삭제 실패: " },

            { "Game default",                       "게임 기본값" },
            { "(none loaded)",                      "(불러온 항목 없음)" },
            { "✓ Done editing",                     "✓ 편집 완료" },
            { "Visibility lives in Hide UI → ",     "표시 여부는 UI 숨기기에 있습니다 → " },
            { "Drag to move (Shift: 1 axis)  ·  Ctrl/⌘+Z undo",
              "드래그로 이동 (Shift: 한 축)  ·  Ctrl/⌘+Z 실행 취소" },
            { "Drag to move (Shift: 1 axis)  ·  Grips / scroll to scale  ·  Right-click reset  ·  Ctrl/⌘+Z undo",
              "드래그로 이동 (Shift: 한 축)  ·  손잡이/스크롤로 크기 조절  ·  우클릭 초기화  ·  Ctrl/⌘+Z 실행 취소" },
            { "Style 1: white fill along the top edge; flashes the Progress perfect color at 100%.",
              "스타일 1: 화면 상단을 흰색으로 채우고, 100%에서 진행도 퍼펙트 색상으로 번쩍입니다." },

            // ── Updates (Misc → Updates) ────────────────────────────────────
            { "Updates",                            "업데이트" },
            { "Installed",                          "설치된 버전" },
            { "Build channel",                      "빌드 채널" },
            { "Stable",                             "안정판" },
            { "Beta",                               "베타" },
            { "Alpha",                              "알파" },
            { "Check now",                          "지금 확인" },
            { "Install update",                     "업데이트 설치" },
            { "Update to {0}",                      "{0}(으)로 업데이트" },
            { "Switch to {0}",                      "{0}(으)로 전환" },
            { "Open releases page",                 "릴리스 페이지 열기" },
            { "Update checks are not running",      "업데이트 확인이 실행되고 있지 않습니다" },
            { "Checking…",                          "확인 중…" },
            { "Up to date",                         "최신 버전입니다" },
            { "No builds published on this channel", "이 채널에는 배포된 빌드가 없습니다" },
            { "Available",                          "사용 가능" },
            { "Downloading…",                       "다운로드 중…" },
            { "Installed — restart the game to apply", "설치 완료 — 적용하려면 게임을 다시 시작하세요" },
            { "Failed",                             "실패" },
            { "Not checked yet",                    "아직 확인하지 않음" },
            { "Alpha and Beta include everything below them. Switching to a lower channel offers the newest build there, even if that is older than what you have.",
              "알파와 베타는 그 아래 단계의 빌드까지 모두 포함합니다. 더 낮은 채널로 바꾸면 그 채널의 최신 빌드를 설치할 수 있으며, 현재 설치된 버전보다 낮더라도 표시됩니다." },

            // ── Optimization descriptions (Misc → Optimizations) ────────────
            { "Halves AudioSource.GetSpectrumData FFT cost on levels that use audio visualization.",
              "오디오 시각화를 쓰는 레벨에서 AudioSource.GetSpectrumData의 FFT 비용을 절반으로 줄입니다." },
            { "Frees CPU-side pixel data after GPU upload. Halves RAM per custom level texture.",
              "GPU 업로드 후 CPU 쪽 픽셀 데이터를 해제합니다. 커스텀 레벨 텍스처당 RAM 사용량이 절반이 됩니다." },
            { "Compresses textures to DXT before upload. 4-6x VRAM savings, slight quality loss. Requires Non-Readable.",
              "업로드 전에 텍스처를 DXT로 압축합니다. VRAM을 4~6배 절약하지만 화질이 약간 떨어집니다. 텍스처 읽기 불가 처리가 필요합니다." },
            { "Eliminates per-frame Collider2D[] allocation from decoration hitbox checks.",
              "장식 히트박스 검사에서 매 프레임 발생하는 Collider2D[] 할당을 없앱니다." },
            { "Forces GC and unloads unused textures/audio between levels to reclaim memory.",
              "레벨 사이에 GC를 실행하고 사용하지 않는 텍스처와 오디오를 해제해 메모리를 회수합니다." },
            { "Prevents abandoned DOTween sequences from being created every frame on Volume-type track colors.",
              "볼륨 타입 트랙 색상에서 매 프레임 버려지는 DOTween 시퀀스가 생성되지 않도록 합니다." },
            { "Skips the screen tile/scroll shader passes when they are set to do nothing.",
              "화면 타일/스크롤 셰이더 패스가 아무 효과도 없을 때 건너뜁니다." },
            { "Frees textures the game replaces without destroying (decorations, camera, thumbnails, waveforms).",
              "게임이 파괴하지 않고 교체하는 텍스처를 해제합니다 (장식, 카메라, 썸네일, 파형)." },
            { "Drops bloom to its cheaper path. Visibly softer glow — off by default.",
              "블룸을 더 가벼운 방식으로 처리합니다. 광원이 눈에 띄게 부드러워지며, 기본값은 꺼짐입니다." },

        };
    }
}

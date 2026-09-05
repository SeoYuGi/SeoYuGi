using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 무전 채팅바 (지휘관 모드) — TAB(또는 Enter)으로 열고 문장을 치면 LLM이 명령으로 해석한다.
    /// 프리셋 창은 은퇴 (2026-09-05): 정형 콜은 퀵채팅(숫자키)이 즉시 명령이 되고,
    /// 이 바는 퀵챗으로 못 하는 자연어 명령 전용이다. 음성(V 꾹)도 같은 길로 들어온다.
    ///
    /// 열려 있는 동안 완전 정지(GameFreeze) — 슬로모(0.08)는 "덜 멈춘 느낌"이라는
    /// 피드백(2026-09-05)으로 폐기. 전투 시계가 전부 Time.deltaTime이라 같이 멈춘다.
    /// 발신(Enter)하면 즉시 닫혀 게임이 재개되고, 응답은 HUD 이벤트 피드로 온다.
    /// </summary>
    public class RadioWindow : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        /// <summary>입력줄이 화면에 있음(컴포넌트 활성) — 채팅 로그가 그 위에 쌓인다. 배치는 BattleHud.RadioFieldTopHud.</summary>
        public static bool Shown { get; private set; }
        void OnEnable() => Shown = true;

        /// <summary>열 때 전장을 정지시킬지. 싱글 지휘관 = true. 온라인은 호스트 시계라 정지 불가 — 러너가 false로 둔다.
        /// 온라인의 "고민 시간"은 무전 타임(호스트가 전원 동시 정지)이 대신한다.</summary>
        public bool FreezeOnOpen = true;
        bool frozeOnOpen;
        bool guided; // 첫 판 가이드로 열림 — 상단 안내 + 입력줄에 예시 문장 회전

        /// <summary>텍스트 입력 중 — 게임 핫키(해킹·퀵챗·핑·카메라·이동)가 이걸 보고 잠긴다.
        /// 한글 타이핑의 물리키가 게임키와 겹치기 때문 (ㅂ/ㅈ=카메라 회전, ㅗ=해킹).</summary>
        public static bool TextInputActive { get; private set; }

        /// <summary>자유 서술 발신 (Enter). 러너가 LlmRadio로 보내고, 응답이 오면 SetWaiting(false).</summary>
        public event Action<string> OnFreeText;

        GUIStyle hintStyle, inputStyle, rightHintStyle;
        bool stylesReady;
        string draft = "";   // 입력 중인 문장
        bool waiting;        // 발신 후 응답 대기 — 재발신 잠금 (게임은 돌아간다)
        bool wantFocus;      // 열린 직후 입력줄에 포커스

        /// <summary>교신 대기 해제 — 러너가 LlmRadio 응답 콜백에서 부른다.</summary>
        public void SetWaiting(bool value) => waiting = value;


        /// <summary>러너가 매 프레임 호출 — 지휘관 모드가 아니거나 전투 중이 아니면 enabled=false로 둔다.</summary>
        public void HandleHotkey()
        {
            if (Keyboard.current == null) return;
            var kb = Keyboard.current;
            // TAB = 열기/닫기 (2026-09-06 유저: Enter는 마우스 쥔 손을 풀어야 해서 불편). Enter 열기도 남겨둠.
            if (!IsOpen && (kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame ||
                            kb.numpadEnterKey.wasPressedThisFrame))
                Open();
            else if (IsOpen && (kb.tabKey.wasPressedThisFrame || kb.escapeKey.wasPressedThisFrame))
                Close();
        }

        /// <summary>첫 판 가이드 — 무전 타임에 자동으로 열리며 예시 문장을 보여준다.</summary>
        public void OpenGuided()
        {
            guided = true;
            Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            frozeOnOpen = FreezeOnOpen;
            if (frozeOnOpen) GameFreeze.Push(); // 완전 정지 — 치는 동안 전장이 안 흐른다 (싱글)
            Input.imeCompositionMode = IMECompositionMode.On; // 한글 조합 — Both 입력 모드 필수
            TextInputActive = true;
            wantFocus = true;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            guided = false;
            if (frozeOnOpen) GameFreeze.Pop();
            Input.imeCompositionMode = IMECompositionMode.Auto;
            TextInputActive = false;
        }

        void OnDisable()
        {
            Shown = false;
            Close(); // 라운드 종료·씬 전환에서 시간이 느린 채로 남지 않게
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            inputStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.62f, 0.72f) }
            };
            GameFonts.Apply(inputStyle, GameFonts.Hud);
            GameFonts.Apply(hintStyle, GameFonts.Hud);
        }

        void OnGUI()
        {
            // 상시 표시 (2026-09-06 "채팅 있는 게임들처럼"): 중앙 하단, 채팅 로그 맨 밑·하단바 바로 위에 입력줄을 고정해 둔다.
            // 닫힘 = 흐린 판 + 마지막 교신 응답(또는 여는 법), 열림 = 포커스된 입력줄. TAB/Enter로 열고 Enter 발신.
            if (!BattleHud.BattleHudActive) return;
            EnsureStyles();

            float u = BattleHud.PixelPerHud;
            inputStyle.fontSize = Mathf.RoundToInt(17 * u);
            hintStyle.fontSize = Mathf.RoundToInt(14 * u);

            float w = BattleHud.ChatW * u, fieldH = BattleHud.RadioFieldH * u;
            float x = (Screen.width - w) / 2f, yField = BattleHud.RadioFieldTopHud * u;
            var fieldRect = new Rect(x, yField, w, fieldH);
            var inner = new Rect(x + 10f * u, yField, w - 20f * u, fieldH);

            // 판 + 좌측 시안 액센트 — 채팅 로그와 같은 어두운 판. 닫혀 있으면 흐리게
            var prev = GUI.color;
            GUI.color = new Color(0.02f, 0.04f, 0.09f, IsOpen ? 0.9f : 0.55f);
            GUI.DrawTexture(fieldRect, Texture2D.whiteTexture);
            GUI.color = new Color(0.55f, 0.95f, 1f, IsOpen ? 0.9f : 0.35f);
            GUI.DrawTexture(new Rect(fieldRect.x, fieldRect.y, 2f, fieldRect.height), Texture2D.whiteTexture);
            GUI.color = prev;

            if (!IsOpen)
            {
                // 마지막 응답 잔상 표시는 은퇴 (2026-09-06 "영원히 안 사라지는데") — 응답은 채팅 로그가
                // 화자 클래스 색으로 이미 보여준다. 입력줄은 여는 법과 교신 상태만.
                string idle = !LlmRadio.HasKey ? "자유 무전 오프라인 (API 키 없음). 퀵챗(숫자키)은 동작"
                    : waiting ? "...교신 중"
                    : "TAB  무전 입력";
                GUI.Label(inner, idle, hintStyle);
                return;
            }

            if (!LlmRadio.HasKey)
            {
                GUI.Label(inner, "자유 무전 오프라인. API 키 없음. 퀵챗(숫자키)은 동작한다.", hintStyle);
                return;
            }

            var ev = Event.current;
            // 입력줄 밖 클릭 = 닫기 (2026-09-06). 입력줄 자체 클릭은 캐럿 이동이라 유지.
            if (ev.type == EventType.MouseDown && !fieldRect.Contains(ev.mousePosition))
            {
                Close();
                return;
            }

            // Enter = 발신 — TextField가 이벤트를 먹기 전에 가로챈다
            bool submit = !waiting && ev.type == EventType.KeyDown &&
                          (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter) &&
                          GUI.GetNameOfFocusedControl() == "RadioFreeText";
            if (submit)
            {
                ev.Use();
                var text = draft.Trim();
                draft = "";
                if (text.Length > 0)
                {
                    waiting = true;
                    OnFreeText?.Invoke(text);
                }
                Close(); // 발신 즉시 게임 재개 — 응답은 HUD 피드로
                return;
            }

            GUI.enabled = !waiting;
            GUI.SetNextControlName("RadioFreeText");
            draft = GUI.TextField(fieldRect, draft, inputStyle);
            GUI.enabled = true;

            // 오른쪽 끝 흐린 안내 — 위에 줄을 더 두면 채팅 로그와 겹친다
            if (rightHintStyle == null) rightHintStyle = new GUIStyle(hintStyle) { alignment = TextAnchor.MiddleRight };
            rightHintStyle.fontSize = hintStyle.fontSize;
            GUI.Label(new Rect(x, yField, w - 8f * u, fieldH), waiting ? "...교신 중" : "Enter 발신   TAB 닫기", rightHintStyle);

            // 가이드 — 비어 있는 입력줄에 예시 문장이 3초마다 바뀐다 (회색). 라벨은 클릭을 안 먹어 포커스는 그대로.
            if (guided && string.IsNullOrEmpty(draft))
            {
                var ex = Guide.RadioExamples[(int)(Time.unscaledTime / 3f) % Guide.RadioExamples.Length];
                GUI.Label(inner, "예: " + ex, hintStyle);
            }

            if (wantFocus)
            {
                GUI.FocusControl("RadioFreeText");
                wantFocus = false;
            }
        }
    }
}

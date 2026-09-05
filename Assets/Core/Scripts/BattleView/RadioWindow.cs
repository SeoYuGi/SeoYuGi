using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 무전 채팅바 (지휘관 모드) — Enter로 열고 문장을 치면 LLM이 명령으로 해석한다.
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

        Func<string> ackProvider;
        GUIStyle hintStyle, inputStyle, ackStyle;
        Texture2D texBar; // 무전 단말 패널 아트 (Panel_Radio) — 없으면 어두운 판 폴백
        bool stylesReady;
        string draft = "";   // 입력 중인 문장
        bool waiting;        // 발신 후 응답 대기 — 재발신 잠금 (게임은 돌아간다)
        bool wantFocus;      // 열린 직후 입력줄에 포커스

        /// <summary>교신 대기 해제 — 러너가 LlmRadio 응답 콜백에서 부른다.</summary>
        public void SetWaiting(bool value) => waiting = value;

        public void Init(Func<string> lastAck)
        {
            ackProvider = lastAck;
        }

        /// <summary>러너가 매 프레임 호출 — 지휘관 모드가 아니거나 전투 중이 아니면 enabled=false로 둔다.</summary>
        public void HandleHotkey()
        {
            if (Keyboard.current == null) return;
            if (!IsOpen && (Keyboard.current.enterKey.wasPressedThisFrame ||
                            Keyboard.current.numpadEnterKey.wasPressedThisFrame))
                Open();
            else if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame)
                Close();
        }

        /// <summary>첫 판 가이드 — 무전 타임에 자동으로 열리며 예시 문장을 보여준다.</summary>
        public void OpenGuided()
        {
            guided = true;
            Open();
        }

        void Open()
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
            Close(); // 라운드 종료·씬 전환에서 시간이 느린 채로 남지 않게
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            texBar = BattleHud.LoadKeyed("UI/Panel_Radio");
            inputStyle = new GUIStyle(GUI.skin.textField) { alignment = TextAnchor.MiddleLeft };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.62f, 0.72f) }
            };
            ackStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleLeft, wordWrap = true,
                normal = { textColor = new Color(0.75f, 0.95f, 0.8f) }
            };
            GameFonts.Apply(inputStyle, GameFonts.Hud);
            GameFonts.Apply(hintStyle, GameFonts.Hud);
            GameFonts.Apply(ackStyle, GameFonts.Hud);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            float s = Mathf.Max(1f, Screen.height / 1080f) * 1.25f;
            inputStyle.fontSize = Mathf.RoundToInt(18 * s);
            hintStyle.fontSize = Mathf.RoundToInt(14 * s);
            ackStyle.fontSize = Mathf.RoundToInt(15 * s);

            // 하단 좌측 채팅바 — 시선이 전장에 남는 위치.
            // y는 하단 통합 바(높이 ~110·프레임 여유 포함) 위로 — 겹침 수정 (2026-09-05)
            float w = Mathf.Min(560f * s, Screen.width * 0.5f);
            float fieldH = 42f * s, pad = 10f * s;
            float x = 24f * s;
            float yField = Screen.height - 205f * s;

            if (!LlmRadio.HasKey)
            {
                GUI.Label(new Rect(x, yField, w, fieldH),
                    "자유 무전 오프라인 — API 키 없음. 퀵챗(숫자키)은 동작한다.", hintStyle);
                return;
            }

            // 배경판 — 무전 단말 아트 (스피커 그릴·시안 테두리), 없으면 어두운 판
            var prev = GUI.color;
            var back = new Rect(x - pad, yField - 30f * s - pad, w + pad * 2, fieldH + 30f * s + pad * 2);
            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.88f);
            GUI.DrawTexture(back, Texture2D.whiteTexture);
            GUI.color = prev;
            if (texBar != null)
                GUI.DrawTexture(back, texBar, ScaleMode.StretchToFill);

            string ack = ackProvider != null ? ackProvider() : "";
            string topLine = waiting ? "…교신 중"
                : guided ? "이렇게 말하면 알아듣습니다 — 입력하거나 V를 누른 채 말하세요"
                : !string.IsNullOrEmpty(ack) ? "> " + ack
                : "무전 · Enter 발신 · ESC 취소";
            GUI.Label(new Rect(x, yField - 28f * s, w, 26f * s), topLine,
                waiting || string.IsNullOrEmpty(ack) ? hintStyle : ackStyle);

            // Enter = 발신 — TextField가 이벤트를 먹기 전에 가로챈다
            var ev = Event.current;
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
            draft = GUI.TextField(new Rect(x, yField, w, fieldH), draft, inputStyle);
            GUI.enabled = true;

            // 가이드 — 비어 있는 입력줄에 예시 문장이 3초마다 바뀐다 (회색). 라벨은 클릭을 안 먹어 포커스는 그대로.
            if (guided && string.IsNullOrEmpty(draft))
            {
                var ex = Guide.RadioExamples[(int)(Time.unscaledTime / 3f) % Guide.RadioExamples.Length];
                GUI.Label(new Rect(x + 8f * s, yField, w - 16f * s, fieldH), "예: " + ex, hintStyle);
            }

            if (wantFocus)
            {
                GUI.FocusControl("RadioFreeText");
                wantFocus = false;
            }
        }
    }
}

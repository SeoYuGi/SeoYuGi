using System;
using System.Collections.Generic;
using SeoYuGi.Battle;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 무전 지휘창 (지휘관 모드) — T로 열면 전투가 느려지고, 팀원에게 상시 명령을 내린다.
    ///
    /// 프리셋 버튼은 LLM을 거치지 않는다. 즉시 나가고 오프라인에서도 동작하므로
    /// 네트워크가 없어도 지휘관 모드 전체가 성립한다. 자유 서술은 나중에 얹는 확장이다.
    ///
    /// 시간은 Time.timeScale로 늦춘다 — 전투 시계(Combat/Move/Round.Tick)가 전부
    /// Time.deltaTime에서 나오므로 별도 배선 없이 같이 느려진다. 완전 정지(0) 대신
    /// 아주 느리게(0.08) 두는 이유는, 멈추면 코루틴·연출이 얼어붙고 "정지 버그"처럼 보이기 때문이다.
    /// </summary>
    public class RadioWindow : MonoBehaviour
    {
        const float SlowScale = 0.08f;   // 열려 있는 동안의 시간 배속
        const float FadeSeconds = 0.12f;

        public bool IsOpen { get; private set; }

        /// <summary>프리셋 선택 — (인덱스). 러너가 실제 명령으로 펴서 적용한다.</summary>
        public event Action<int> OnPreset;

        Func<string> ackProvider;
        float restoreScale = 1f;
        GUIStyle titleStyle, presetStyle, ackStyle, hintStyle;
        bool stylesReady;

        public void Init(Func<string> lastAck)
        {
            ackProvider = lastAck;
        }

        /// <summary>러너가 매 프레임 호출 — 지휘관 모드가 아니거나 전투 중이 아니면 enabled=false로 둔다.</summary>
        public void HandleHotkey()
        {
            if (Keyboard.current == null) return;
            if (Keyboard.current.tKey.wasPressedThisFrame) Toggle();
            else if (IsOpen && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            restoreScale = Mathf.Approximately(Time.timeScale, SlowScale) ? 1f : Time.timeScale;
            Time.timeScale = SlowScale;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            Time.timeScale = restoreScale <= 0f ? 1f : restoreScale;
        }

        void OnDisable()
        {
            Close(); // 라운드 종료·씬 전환에서 시간이 느린 채로 남지 않게
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 20, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.9f, 1f) }
            };
            presetStyle = new GUIStyle(GUI.skin.button) { fontSize = 17, fontStyle = FontStyle.Bold };
            ackStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16, alignment = TextAnchor.MiddleLeft, wordWrap = true,
                normal = { textColor = new Color(0.75f, 0.95f, 0.8f) }
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.55f, 0.62f, 0.72f) }
            };
            GameFonts.Apply(titleStyle, GameFonts.Title);
            GameFonts.Apply(presetStyle, GameFonts.Hud);
            GameFonts.Apply(ackStyle, GameFonts.Hud);
            GameFonts.Apply(hintStyle, GameFonts.Hud);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            const float W = 420f, PadX = 18f;
            float h = Screen.height;
            var box = new Rect(24f, h * 0.5f - 190f, W, 380f);

            // 어두운 판 — 전투 화면과 분리
            var prev = GUI.color;
            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.93f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            float y = box.y + 16f;
            GUI.Label(new Rect(box.x + PadX, y, W - PadX * 2, 28f), "무전", titleStyle);
            y += 30f;
            GUI.Label(new Rect(box.x + PadX, y, W - PadX * 2, 20f),
                "팀원에게 지시한다 · T 또는 ESC로 닫기", hintStyle);
            y += 28f;

            var presets = OrderPresets.All;
            for (int i = 0; i < presets.Length; i++)
            {
                var r = new Rect(box.x + PadX, y, W - PadX * 2, 40f);
                if (GUI.Button(r, presets[i].label, presetStyle))
                {
                    OnPreset?.Invoke(i);
                    Close();
                }
                y += 46f;
            }

            string ack = ackProvider != null ? ackProvider() : "";
            if (!string.IsNullOrEmpty(ack))
                GUI.Label(new Rect(box.x + PadX, box.yMax - 44f, W - PadX * 2, 36f), "> " + ack, ackStyle);
        }
    }
}

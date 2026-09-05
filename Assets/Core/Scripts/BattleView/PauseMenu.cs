using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// ESC 일시정지 메뉴 — 계속하기 / 설정 / 게임 종료.
    ///
    /// 무전창(RadioWindow)과 달리 시간을 완전히 멈춘다(timeScale 0). 무전은 전투의 일부라
    /// 흐름이 이어져야 하지만, 이건 전투 밖으로 나가는 것이다.
    ///
    /// ESC는 이미 조준 취소에 쓰이므로, 조준 중이거나 무전창이 열려 있으면 열지 않는다 —
    /// 그쪽이 먼저 ESC를 소비하고, 아무것도 열려 있지 않을 때만 메뉴가 뜬다.
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        public bool IsOpen { get; private set; }

        /// <summary>메뉴를 열어도 되는가 — 조준 중·무전 중이면 ESC는 그쪽 몫이다.</summary>
        public Func<bool> CanOpen;

        /// <summary>설정에서 조절할 음량 — 러너가 BattleAudio를 물려준다.</summary>
        public BattleAudio Audio;

        bool inSettings;
        float restoreScale = 1f;
        GUIStyle titleStyle, itemStyle, labelStyle, hintStyle;
        bool stylesReady;

        public void HandleHotkey()
        {
            if (Keyboard.current == null) return;
            if (!Keyboard.current.escapeKey.wasPressedThisFrame) return;

            if (IsOpen)
            {
                if (inSettings) inSettings = false; // 설정 → 메뉴로 한 단계만 뒤로
                else Close();
                return;
            }
            if (CanOpen != null && !CanOpen()) return; // 조준·무전이 ESC를 먼저 쓴다
            Open();
        }

        void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            inSettings = false;
            restoreScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            inSettings = false;
            Time.timeScale = restoreScale <= 0f ? 1f : restoreScale;
        }

        void OnDisable()
        {
            Close(); // 씬 전환·라운드 조립에서 시간이 멈춘 채 남지 않게
        }

        void EnsureStyles()
        {
            if (stylesReady) return;
            stylesReady = true;
            titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.55f, 0.9f, 1f) }
            };
            itemStyle = new GUIStyle(GUI.skin.button) { fontSize = 22, fontStyle = FontStyle.Bold };
            labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18, alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.8f, 0.86f, 0.94f) }
            };
            hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15, alignment = TextAnchor.MiddleCenter,
                normal = { textColor = new Color(0.55f, 0.62f, 0.72f) }
            };
            GameFonts.Apply(titleStyle, GameFonts.Title);
            GameFonts.Apply(itemStyle, GameFonts.Hud);
            GameFonts.Apply(labelStyle, GameFonts.Hud);
            GameFonts.Apply(hintStyle, GameFonts.Hud);
        }

        void OnGUI()
        {
            if (!IsOpen) return;
            EnsureStyles();

            const float W = 420f, PadX = 30f;
            float H = inSettings ? 400f : 340f;
            var box = new Rect((Screen.width - W) * 0.5f, (Screen.height - H) * 0.5f, W, H);

            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.72f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = new Color(0.35f, 0.85f, 1f, 0.55f);
            GUI.DrawTexture(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), Texture2D.whiteTexture);
            GUI.color = new Color(0.02f, 0.04f, 0.08f, 0.97f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;

            if (inSettings) DrawSettings(box, PadX);
            else DrawMenu(box, PadX);
        }

        void DrawMenu(Rect box, float padX)
        {
            float y = box.y + 26f;
            GUI.Label(new Rect(box.x, y, box.width, 38f), "일시정지", titleStyle);
            y += 62f;

            float w = box.width - padX * 2f;
            if (GUI.Button(new Rect(box.x + padX, y, w, 56f), "계속하기", itemStyle)) Close();
            y += 64f;
            if (GUI.Button(new Rect(box.x + padX, y, w, 56f), "설정", itemStyle)) inSettings = true;
            y += 64f;
            if (GUI.Button(new Rect(box.x + padX, y, w, 56f), "게임 종료", itemStyle)) Quit();

            GUI.Label(new Rect(box.x, box.yMax - 36f, box.width, 20f), "ESC — 계속하기", hintStyle);
        }

        void DrawSettings(Rect box, float padX)
        {
            float y = box.y + 26f;
            GUI.Label(new Rect(box.x, y, box.width, 38f), "설정", titleStyle);
            y += 60f;

            float w = box.width - padX * 2f;
            if (Audio != null)
            {
                Audio.BgmVolume = Slider(box.x + padX, ref y, w, "배경음", Audio.BgmVolume);
                Audio.SfxVolume = Slider(box.x + padX, ref y, w, "효과음", Audio.SfxVolume);
                Audio.VoiceVolume = Slider(box.x + padX, ref y, w, "음성", Audio.VoiceVolume);
            }
            else
            {
                GUI.Label(new Rect(box.x + padX, y, w, 24f), "조절할 항목이 없습니다.", labelStyle);
                y += 32f;
            }

            if (GUI.Button(new Rect(box.x + padX, box.yMax - 76f, w, 48f), "뒤로", itemStyle))
                inSettings = false;
            GUI.Label(new Rect(box.x, box.yMax - 26f, box.width, 20f), "ESC — 뒤로", hintStyle);
        }

        float Slider(float x, ref float y, float w, string label, float value)
        {
            GUI.Label(new Rect(x, y, w * 0.4f, 24f), label, labelStyle);
            GUI.Label(new Rect(x + w - 56f, y, 56f, 24f),
                Mathf.RoundToInt(value * 100f) + "%",
                new GUIStyle(labelStyle) { alignment = TextAnchor.MiddleRight });
            y += 26f;
            float v = GUI.HorizontalSlider(new Rect(x, y + 4f, w, 18f), value, 0f, 1f);
            y += 34f;
            return v;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}

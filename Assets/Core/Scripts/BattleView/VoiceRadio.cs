using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 음성 무전 (지휘관 모드) — V 꾹 누르고 말하면(push-to-talk) Whisper가 받아쓰고,
    /// 그 문장이 채팅 무전과 같은 길(LlmRadio)로 들어가 명령이 된다.
    ///
    /// 누르는 동안 완전 정지(GameFreeze) — 채팅과 동일. 말 끝나고 손 떼면 발신 + 재개.
    /// 폴백: 키 없음·마이크 없음·전사 실패 → 아무 일도 안 일어남. 채팅·퀵챗이 항상 남아 있다.
    /// </summary>
    public class VoiceRadio : MonoBehaviour
    {
        const string Endpoint = "https://api.openai.com/v1/audio/transcriptions";
        const int SampleRate = 16000;
        const int MaxSeconds = 10;      // PTT 한 번의 상한 — 넘기면 자동 발신
        const float MinSeconds = 0.4f;  // 이보다 짧으면 오발로 보고 버림

        /// <summary>받아쓴 문장 — 러너가 채팅 무전과 같은 핸들러로 넘긴다.</summary>
        public event Action<string> OnTranscript;

        public bool IsRecording { get; private set; }

        Func<bool> canRecord;   // 러너가 준다 — 전투 중 && 타이핑 중 아님
        AudioClip clip;
        GUIStyle recStyle;
        Texture2D texMic;       // 마이크 배지 아트 — 녹음 표시

        public void Init(Func<bool> canRecordNow)
        {
            canRecord = canRecordNow;
        }

        void Update()
        {
            if (Keyboard.current == null) return;

            if (!IsRecording)
            {
                if (Keyboard.current.vKey.wasPressedThisFrame &&
                    LlmRadio.HasKey && Microphone.devices.Length > 0 &&
                    (canRecord == null || canRecord()))
                {
                    clip = Microphone.Start(null, false, MaxSeconds, SampleRate);
                    IsRecording = clip != null;
                    if (IsRecording) GameFreeze.Push(); // 말하는 동안 완전 정지 (마이크는 실시간 하드웨어라 안 멈춘다)
                }
                return;
            }

            // 손을 뗐거나, 상한에 닿았거나, 상황이 바뀌면(라운드 종료 등) 마무리
            bool released = Keyboard.current.vKey.wasReleasedThisFrame;
            bool full = Microphone.GetPosition(null) >= clip.samples - 1;
            bool aborted = canRecord != null && !canRecord();
            if (released || full || aborted)
                FinishRecording(discard: aborted);
        }

        void OnDisable()
        {
            if (IsRecording) FinishRecording(discard: true);
        }

        void FinishRecording(bool discard)
        {
            int pos = Microphone.GetPosition(null);
            Microphone.End(null);
            IsRecording = false;
            GameFreeze.Pop();

            if (discard || clip == null || pos < (int)(SampleRate * MinSeconds)) { clip = null; return; }

            var samples = new float[pos]; // 모노 1채널
            clip.GetData(samples, 0);
            clip = null;
            Send(EncodeWav(samples, SampleRate));
        }

        void Send(byte[] wav)
        {
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("file", wav, "radio.wav", "audio/wav"),
                new MultipartFormDataSection("model", "whisper-1"),
                new MultipartFormDataSection("language", "ko")
            };
            var req = UnityWebRequest.Post(Endpoint, form);
            req.timeout = 15;
            req.SetRequestHeader("Authorization", "Bearer " + LlmRadio.ApiKey);
            req.SendWebRequest().completed += _ =>
            {
                try
                {
                    if (req.result == UnityWebRequest.Result.Success)
                    {
                        var text = (string)JObject.Parse(req.downloadHandler.text)["text"];
                        text = text?.Trim();
                        if (!string.IsNullOrEmpty(text)) OnTranscript?.Invoke(text);
                    }
                    else Debug.LogWarning($"VoiceRadio 전사 실패: {req.responseCode} {req.error}");
                }
                catch (Exception e) { Debug.LogWarning($"VoiceRadio 파싱 실패: {e.Message}"); }
                finally { req.Dispose(); }
            };
        }

        /// <summary>PCM float → 16비트 모노 WAV.</summary>
        static byte[] EncodeWav(float[] samples, int sampleRate)
        {
            int dataSize = samples.Length * 2;
            var bytes = new byte[44 + dataSize];
            void WriteInt(int offset, int v) { bytes[offset] = (byte)v; bytes[offset + 1] = (byte)(v >> 8); bytes[offset + 2] = (byte)(v >> 16); bytes[offset + 3] = (byte)(v >> 24); }
            void WriteShort(int offset, short v) { bytes[offset] = (byte)v; bytes[offset + 1] = (byte)(v >> 8); }

            Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
            WriteInt(4, 36 + dataSize);
            Encoding.ASCII.GetBytes("WAVEfmt ").CopyTo(bytes, 8);
            WriteInt(16, 16);            // fmt 청크 크기
            WriteShort(20, 1);           // PCM
            WriteShort(22, 1);           // 모노
            WriteInt(24, sampleRate);
            WriteInt(28, sampleRate * 2); // byte rate
            WriteShort(32, 2);           // block align
            WriteShort(34, 16);          // bits per sample
            Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
            WriteInt(40, dataSize);

            for (int i = 0; i < samples.Length; i++)
                WriteShort(44 + i * 2, (short)(Mathf.Clamp(samples[i], -1f, 1f) * short.MaxValue));
            return bytes;
        }

        void OnGUI()
        {
            if (!IsRecording) return;
            if (recStyle == null)
            {
                recStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(1f, 0.35f, 0.35f) }
                };
                GameFonts.Apply(recStyle, GameFonts.Hud);
                texMic = BattleHud.LoadKeyed("UI/Icon_Mic");
            }
            float s = Mathf.Max(1f, Screen.height / 1080f) * 1.25f;
            recStyle.fontSize = Mathf.RoundToInt(30 * s);

            // 화면 중앙 하단 — 어두운 판 + 마이크 배지 점멸로 확실히 보이게 (unscaled: 프리즈 중에도 깜빡인다)
            float w = 480f * s, h = 64f * s;
            var box = new Rect((Screen.width - w) * 0.5f, Screen.height * 0.72f, w, h);
            var prev = GUI.color;
            GUI.color = new Color(0.05f, 0.02f, 0.02f, 0.9f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = prev;
            bool blink = Time.unscaledTime % 0.8f < 0.5f;
            if (texMic != null)
            {
                GUI.color = new Color(1f, 1f, 1f, blink ? 1f : 0.35f);
                GUI.DrawTexture(new Rect(box.x + 10f * s, box.y + 6f * s, h - 12f * s, h - 12f * s),
                    texMic, ScaleMode.ScaleToFit);
                GUI.color = prev;
                GUI.Label(box, "녹음 중. 손 떼면 발신", recStyle);
            }
            else
                GUI.Label(box, (blink ? "REC " : "   ") + "녹음 중. 손 떼면 발신", recStyle);
        }
    }
}

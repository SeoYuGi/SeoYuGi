using System.Collections.Generic;
using UnityEngine;

namespace SeoYuGi.BattleView
{
    /// <summary>
    /// 임시 오디오 재생기 — Resources/Audio/{BGM,SFX,Voice}에서 이름으로 로드.
    /// SFX 원본이 10초 클립(같은 소리 반복)이라 maxSeconds로 잘라 재생한다 —
    /// 정식 사운드 뱅크가 들어오면 트림된 클립 + 이 컴포넌트 교체.
    /// </summary>
    public class BattleAudio : MonoBehaviour
    {
        [Range(0f, 1f)] [SerializeField] float bgmVolume = 0.3f;
        [Range(0f, 1f)] [SerializeField] float sfxVolume = 0.85f;
        [Range(0f, 1f)] [SerializeField] float voiceVolume = 1f;

        AudioSource bgm;
        AudioSource captureLoop;
        AudioSource typingLoop;
        readonly Dictionary<string, AudioClip> cache = new Dictionary<string, AudioClip>();

        void Awake()
        {
            bgm = gameObject.AddComponent<AudioSource>();
            bgm.playOnAwake = false;
            captureLoop = NewLoopSource();
            typingLoop = NewLoopSource();
        }

        AudioSource NewLoopSource()
        {
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = true;
            return src;
        }

        AudioClip Load(string path)
        {
            if (cache.TryGetValue(path, out var clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/" + path);
            if (clip == null) Debug.LogWarning($"오디오 없음: Resources/Audio/{path}");
            cache[path] = clip;
            return clip;
        }

        /// <summary>BGM 교체. 같은 곡이면 무시.</summary>
        public void PlayBgm(string name, bool loop = true)
        {
            var clip = Load("BGM/" + name);
            if (clip == null || bgm.clip == clip) return;
            bgm.clip = clip;
            bgm.loop = loop;
            bgm.volume = bgmVolume;
            bgm.Play();
        }

        public void StopBgm()
        {
            bgm.Stop();
            bgm.clip = null;
        }

        /// <summary>효과음 1회. maxSeconds 지나면 잘라서 정지 (10초 원본 대응).</summary>
        public void PlaySfx(string name, float maxSeconds = 1.2f)
        {
            PlayOneShot("SFX/" + name, sfxVolume, maxSeconds);
        }

        /// <summary>관제 AI 보이스 — 전체 재생. 재생 길이(초) 반환 — 자막 표시 시간용.</summary>
        public float PlayVoice(string name)
        {
            return PlayOneShot("Voice/" + name, voiceVolume, 30f);
        }

        float PlayOneShot(string path, float volume, float maxSeconds)
        {
            var clip = Load(path);
            if (clip == null) return 0f;
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.clip = clip;
            src.volume = volume;
            src.Play();
            float length = Mathf.Min(maxSeconds, clip.length);
            Destroy(src, length + 0.05f);
            return length;
        }

        /// <summary>거점 점거 중 틱 루프 on/off.</summary>
        public void SetCaptureLoop(bool on) =>
            SetLoop(captureLoop, "SFX/S11_CaptureLoop", on, sfxVolume * 0.5f);

        /// <summary>브리핑 화면 타이핑 루프 on/off.</summary>
        public void SetTypingLoop(bool on) =>
            SetLoop(typingLoop, "SFX/S25_Typing", on, sfxVolume * 0.35f);

        void SetLoop(AudioSource src, string path, bool on, float volume)
        {
            if (on)
            {
                if (src.isPlaying) return;
                var clip = Load(path);
                if (clip == null) return;
                src.clip = clip;
                src.volume = volume;
                src.Play();
            }
            else if (src.isPlaying)
            {
                src.Stop();
            }
        }
    }
}

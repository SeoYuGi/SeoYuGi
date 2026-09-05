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
            Debug.Log($"[SFX] {name}"); // 임시 진단 (2026-09-05) — 거슬리는 소리 범인 색출용, 확인 후 제거
            PlayOneShot("SFX/" + name, sfxVolume, maxSeconds);
        }

        /// <summary>관제 AI 보이스 — 전체 재생. 재생 길이(초) 반환 — 자막 표시 시간용.</summary>
        public float PlayVoice(string name)
        {
            return PlayOneShot("Voice/" + name, voiceVolume, 30f);
        }

        const int MaxOneShots = 8; // 동시 재생 상한 — 난전에서 소스가 겹치면 합산 클리핑으로 "끼익" 찢어진다 (2026-09-05)

        float PlayOneShot(string path, float volume, float maxSeconds)
        {
            var clip = Load(path);
            if (clip == null) return 0f;

            // 상한 초과 시 이번 소리는 생략 — 이미 8겹이면 어차피 안 들리고 파형만 깨진다
            int playing = 0;
            foreach (var s in GetComponents<AudioSource>())
                if (s != null && s.isPlaying && s != bgm) playing++;
            if (playing >= MaxOneShots) return 0f;

            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.clip = clip;
            src.volume = volume * 0.8f; // 합산 여유 — 개별 소리가 아니라 총합이 깨지는 걸 막는다
            src.Play();
            float length = Mathf.Min(maxSeconds, clip.length);
            Destroy(src, length + 0.05f);
            return length;
        }

        AudioClip thumpSmall, thumpBig;

        /// <summary>저역 임팩트 "쿵" — 기존 SFX 위에 겹쳐 무게 추가 (절차 합성, 파일 불필요).</summary>
        public void PlayThump(bool big)
        {
            if (thumpSmall == null)
            {
                thumpSmall = MakeThump(0.16f, 62f, 30f);
                thumpBig = MakeThump(0.3f, 52f, 26f);
            }
            var clip = big ? thumpBig : thumpSmall;
            var src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.clip = clip;
            src.volume = sfxVolume * (big ? 1f : 0.65f);
            src.Play();
            Destroy(src, clip.length + 0.05f);
        }

        /// <summary>감쇠 사인 서브 + 노이즈 트랜지언트 — 다크 톤 저역 펀치.</summary>
        static AudioClip MakeThump(float seconds, float freq, float pitchDrop)
        {
            const int rate = 44100;
            int n = (int)(rate * seconds);
            var data = new float[n];
            float phase = 0f;
            var rng = new System.Random(7);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float env = Mathf.Exp(-t * (10f / seconds) * 0.55f);
                float f = Mathf.Max(30f, freq - pitchDrop * (t / seconds));
                phase += 2f * Mathf.PI * f / rate;
                float s = Mathf.Sin(phase) * env;
                if (i < 130) s += ((float)rng.NextDouble() * 2f - 1f) * 0.35f * (1f - i / 130f); // 타격 클릭
                data[i] = Mathf.Clamp(s, -1f, 1f) * 0.9f;
            }
            var clip = AudioClip.Create("thump", n, 1, rate, false);
            clip.SetData(data, 0);
            return clip;
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

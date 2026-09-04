using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using DG.Tweening;

/// <summary>
/// 게임 전체 BGM · SFX 담당 매니저.
/// Singleton&lt;SoundManager&gt; 상속 → 단일 인스턴스, DontDestroyOnLoad.
///
/// ── BGM (AudioSource 1개, phase별 loop 설정) ─────────────
/// SetPhase(GamePhase)   : 페이즈 BGM으로 DOTween crossfade
/// PlayBGM(AudioClip)    : 미등록 곡으로 crossfade
/// StopBGM() / StopBGMFade()
///
/// ── SFX ──────────────────────────────────────────────────
/// PlaySFX(SFXType) / PlaySFX(AudioClip)               : 2D (한 소스 PlayOneShot, 겹침 OK)
/// PlaySFXAt(SFXType, pos) / PlaySFXAt(AudioClip, pos) : 3D (위치별 voice 풀, round-robin)
/// StopAllSFX()                                        : 2D + 3D + 루프 전부 정지
///
/// ── 루프 SFX (앰비언스·기믹 지속음: 빗소리, 수문 물소리, 퍼레이드 음악…) ──
/// PlayLoop(SFXType)          : 2D 루프 시작(이미 도는 중이면 무시 — 매 프레임 불러도 안전)
/// PlayLoopAt(SFXType, pos)   : 3D 루프 — 이미 도는 중이면 위치만 갱신(이동체 추적용)
/// StopLoop(SFXType)          : 해당 루프 fade-out 정지
/// StopAllLoops()             : 루프 전부 정지 — 씬 전환·라운드 리셋 안전망
///
/// ── 볼륨 (AudioMixer log scale, PlayerPrefs 저장) ─────────
/// SetBGMVolume(0~1) / SetSFXVolume(0~1)
///
/// SFX: 2D는 매니저 소스 1개에 PlayOneShot(겹침). 3D는 자식 소스 여러 개(_sfx3DVoices)를
///      round-robin — 소스마다 위치를 따로 둬 동시 발음이 안 섞임. 모두 SFX 믹서그룹·StopAllSFX 대상.
/// </summary>
public class SoundManager : Singleton<SoundManager>
{
    [SerializeField] SoundLibrarySO _library;
    [SerializeField] AudioMixer     _mixer;

    [Header("BGM crossfade 지속 시간 (초)")]
    [SerializeField] float _bgmFadeDuration = 1f;

    [Header("3D SFX 동시 발음 수 — 각자 위치를 유지할 수 있는 최대 동시 개수")]
    [SerializeField] int _sfx3DVoices = 8;

    // AudioSource 볼륨 = "믹서에 얼마나 보낼지" 비율(0~1). 실제 사용자 볼륨은 AudioMixer 파라미터가 담당.
    const float kBgmVolume = 1f;

    // 3D 효과음 거리 감쇠 — 카메라(=리스너)가 플레이어에서 12유닛 뒤에 있는 것을 감안한 값.
    const float kSfx3DMinDistance = 15f;   // 이 안쪽은 원음 — 내 주변에서 난 소리는 온전히 들린다
    const float kSfx3DMaxDistance = 70f;   // 이 밖은 무음 — 맵 전체(대각 약 38)를 넉넉히 덮는다

    AudioSource   _bgmSource;   // BGM
    AudioSource   _sfx2D;       // 2D 효과음 — PlayOneShot으로 겹쳐 재생
    AudioSource[] _sfx3D;       // 3D 효과음 — 위치별 동시 발음 위해 자식 소스 여러 개 round-robin
    int           _sfx3DIndex;
    Tween         _bgmTween;

    readonly Dictionary<SFXType,   AudioClip[]> _sfxMap = new();
    readonly Dictionary<GamePhase, AudioClip>   _bgmMap = new();

    protected override void Awake()
    {
        base.Awake();                   // Singleton + DontDestroyOnLoad
        if (Instance != this) return;   // 중복 인스턴스 → Destroy 예약됨, 초기화 건너뜀
        DontDestroyOnLoad(this.gameObject);
#if UNITY_EDITOR
        // MPPM(Multiplayer Play Mode) 가상 플레이어는 Library/VP/ 밑에서 실행된다.
        // 클론들도 각자 BGM을 틀면 같은 곡이 반 박자 어긋나게 겹쳐 "2배속"처럼 들리므로,
        // 클론은 통째로 음소거하고 메인 에디터만 소리 낸다(빌드에는 포함되지 않는 코드).
        if (Application.dataPath.Contains("/Library/VP/"))
            AudioListener.volume = 0f;
#endif
        BuildMaps();
        BuildAudioSources();
        // 볼륨 적용은 Start(한 프레임 뒤)로 — AudioMixer.SetFloat는 Awake 프레임엔 안 먹는 Unity 버그.
    }

  
   

    // AudioMixer는 Awake/첫 프레임엔 SetFloat가 적용되지 않는다(Unity 버그) → 한 프레임 뒤 저장 볼륨 적용.
    // (런타임 드래그는 정상 적용되나, 시작 시 로드값이 안 먹어 재시작하면 소리가 다시 커지는 문제 해결.)
    IEnumerator Start()
    {
        yield return null;
        if (Instance == this) LoadVolumes();
    }

    // ── 초기화 ───────────────────────────────────────────

    void BuildMaps()
    {
        if (_library == null)
        {
            Debug.LogError("[SoundManager] SoundLibrary가 연결되지 않았습니다.");
            return;
        }
        foreach (var e in _library.sfxEntries) _sfxMap[e.type]  = e.clips;
        foreach (var e in _library.bgmEntries) _bgmMap[e.phase] = e.clip;
    }

    // BGM·2D SFX는 매니저에 직접, 3D SFX는 위치별 자식 소스 voice 풀. 모두 시작 시 1회 생성.
    void BuildAudioSources()
    {
        if (_mixer == null)
        {
            Debug.LogError("[SoundManager] AudioMixer가 연결되지 않았습니다.");
            return;
        }

        var sfxGroup = _mixer.FindMatchingGroups("SFX")[0];

        _sfx2D = gameObject.AddComponent<AudioSource>();
        _sfx2D.outputAudioMixerGroup = sfxGroup;
        _sfx2D.spatialBlend = 0f;       // 2D (거리 무관)
        _sfx2D.playOnAwake  = false;

        // 3D는 소스마다 '자기 위치(transform)'가 있어야 동시 발음이 안 섞임 → 자식 오브젝트로 voice 수만큼.
        // PlayClipAtPoint(clip, pos) 대신 풀을 쓰는 이유:
        //   ① PlayClipAtPoint는 호출마다 임시 오브젝트를 생성/파괴 → 3D가 동시 많으면 GC 부담.
        //   ② 그 임시 소스는 믹서그룹 미연결 → SFX 볼륨 슬라이더가 3D엔 안 먹음.
        //   ③ 핸들이 없어 StopAllSFX로 멈출 수 없음.
        // → 시작 시 voice 수만큼 만들어 두고(믹서그룹 연결) round-robin 재사용.
        //   트레이드오프: 동시 발음이 voice 수를 넘으면 오래된 소스를 재사용 → 그 소리는 위치 공유(필요 시 _sfx3DVoices↑).
        _sfx3D = new AudioSource[Mathf.Max(1, _sfx3DVoices)];
        for (int i = 0; i < _sfx3D.Length; i++)
        {
            var go = new GameObject($"SFX3D_{i}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.outputAudioMixerGroup = sfxGroup;
            src.spatialBlend = 1f;      // 3D (위치 기준)
            ApplySpatialRange(src);
            src.playOnAwake  = false;
            _sfx3D[i] = src;
        }

        _bgmSource = gameObject.AddComponent<AudioSource>();
        _bgmSource.outputAudioMixerGroup = _mixer.FindMatchingGroups("BGM")[0];
        _bgmSource.loop        = true;
        _bgmSource.playOnAwake = false;

        // 연타 전용 3D 채널: 새 타격이 직전 타격의 잔향을 '끊고' 재생 → 겹침/메아리 원천 차단.
        var tapGo = new GameObject("SFX3D_Tap");
        tapGo.transform.SetParent(transform);
        _tapSrc = tapGo.AddComponent<AudioSource>();
        _tapSrc.outputAudioMixerGroup = sfxGroup;
        _tapSrc.spatialBlend = 1f;
        ApplySpatialRange(_tapSrc);
        _tapSrc.playOnAwake  = false;
    }

    /// <summary>
    /// 3D 소스의 거리 감쇠. 유니티 기본값(min 1 · max 500 · Logarithmic)은 이 게임에 맞지 않는다 —
    /// AudioListener가 Main Camera에 있고 카메라는 플레이어에서 12유닛 떨어져 있어서,
    /// 기본값이면 '내 발밑' 소리조차 8% 볼륨이고 맵 반대편 이벤트(발화·석상 낙하)는 사실상 무음이 된다.
    /// min을 카메라 거리보다 크게 잡아 내 주변은 원음으로, max는 맵 대각선(경복궁 30x20 → 약 38) 밖에서 소멸하도록.
    /// </summary>
    static void ApplySpatialRange(AudioSource src)
    {
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = kSfx3DMinDistance;
        src.maxDistance = kSfx3DMaxDistance;
    }

    AudioSource _tapSrc;



    void LoadVolumes()
    {
        SetBGMVolume(PlayerPrefs.GetFloat("BGMVolume", 0.8f));
        SetSFXVolume(PlayerPrefs.GetFloat("SFXVolume", 1.0f));
    }

    // ── BGM ──────────────────────────────────────────────

    /// <summary>게임 페이즈 BGM으로 crossfade. 같은 곡이면 무시.</summary>
    public void SetPhase(GamePhase phase)
    {
        if (_bgmMap.TryGetValue(phase, out var clip))
            PlayBGM(clip, true);
    }

    /// <summary>지정 클립으로 crossfade(라이브러리 미등록 곡도 가능). 같은 곡이면 무시.</summary>
    public void PlayBGM(AudioClip clip) => PlayBGM(clip, true);

    /// <summary>지정 클립으로 crossfade. loop=false면 한 번만 재생한다.</summary>
    public void PlayBGM(AudioClip clip, bool loop)
    {
        if (clip == null) return;

        if (_bgmSource.clip == clip)
        {
            _bgmSource.loop = loop;
            if (!_bgmSource.isPlaying)
                _bgmSource.Play();
            return;
        }

        _bgmTween?.Kill();
        var seq = DOTween.Sequence();
        if (_bgmSource.isPlaying)
            seq.Append(_bgmSource.DOFade(0f, _bgmFadeDuration));       // 기존 곡 fade-out
        seq.AppendCallback(() =>
            {
                _bgmSource.clip   = clip;
                _bgmSource.loop   = loop;
                _bgmSource.volume = 0f;
                _bgmSource.Play();
            })
           .Append(_bgmSource.DOFade(kBgmVolume, _bgmFadeDuration));   // 새 곡 fade-in
        _bgmTween = seq;
    }

 

    /// <summary>BGM 즉시 정지.</summary>
    public void StopBGM()
    {
        _bgmTween?.Kill();
        _bgmSource.Stop();
    }

    /// <summary>BGM을 fade-out 후 정지.</summary>
    public void StopBGMFade()
    {
        _bgmTween?.Kill();
        _bgmTween = _bgmSource.DOFade(0f, _bgmFadeDuration)
            .OnComplete(() =>
            {
                _bgmSource.Stop();
                _bgmSource.volume = kBgmVolume;   // 다음 재생을 위해 복원
            });
    }

    // ── SFX ──────────────────────────────────────────────

    /// <summary>2D 효과음 (거리 무관).</summary>
    public void PlaySFX(SFXType type) => PlaySFX(PickClip(type));

    /// <summary>2D 효과음 — 클립 직접 지정(미등록 1회성).</summary>
    public void PlaySFX(AudioClip clip)
    {
        if (clip != null) _sfx2D.PlayOneShot(clip);   // 한 소스로 겹쳐 재생
    }

    /// <summary>3D 효과음 (월드 위치 기준 — 멀면 작게).</summary>
    public void PlaySFXAt(SFXType type, Vector3 worldPos) => PlaySFXAt(PickClip(type), worldPos);

    /// <summary>3D 효과음 — 클립 직접 지정. 다음 3D 소스를 그 위치로 옮겨 PlayOneShot(round-robin).</summary>
    public void PlaySFXAt(AudioClip clip, Vector3 worldPos) => PlaySFXAt(clip, worldPos, 1f);

    /// <summary>3D 효과음 + 피치(0.9~1.1 랜덤 등) — 반복음 단조로움 방지.</summary>
    public void PlaySFXAt(SFXType type, Vector3 worldPos, float pitch) => PlaySFXAt(PickClip(type), worldPos, pitch);

    public void PlaySFXAt(AudioClip clip, Vector3 worldPos, float pitch)
    {
        if (clip == null) return;
        var src = _sfx3D[_sfx3DIndex];
        _sfx3DIndex = (_sfx3DIndex + 1) % _sfx3D.Length;
        src.transform.position = worldPos;
        src.pitch = pitch;   // round-robin이라 이전 피치 잔재 방지 위해 매번 지정
        src.PlayOneShot(clip);
    }

    /// <summary>모든 SFX 즉시 정지 (2D + 3D + 루프 전부).</summary>
    public void StopAllSFX()
    {
        _sfx2D.Stop();
        foreach (var s in _sfx3D) s.Stop();
        StopAllLoops();
    }

    // SoundLibrary에서 랜덤 클립 선택
    AudioClip PickClip(SFXType type)
    {
        if (!_sfxMap.TryGetValue(type, out var clips) || clips.Length == 0) return null;
        return clips[Random.Range(0, clips.Length)];
    }

    /// <summary>라이브러리에 이 타입의 클립이 연결되어 있는가 — 호출부가 '연결 전엔 기존 합성음 폴백' 분기에 쓴다.</summary>
    public bool HasClip(SFXType type)
        => _sfxMap.TryGetValue(type, out var clips) && clips.Length > 0;

    // ── 루프 SFX ─────────────────────────────────────────
    // 원샷(PlayOneShot)과 달리 켜짐/꺼짐 상태가 있는 지속음 — 타입당 소스 1개를 만들어 두고 재사용.
    // 시작·정지 모두 짧게 fade해서 '뚝' 끊기는 소리를 막는다.

    const float kLoopFadeSeconds = 0.6f;
    readonly Dictionary<SFXType, AudioSource> _loops = new();
    readonly HashSet<SFXType> _stoppingLoops = new();   // fade-out 중(isPlaying이지만 곧 꺼질) 표시

    /// <summary>2D 루프 시작(거리 무관 — 날씨 앰비언스 등). 이미 도는 중이면 무시하므로 매 프레임 불러도 안전.</summary>
    public void PlayLoop(SFXType type) => PlayLoopInternal(type, null);

    /// <summary>3D 루프 시작/위치 갱신. 이미 도는 중이면 위치만 옮긴다 — 퍼레이드 카처럼 움직이는 음원 추적용.</summary>
    public void PlayLoopAt(SFXType type, Vector3 worldPos) => PlayLoopInternal(type, worldPos);

    void PlayLoopInternal(SFXType type, Vector3? worldPos)
    {
        var src = GetOrCreateLoopSource(type);
        if (src == null) return;   // 클립 미연결 — 조용히 무시(연결되면 그때부터 들림)

        src.spatialBlend = worldPos.HasValue ? 1f : 0f;
        if (worldPos.HasValue) src.transform.position = worldPos.Value;

        // fade-out 도중 다시 켜진 경우(isPlaying이지만 곧 Stop 예약됨) — 예약을 취소하고 볼륨을 되살린다.
        // 이 처리가 없으면 '정지 0.6초 안에 재요청'이 무시된 채 그대로 꺼져버린다(날씨 짧은 재전환 등).
        bool rescuing = _stoppingLoops.Remove(type);
        if (src.isPlaying && !rescuing) return;

        src.DOKill();
        if (!src.isPlaying) { src.volume = 0f; src.Play(); }
        src.DOFade(1f, kLoopFadeSeconds);
    }

    /// <summary>해당 루프를 fade-out 후 정지. 안 돌고 있으면 무시.</summary>
    public void StopLoop(SFXType type)
    {
        if (!_loops.TryGetValue(type, out var src) || src == null || !src.isPlaying) return;
        _stoppingLoops.Add(type);
        src.DOKill();
        src.DOFade(0f, kLoopFadeSeconds)
           .OnComplete(() => { src.Stop(); _stoppingLoops.Remove(type); });
    }

    /// <summary>루프 전부 즉시 정지 — 씬 전환·라운드 리셋 안전망.</summary>
    public void StopAllLoops()
    {
        _stoppingLoops.Clear();
        foreach (var kv in _loops)
        {
            if (kv.Value == null) continue;
            kv.Value.DOKill();
            kv.Value.Stop();
        }
    }

    // 타입당 전용 자식 소스(SFX 믹서그룹·3D 감쇠 설정 포함). 클립이 라이브러리에 없으면 null.
    AudioSource GetOrCreateLoopSource(SFXType type)
    {
        if (_loops.TryGetValue(type, out var src) && src != null)
        {
            // 클립 여러 개 등록 타입도 루프는 한 곡을 유지 — 재생 중 랜덤 교체는 위화감이 크다.
            if (src.clip == null) src.clip = PickClip(type);
            return src.clip != null ? src : null;
        }

        var clip = PickClip(type);
        if (clip == null) return null;

        var go = new GameObject($"SFXLoop_{type}");
        go.transform.SetParent(transform);
        src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = _mixer.FindMatchingGroups("SFX")[0];
        src.loop = true;
        src.playOnAwake = false;
        src.clip = clip;
        ApplySpatialRange(src);   // 3D로 쓸 때의 감쇠 — 2D(spatialBlend 0)면 무시됨
        _loops[type] = src;
        return src;
    }

    // ── 볼륨 (AudioMixer logarithmic scale) ──────────────

    /// <summary>BGM 볼륨 (0~1). 믹서 BGMVolume에 log 변환. PlayerPrefs 저장.</summary>
    public void SetBGMVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("BGMVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("BGMVolume", linear);
    }

    /// <summary>SFX 볼륨 (0~1). 믹서 SFXVolume에 log 변환. PlayerPrefs 저장.</summary>
    public void SetSFXVolume(float linear)
    {
        linear = Mathf.Max(linear, 0.0001f);
        _mixer.SetFloat("SFXVolume", Mathf.Log10(linear) * 20f);
        PlayerPrefs.SetFloat("SFXVolume", linear);
    }

    /// <summary>볼륨 설정(PlayerPrefs)을 디스크에 보장 저장. UI가 설정 닫을 때 호출.
    /// SetFloat은 메모리 캐시만 갱신하므로, 드래그마다 말고 닫기/종료 시 1회 호출(I/O 폭주 방지).</summary>
    public void SaveVolumes() => PlayerPrefs.Save();

    void OnApplicationQuit() => SaveVolumes();
    void OnApplicationPause(bool paused) { if (paused) SaveVolumes(); }
}

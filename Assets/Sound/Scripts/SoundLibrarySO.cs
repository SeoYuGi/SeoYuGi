using UnityEngine;
using UnityEngine.Audio;

/// <summary>
/// 모든 오디오 클립을 한 곳에서 관리하는 ScriptableObject.
/// Project 창에서 Create → Sound → Library 로 생성.
/// SoundManager가 Awake에서 이 SO를 읽어 Dictionary로 빌드함.
/// </summary>
[CreateAssetMenu(menuName = "Sound/Library", fileName = "SoundLibrary")]
public class SoundLibrarySO : ScriptableObject
{
    [System.Serializable]
    public struct SFXEntry
    {
        public SFXType     type;
        public AudioClip[] clips;   // 여러 개면 재생 시 랜덤 선택
    }

    [System.Serializable]
    public struct BGMEntry
    {
        public GamePhase  phase;
        public AudioClip  clip;     // 페이즈당 1개
    }

    [Header("효과음 (SFX) — type 하나당 clips 배열 등록. 여러 개면 재생 시 랜덤 선택.")]
    public SFXEntry[] sfxEntries;

    [Header("배경음 (BGM) — 게임 페이즈가 바뀔 때 자동 crossfade.")]
    public BGMEntry[] bgmEntries;
}

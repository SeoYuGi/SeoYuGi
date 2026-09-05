using UnityEngine;

/// <summary>
/// Resources/UI 텍스처 → 스프라이트 로더. 생성 이미지의 캔버스 여백을 크롭해 UI에 맞춘다.
/// 텍스처가 없으면 null — 호출부는 플랫 컬러 폴백 유지.
/// </summary>
public static class UISkin
{
    /// <summary>crop01 = 0..1 정규화 크롭 (x, y, w, h — y는 아래 원점). null이면 전체.</summary>
    public static Sprite Load(string path, Rect? crop01 = null)
    {
        var tex = Resources.Load<Texture2D>(path);
        if (tex == null) return null;
        var r = crop01 ?? new Rect(0f, 0f, 1f, 1f);
        return Sprite.Create(tex,
            new Rect(r.x * tex.width, r.y * tex.height, r.width * tex.width, r.height * tex.height),
            new Vector2(0.5f, 0.5f));
    }

    /// <summary>버튼 플레이트 — 검정 캔버스 키잉+내용 크롭 (2026-09-05 "투명 png가 아니야?").
    /// 밴드 크롭만으론 프레임 둘레 검정이 남았다 — LoadKeyed가 검정→투명 + 내용 바운딩 크롭까지 처리.</summary>
    public static Sprite ButtonPlate()
    {
        var tex = SeoYuGi.BattleView.BattleHud.LoadKeyed("UI/Frame_ButtonWide");
        if (tex == null) return null;
        return Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
    }

    /// <summary>슬롯 프레임 — 가장자리 소폭 크롭.</summary>
    public static Sprite SlotFrame() => Load("UI/Frame_LobbySlot", new Rect(0.02f, 0.02f, 0.96f, 0.96f));
}

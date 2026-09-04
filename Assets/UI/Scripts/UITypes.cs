public enum UIType { HUD = 0, Popup = 1, System = 2 }

public static class UITypes
{
    public static int GetSortingOrder(UIType t) => t switch
    {
        UIType.HUD    => 10,
        UIType.Popup  => 30,
        UIType.System => 100,   // 연결 끊김·에러·로딩 — 항상 최상단
        _             => 0,
    };
}

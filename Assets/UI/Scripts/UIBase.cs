using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public abstract class UIBase : MonoBehaviour
{
    protected Dictionary<Type, UnityEngine.Object[]> _objects = new Dictionary<Type, UnityEngine.Object[]>();

    public abstract void Init();

    protected void Bind<T>(Type type) where T : UnityEngine.Object
    {
        string[] names = Enum.GetNames(type);
        UnityEngine.Object[] objects = new UnityEngine.Object[names.Length];
        _objects.Add(typeof(T), objects);

        for (int i = 0; i < names.Length; i++)
        {
            if (typeof(T) == typeof(GameObject))
                objects[i] = FindChildGO(gameObject, names[i]);
            else
                objects[i] = FindChild<T>(gameObject, names[i]);

            if (objects[i] == null)
                Debug.Log($"Failed to bind({names[i]})");
        }
    }

    protected T Get<T>(int idx) where T : UnityEngine.Object
    {
        if (_objects.TryGetValue(typeof(T), out UnityEngine.Object[] objects) == false)
            return null;

        return objects[idx] as T;
    }

    // type: "Click" | "Drag" | "BeginDrag" | "EndDrag" | "Drop"
    public static void BindEvent(GameObject go, Action<PointerEventData> action, string type = "Click")
    {
        UIEventHandler evt = go.GetComponent<UIEventHandler>() ?? go.AddComponent<UIEventHandler>();

        switch (type)
        {
            case "Click":
                evt.OnClickHandler -= action;
                evt.OnClickHandler += action;
                break;
            case "Drag":
                evt.OnDragHandler -= action;
                evt.OnDragHandler += action;
                break;
            case "BeginDrag":
                evt.OnBeginDragHandler -= action;
                evt.OnBeginDragHandler += action;
                break;
            case "EndDrag":
                evt.OnEndDragHandler -= action;
                evt.OnEndDragHandler += action;
                break;
            case "Drop":
                evt.OnDropHandler -= action;
                evt.OnDropHandler += action;
                break;
        }
    }

    // ── Helpers (GameObjectUtils 인라인) ─────────────────────────────
    static T FindChild<T>(GameObject root, string name) where T : UnityEngine.Object
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.GetComponent<T>();
        return null;
    }

    static GameObject FindChildGO(GameObject root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t.gameObject;
        return null;
    }
}

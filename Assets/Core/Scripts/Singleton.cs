using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    /*
     * 코드리뷰:
     * 지연초기화 주의하기
     * 
     */
    
    public static T Instance { get; private set; }

    protected virtual void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this as T;
        //돈디스토이는 그거 쓰는 곳에서 해주기.
        //DontDestroyOnLoad(gameObject);
    }
}

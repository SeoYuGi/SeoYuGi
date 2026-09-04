using UnityEngine;

// Addressables 전환 시: new AddressablesProvider() 한 줄 교체
public class ResourcesProvider : IAssetProvider
{
    public GameObject Load(string path) => Resources.Load<GameObject>(path);
}

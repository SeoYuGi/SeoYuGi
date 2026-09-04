using UnityEngine;

public interface IAssetProvider
{
    GameObject Load(string path);
}

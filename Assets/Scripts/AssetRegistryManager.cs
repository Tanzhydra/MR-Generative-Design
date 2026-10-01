using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class AssetMapping {
    public string assetId;
    public GameObject prefab;
}

public class AssetRegistryManager : MonoBehaviour
{
    public static AssetRegistryManager Instance { get; private set; }

    [Header("Katalog Aset 3D (Otomatis Masuk Memori)")]
    public List<AssetMapping> assetList = new List<AssetMapping>();
    private Dictionary<string, GameObject> assetDictionary = new Dictionary<string, GameObject>();

    private void Awake()
    {
        if (Instance == null) {
            Instance = this;
        } else {
            Destroy(gameObject);
            return;
        }

        foreach (var item in assetList)
        {
            if (!assetDictionary.ContainsKey(item.assetId))
            {
                assetDictionary.Add(item.assetId, item.prefab);
            }
        }
        Debug.Log($"<color=magenta>[Asset Registry]</color> Berhasil memuat {assetDictionary.Count} aset ke memori.");
    }

    public bool TryGetPrefab(string assetId, out GameObject prefab)
    {
        return assetDictionary.TryGetValue(assetId, out prefab);
    }
}
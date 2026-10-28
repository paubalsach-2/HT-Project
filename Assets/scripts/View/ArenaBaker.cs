#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Converteix l'escena generada per HexBoard en assets reals (malles, materials,
// textures, terreny) i desa tota l'arena com a prefab: Assets/Baked/Arena.prefab
public static class ArenaBaker
{
    const string Folder = "Assets/Baked";
    static int counter;

    [MenuItem("Tools/Bake Arena")]
    public static void Bake()
    {
        var hb = Object.FindFirstObjectByType<HexBoard>();
        if (hb == null) { Debug.LogError("[ArenaBaker] No hi ha cap HexBoard a l'escena."); return; }

        hb.Generate();
        Transform root = hb.transform.Find("Generated");
        if (root == null) { Debug.LogError("[ArenaBaker] No s'ha trobat l'objecte 'Generated'."); return; }

        MakeFolders();
        counter = 0;

        // Els objectes generats porten DontSave: cal treure-ho per poder desar el prefab
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            t.gameObject.hideFlags = HideFlags.None;

        // Materials (i les seves textures)
        foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                PersistMaterial(m);

        // Malles
        foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
            Persist(mf.sharedMesh, "Meshes", "asset");
        foreach (var mc in root.GetComponentsInChildren<MeshCollider>(true))
            Persist(mc.sharedMesh, "Meshes", "asset");

        // Terreny
        foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
        {
            var td = terrain.terrainData;
            if (td == null) continue;

            // Es llegeix el mapa de capes (herba/roca) abans de desar, perquè no es perdi
            int ar = td.alphamapResolution;
            float[,,] alpha = td.GetAlphamaps(0, 0, ar, ar);
            foreach (var layer in td.terrainLayers)
            {
                if (layer == null) continue;
                Persist(layer.diffuseTexture, "Textures", "asset");
                Persist(layer.normalMapTexture, "Textures", "asset");
                Persist(layer.maskMapTexture, "Textures", "asset");
                Persist(layer, "Terrain", "terrainlayer");
            }
            Persist(td, "Terrain", "asset");

            // Es torna a aplicar el mapa de capes sobre l'asset ja desat
            td.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(td);
        }

        AssetDatabase.SaveAssets();

        string prefabPath = AssetDatabase.GenerateUniqueAssetPath(Folder + "/Arena.prefab");
        PrefabUtility.SaveAsPrefabAsset(root.gameObject, prefabPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[ArenaBaker] Fet. Prefab creat a " + prefabPath);
    }

    static void PersistMaterial(Material m)
    {
        if (m == null || HasPath(m)) return;
        foreach (var n in m.GetTexturePropertyNames())
        {
            var tex = m.GetTexture(n);
            if (tex != null) Persist(tex, "Textures", "asset");
        }
        Persist(m, "Materials", "mat");
    }

    static bool HasPath(Object o)
    {
        return !string.IsNullOrEmpty(AssetDatabase.GetAssetPath(o));
    }

    static void Persist(Object o, string sub, string ext)
    {
        if (o == null || HasPath(o)) return;
        string n = string.IsNullOrEmpty(o.name) ? o.GetType().Name : o.name;
        foreach (char c in System.IO.Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        string path = AssetDatabase.GenerateUniqueAssetPath(Folder + "/" + sub + "/" + n + "_" + (counter++) + "." + ext);
        AssetDatabase.CreateAsset(o, path);
    }

    static void MakeFolders()
    {
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Baked");
        foreach (var s in new[] { "Materials", "Textures", "Meshes", "Terrain" })
            if (!AssetDatabase.IsValidFolder(Folder + "/" + s)) AssetDatabase.CreateFolder(Folder, s);
    }
}
#endif

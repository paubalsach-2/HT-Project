using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class HexBoard : MonoBehaviour
{
    [Header("Hexàgons")]
    public float radius = 1f;
    public float tileHeight = 0.2f;
    public float gap = 0.04f;           // separació visual entre hexàgons

    [Header("Tauler")]
    public int columns = 7;
    public int rows = 8;
    public int benchSlots = 9;
    public bool enemyBench = true;

    [Header("Plataforma")]
    public Vector3 platformSize = new Vector3(20f, 1f, 26f);

    [Header("Colors")]
    public Color colorA = new Color(0.30f, 0.32f, 0.38f);
    public Color colorB = new Color(0.22f, 0.24f, 0.30f);
    public Color benchColor = new Color(0.45f, 0.35f, 0.20f);
    public Color platformColor = new Color(0.55f, 0.55f, 0.58f);

    Mesh hexMesh;

    [ContextMenu("Generate Board")]
    public void Generate()
    {
        // esborra el que hi hagi
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying) Destroy(transform.GetChild(i).gameObject);
            else DestroyImmediate(transform.GetChild(i).gameObject);
        }

        hexMesh = BuildHexMesh(radius - gap, tileHeight);
        float w = Mathf.Sqrt(3f) * radius;     // amplada cara a cara
        float rowStep = 1.5f * radius;         // distància vertical entre files

        // Plataforma
        var plat = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plat.name = "Platform";
        plat.transform.SetParent(transform, false);
        plat.transform.localScale = platformSize;
        plat.transform.localPosition = new Vector3(0, -platformSize.y / 2f - 0.01f, 0);
        SetColor(plat, platformColor);

        // Tauler (centrat)
        float boardW = columns * w + w / 2f;
        float boardD = (rows - 1) * rowStep;
        Transform board = new GameObject("Board").transform;
        board.SetParent(transform, false);

        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                float x = c * w + (r % 2) * w / 2f - boardW / 2f + w / 2f;
                float z = r * rowStep - boardD / 2f;
                CreateTile($"Tile_{c}_{r}", board, new Vector3(x, 0, z), (c + r) % 2 == 0 ? colorA : colorB);
            }

        // Banquetes
        float benchZ = boardD / 2f + 3.5f;
        CreateBench("PlayerBench", -benchZ, w);
        if (enemyBench) CreateBench("EnemyBench", benchZ, w);
    }

    void CreateBench(string name, float z, float w)
    {
        Transform bench = new GameObject(name).transform;
        bench.SetParent(transform, false);
        float total = benchSlots * w;
        for (int i = 0; i < benchSlots; i++)
        {
            float x = i * w - total / 2f + w / 2f;
            CreateTile($"{name}_Slot_{i}", bench, new Vector3(x, 0, z), benchColor);
        }
    }

    void CreateTile(string name, Transform parent, Vector3 pos, Color col)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = hexMesh;
        go.AddComponent<MeshRenderer>();
        go.AddComponent<MeshCollider>().sharedMesh = hexMesh;
        SetColor(go, col);
    }

    // Hexàgon "pointy-top" amb gruix, origen a la cara superior (y = 0)
    Mesh BuildHexMesh(float r, float h)
    {
        var m = new Mesh { name = "Hex" };
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        var uv = new System.Collections.Generic.List<Vector2>();

        Vector3[] top = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * (60f * i + 30f);
            top[i] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
        }

        // cara superior
        v.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < 6; i++) { v.Add(top[i]); uv.Add(new Vector2(top[i].x / (2 * r) + 0.5f, top[i].z / (2 * r) + 0.5f)); }
        for (int i = 0; i < 6; i++) { t.Add(0); t.Add(1 + (i + 1) % 6); t.Add(1 + i); }

        // costats
        for (int i = 0; i < 6; i++)
        {
            Vector3 a = top[i], b = top[(i + 1) % 6];
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(b + Vector3.down * h); v.Add(a + Vector3.down * h);
            uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 0));
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }

        // cara inferior
        int bs = v.Count;
        v.Add(Vector3.down * h); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < 6; i++) { v.Add(top[i] + Vector3.down * h); uv.Add(Vector2.zero); }
        for (int i = 0; i < 6; i++) { t.Add(bs); t.Add(bs + 1 + i); t.Add(bs + 1 + (i + 1) % 6); }

        m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    void SetColor(GameObject go, Color c)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        Material mat = rp != null && rp.defaultMaterial != null
            ? new Material(rp.defaultMaterial)
            : new Material(Shader.Find("Standard"));
        mat.color = c; // funciona a Built-in i URP
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", c);
        go.GetComponent<Renderer>().sharedMaterial = mat;
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () => { if (this != null) Generate(); };
#endif
    }
}
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
public class HexBoard : MonoBehaviour
{
    [Header("Hexàgons")]
    public float radius = 1f;
    public float tileHeight = 0.2f;
    public float gap = 0.04f;

    [Header("Tauler")]
    public int columns = 7;
    public int rows = 8;
    public int benchSlots = 9;
    public bool enemyBench = true;

    [Header("Plataforma")]
    public Vector3 platformSize = new Vector3(20f, 1f, 26f);

    [Header("Què generar")]
    public bool buildStructures = true;   // parets, columnes, escales, rampes
    public bool buildRoof = true;         // pèrgola / sostre
    public bool roofSlats = false;        // false = sense llistons sobre el camp (es veu l'arena)
    public bool roofCastsShadows = false; // false = el sostre no tapa l'arena amb ombra
    public bool buildTorches = true;      // torxes + llums + partícules
    public bool buildTerrain = true;
    public bool buildVegetation = true;
    public bool saveInScene = false;      // false = es regenera cada cop (no pesa a GitHub)

    [Header("Entorn")]
    public int seed = 7;
    public Vector3 terrainSize = new Vector3(160f, 25f, 160f);
    public int treeCount = 90;
    public int rockCount = 30;
    public int flowerCount = 150;

    [Header("Assets: Japanese Garden Pack")]
    public bool usePrefabs = true;                     // false = torna als arbres de primitives
    public string pathFilter = "Japanese";             // la ruta del prefab ha de contenir aquest text
    public string[] treeKeywords = { "Sakura", "Maple", "Pine" };
    public string[] rockKeywords = { "Rock" };
    public string[] toriiKeywords = { "Torii" };
    public GameObject[] treePrefabs;                   // si és buit, es busquen sols (només a l'Editor)
    public GameObject[] rockPrefabs;
    public GameObject toriiPrefab;
    public bool placeTorii = true;
    public float toriiYaw = 0f;
    public float toriiScale = 1f;
    public Vector2 treeScaleRange = new Vector2(0.8f, 1.3f);
    public Vector2 rockScaleRange = new Vector2(1f, 2f);

    [Header("Llum (Built-in / URP)")]
    public float sunIntensity = 1.2f;
    public float torchIntensity = 3f;
    [Header("Llum (HDRP)")]
    public float hdrpSunLux = 10000f;
    public float hdrpTorchLumens = 3000f;

    [Header("Colors")]
    public Color colorA = new Color(0.80f, 0.82f, 0.95f);
    public Color colorB = new Color(0.58f, 0.60f, 0.75f);
    public Color benchColor = new Color(0.95f, 0.72f, 0.45f);
    public Color platformColor = new Color(0.80f, 0.80f, 0.82f);

    Mesh hexMesh;
    Material matTileA, matTileB, matBench, matPlatform, matStone, matWood, matBark, matLeaf, matRock;
    Material[] flowerMats;
    Texture2D texStone, texWood, texGrass, texRock;

    struct Opening
    {
        public float c, w, sill, top;
        public Opening(float c, float w, float sill, float top) { this.c = c; this.w = w; this.sill = sill; this.top = top; }
    }

    // ---------------------------------------------------------------- cicle de vida
    void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) { UnityEditor.EditorApplication.delayCall += DelayedGenerate; return; }
#endif
        Generate();
    }

    void OnValidate()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) UnityEditor.EditorApplication.delayCall += DelayedGenerate;
#endif
    }

    void DelayedGenerate() { if (this != null && !Application.isPlaying) Generate(); }

    // ---------------------------------------------------------------- generació
    [ContextMenu("Generate Board")]
    public void Generate()
    {
        for (int i = transform.childCount - 1; i >= 0; i--) Kill(transform.GetChild(i).gameObject);

        Random.InitState(seed);
        BuildMaterials();
        hexMesh = BuildHexMesh(radius - gap, tileHeight);

        Transform root = NewGroup("Generated", transform);
        float w = Mathf.Sqrt(3f) * radius;
        float rowStep = 1.5f * radius;
        float boardD = (rows - 1) * rowStep;
        float benchZ = boardD / 2f + 3.5f;

        // Plataforma
        Transform platG = NewGroup("Platform", root);
        Prim(PrimitiveType.Cube, "Platform", platG, new Vector3(0, -platformSize.y / 2f - 0.01f, 0), platformSize, matPlatform);

        // Tauler
        Transform board = NewGroup("Board", root);
        float boardW = columns * w + w / 2f;
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < columns; c++)
            {
                float x = c * w + (r % 2) * w / 2f - boardW / 2f + w / 2f;
                float z = r * rowStep - boardD / 2f;
                CreateTile($"Tile_{c}_{r}", board, new Vector3(x, 0, z), (c + r) % 2 == 0 ? matTileA : matTileB);
            }

        // Banquetes
        CreateBench("PlayerBench", root, -benchZ, w);
        if (enemyBench) CreateBench("EnemyBench", root, benchZ, w);

        if (buildStructures) BuildStructures(NewGroup("Structures", root));
        if (buildTorches) BuildTorches(NewGroup("Torches", root));
        BuildLighting(NewGroup("Lighting", root));
        BuildBoardDust(NewGroup("Effects", root), boardD, boardW);

#if UNITY_EDITOR
        if (usePrefabs) AutoFindPrefabs();
#endif
        Transform env = NewGroup("Environment", root);
        if (buildTerrain) BuildTerrain(env);
        if (buildVegetation) BuildVegetation(env);

        if (!saveInScene)
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.DontSave;
    }

    [ContextMenu("Setup Camera")]
    public void SetupCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        cam.transform.position = new Vector3(0, 18, -14);
        cam.transform.rotation = Quaternion.Euler(55, 0, 0);
    }

    // ---------------------------------------------------------------- estructures
    void BuildStructures(Transform s)
    {
        float hx = platformSize.x / 2f, hz = platformSize.z / 2f;
        float wallH = 1.6f, wallT = 0.4f;

        // Parets laterals amb obertura per a la rampa i finestres
        foreach (int sg in new[] { -1, 1 })
            Wall($"SideWall_{sg}", s, true, sg * (hx - 0.2f), -hz, hz, wallH, wallT, matStone,
                new Opening(0, 4f, 0, wallH),
                new Opening(-6f, 2.5f, 0.5f, 1.2f), new Opening(6f, 2.5f, 0.5f, 1.2f),
                new Opening(-10f, 2.5f, 0.5f, 1.2f), new Opening(10f, 2.5f, 0.5f, 1.2f));

        // Parets dels extrems amb porta per a les escales i finestres
        foreach (int sg in new[] { -1, 1 })
            Wall($"EndWall_{sg}", s, false, sg * (hz - 0.2f), -hx, hx, wallH, wallT, matStone,
                new Opening(0, 4f, 0, wallH),
                new Opening(-6f, 3f, 0.5f, 1.2f), new Opening(6f, 3f, 0.5f, 1.2f));

        // Escales (extrems) i rampes (laterals)
        int steps = Mathf.RoundToInt(platformSize.y / 0.25f);
        Stairs("Stairs_Front", s, 0, hz, 1, 4f, steps, 0.25f, 0.6f, matStone);
        Stairs("Stairs_Back", s, 0, -hz, -1, 4f, steps, 0.25f, 0.6f, matStone);
        Ramp("Ramp_Right", s, 1, 4f, matWood);
        Ramp("Ramp_Left", s, -1, 4f, matWood);

        // Columnes
        float cx = hx - 1f;
        float[] czs = { -(hz - 1f), -4f, 4f, hz - 1f };
        foreach (int sg in new[] { -1, 1 })
            foreach (float cz in czs)
                Column($"Column_{sg}_{cz}", s, new Vector3(sg * cx, 0, cz));

        // Sostre: pèrgola oberta (bigues + llistons), sense ombra per defecte
        if (buildRoof)
        {
            float by = 5.9f;
            foreach (int sg in new[] { -1, 1 })
            {
                RoofPiece(Prim(PrimitiveType.Cube, $"BeamSide_{sg}", s, new Vector3(sg * cx, by, 0), new Vector3(0.8f, 0.6f, hz * 2f - 1f), matWood));
                RoofPiece(Prim(PrimitiveType.Cube, $"BeamGate_{sg}", s, new Vector3(0, by, sg * (hz - 1f)), new Vector3(cx * 2f + 1f, 0.6f, 0.8f), matWood));
            }
            if (roofSlats)
                for (float z = -(hz - 2f); z <= hz - 2f + 0.01f; z += 3f)
                    RoofPiece(Prim(PrimitiveType.Cube, $"Slat_{z}", s, new Vector3(0, by + 0.35f, z), new Vector3(cx * 2f + 1f, 0.15f, 0.3f), matWood, null, false));
        }
    }

    void RoofPiece(GameObject g)
    {
        if (roofCastsShadows) return;
        var r = g.GetComponent<Renderer>();
        if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
    }

    void Column(string name, Transform p, Vector3 pos)
    {
        Transform c = NewGroup(name, p);
        c.localPosition = pos;
        Prim(PrimitiveType.Cube, "Base", c, new Vector3(0, 0.15f, 0), new Vector3(1.3f, 0.3f, 1.3f), matStone);
        Prim(PrimitiveType.Cylinder, "Shaft", c, new Vector3(0, 2.8f, 0), new Vector3(0.9f, 2.5f, 0.9f), matStone);
        Prim(PrimitiveType.Cube, "Capital", c, new Vector3(0, 5.45f, 0), new Vector3(1.3f, 0.3f, 1.3f), matStone);
    }

    void Stairs(string name, Transform p, float x, float zEdge, int dir, float width, int steps, float stepH, float stepD, Material m)
    {
        float total = platformSize.y;
        for (int i = 0; i < steps - 1; i++)
        {
            float hb = total - (i + 1) * stepH;
            if (hb <= 0.01f) break;
            Prim(PrimitiveType.Cube, $"{name}_{i}", p,
                new Vector3(x, -total + hb / 2f, zEdge + dir * stepD * (i + 0.5f)),
                new Vector3(width, hb, stepD), m);
        }
    }

    void Ramp(string name, Transform p, int sign, float width, Material m)
    {
        float drop = platformSize.y, L = 5f, th = 0.3f;
        float ang = Mathf.Atan2(drop, L);
        float len = Mathf.Sqrt(L * L + drop * drop);
        float cx = sign * (platformSize.x / 2f + L / 2f);
        var pos = new Vector3(cx - sign * Mathf.Sin(ang) * th / 2f, -drop / 2f - Mathf.Cos(ang) * th / 2f, 0);
        Prim(PrimitiveType.Cube, name, p, pos, new Vector3(len, th, width), m, new Vector3(0, 0, -sign * ang * Mathf.Rad2Deg));
    }

    // Paret amb obertures (portes/finestres) construïda per peces
    void Wall(string name, Transform p, bool alongZ, float fixedC, float from, float to, float h, float t, Material m, params Opening[] ops)
    {
        var list = new List<Opening>(ops);
        list.Sort((a, b) => a.c.CompareTo(b.c));
        float cur = from;
        foreach (var o in list)
        {
            float s = o.c - o.w / 2f, e = o.c + o.w / 2f;
            if (s > cur) Piece(cur, s, 0, h);
            if (o.sill > 0) Piece(s, e, 0, o.sill);
            if (o.top < h) Piece(s, e, o.top, h);
            cur = e;
        }
        if (cur < to) Piece(cur, to, 0, h);

        void Piece(float a, float b, float y0, float y1)
        {
            float len = b - a, mid = (a + b) / 2f, my = (y0 + y1) / 2f;
            Vector3 size = alongZ ? new Vector3(t, y1 - y0, len) : new Vector3(len, y1 - y0, t);
            Vector3 pos = alongZ ? new Vector3(fixedC, my, mid) : new Vector3(mid, my, fixedC);
            Prim(PrimitiveType.Cube, name + "_part", p, pos, size, m);
        }
    }

    // ---------------------------------------------------------------- torxes, llum i partícules
    void BuildTorches(Transform t)
    {
        float hx = platformSize.x / 2f, hz = platformSize.z / 2f;
        float cx = hx - 1f;
        float[] czs = { -(hz - 1f), -4f, 4f, hz - 1f };
        foreach (int sg in new[] { -1, 1 })
            foreach (float cz in czs)
            {
                Vector3 pos = new Vector3(sg * (cx - 0.75f), 3f, cz);
                Prim(PrimitiveType.Cylinder, "TorchStick", t, pos, new Vector3(0.1f, 0.35f, 0.1f), matWood, null, false);
                Flame(t, pos + Vector3.up * 0.4f);

                var lg = new GameObject("TorchLight");
                lg.transform.SetParent(t, false);
                lg.transform.localPosition = pos + Vector3.up * 0.6f;
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 9f; l.shadows = LightShadows.None;
                l.color = new Color(1f, 0.6f, 0.25f);
                SetIntensity(l, torchIntensity, hdrpTorchLumens);
            }
    }

    void Flame(Transform parent, Vector3 pos)
    {
        var g = new GameObject("Flame");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localEulerAngles = new Vector3(-90, 0, 0);
        var ps = g.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 0.8f; main.startSpeed = 0.6f; main.startSize = 0.28f;
        main.startColor = new Color(1f, 0.6f, 0.15f, 0.9f);
        var em = ps.emission; em.rateOverTime = 25f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 8f; sh.radius = 0.05f;
        var sol = ps.sizeOverLifetime; sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1, 1, 0));
        var col = ps.colorOverLifetime; col.enabled = true;
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0f), 1f) },
                   new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = gr;
        g.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat(new Color(1f, 0.6f, 0.15f));
    }

    void BuildBoardDust(Transform parent, float boardD, float boardW)
    {
        var g = new GameObject("MagicDust");
        g.transform.SetParent(parent, false);
        g.transform.localPosition = new Vector3(0, 0.6f, 0);
        var ps = g.AddComponent<ParticleSystem>();
        var main = ps.main;
        main.startLifetime = 6f; main.startSpeed = 0.05f; main.startSize = 0.07f; main.maxParticles = 200;
        main.prewarm = true;
        main.startColor = new Color(0.7f, 0.85f, 1f, 0.7f);
        var em = ps.emission; em.rateOverTime = 15f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(boardW, 0.2f, boardD + 2f);
        var n = ps.noise; n.enabled = true; n.strength = 0.3f; n.frequency = 0.4f;
        g.GetComponent<ParticleSystemRenderer>().sharedMaterial = ParticleMat(new Color(0.7f, 0.85f, 1f));
    }

    void BuildLighting(Transform t)
    {
        var go = new GameObject("Sun");
        go.transform.SetParent(t, false);
        go.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        var sun = go.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.88f);
        sun.shadows = LightShadows.Soft;
        SetIntensity(sun, sunIntensity, hdrpSunLux);
        RenderSettings.sun = sun;
    }

    // ---------------------------------------------------------------- terreny i vegetació
    float Height(float x, float z)
    {
        float d = Mathf.Sqrt(x * x + z * z);
        float n = Mathf.PerlinNoise(x * 0.05f + seed * 10f, z * 0.05f + seed * 10f);
        float n2 = Mathf.PerlinNoise(x * 0.2f + seed * 3f, z * 0.2f + seed * 3f);
        float hills = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(26f, 34f, d)) * (1.5f + n2 * 1.5f);
        float mount = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(40f, 75f, d)) * (6f + n * 14f);
        return hills + mount;
    }

    float GroundY(float x, float z) { return -platformSize.y + (buildTerrain ? Height(x, z) : 0f); }

    void BuildTerrain(Transform parent)
    {
        int res = 129;
        var td = new TerrainData();
        td.heightmapResolution = res;
        td.size = terrainSize;

        var hm = new float[res, res];
        for (int y = 0; y < res; y++)
            for (int x = 0; x < res; x++)
            {
                float wx = (x / (float)(res - 1) - 0.5f) * terrainSize.x;
                float wz = (y / (float)(res - 1) - 0.5f) * terrainSize.z;
                hm[y, x] = Height(wx, wz) / terrainSize.y;
            }
        td.SetHeights(0, 0, hm);

        td.alphamapResolution = 128;
        td.terrainLayers = new[]
        {
            new TerrainLayer { diffuseTexture = texGrass, tileSize = new Vector2(8, 8) },
            new TerrainLayer { diffuseTexture = texRock,  tileSize = new Vector2(8, 8) }
        };
        int ar = td.alphamapResolution;
        var am = new float[ar, ar, 2];
        for (int y = 0; y < ar; y++)
            for (int x = 0; x < ar; x++)
            {
                float wx = (x / (float)(ar - 1) - 0.5f) * terrainSize.x;
                float wz = (y / (float)(ar - 1) - 0.5f) * terrainSize.z;
                float rock = Mathf.InverseLerp(4f, 9f, Height(wx, wz));
                am[y, x, 0] = 1f - rock;
                am[y, x, 1] = rock;
            }
        td.SetAlphamaps(0, 0, am);

        var tg = Terrain.CreateTerrainGameObject(td);
        tg.name = "Terrain";
        tg.transform.SetParent(parent, false);
        tg.transform.localPosition = new Vector3(-terrainSize.x / 2f, -platformSize.y, -terrainSize.z / 2f);
    }

    bool TryPoint(float minR, float maxR, out Vector3 p)
    {
        for (int i = 0; i < 40; i++)
        {
            float a = Random.value * Mathf.PI * 2f, r = Random.Range(minR, maxR);
            float x = Mathf.Cos(a) * r, z = Mathf.Sin(a) * r;
            if (Mathf.Abs(x) > 16f || Mathf.Abs(z) > 19f) { p = new Vector3(x, GroundY(x, z) - 0.05f, z); return true; }
        }
        p = Vector3.zero; return false;
    }

    void BuildVegetation(Transform parent)
    {
        Transform trees = NewGroup("Trees", parent), rocks = NewGroup("Rocks", parent), flowers = NewGroup("Flowers", parent);
        Vector3 p;

        bool hasTrees = usePrefabs && treePrefabs != null && treePrefabs.Length > 0;
        bool hasRocks = usePrefabs && rockPrefabs != null && rockPrefabs.Length > 0;

        for (int i = 0; i < treeCount; i++)
        {
            if (!TryPoint(18f, 55f, out p)) continue;
            if (hasTrees)
            {
                SpawnPrefab(treePrefabs[Random.Range(0, treePrefabs.Length)], trees, p,
                            Random.Range(0, 360f), Random.Range(treeScaleRange.x, treeScaleRange.y));
                continue;
            }
            float s = Random.Range(0.8f, 1.6f);
            Transform t = NewGroup("Tree", trees);
            t.localPosition = p;
            t.localRotation = Quaternion.Euler(0, Random.Range(0, 360f), 0);
            Prim(PrimitiveType.Cylinder, "Trunk", t, new Vector3(0, 1.5f * s, 0), new Vector3(0.5f * s, 1.5f * s, 0.5f * s), matBark);
            Prim(PrimitiveType.Sphere, "Leaves", t, new Vector3(0, 3.6f * s, 0), new Vector3(2.6f * s, 2.4f * s, 2.6f * s), matLeaf, null, false);
            Prim(PrimitiveType.Sphere, "Leaves2", t, new Vector3(0, 4.8f * s, 0), new Vector3(1.8f * s, 1.7f * s, 1.8f * s), matLeaf, null, false);
        }

        for (int i = 0; i < rockCount; i++)
        {
            if (!TryPoint(14f, 50f, out p)) continue;
            if (hasRocks)
            {
                SpawnPrefab(rockPrefabs[Random.Range(0, rockPrefabs.Length)], rocks, p + Vector3.down * 0.1f,
                            Random.Range(0, 360f), Random.Range(rockScaleRange.x, rockScaleRange.y));
                continue;
            }
            Prim(PrimitiveType.Sphere, "Rock", rocks, p + Vector3.up * 0.1f,
                new Vector3(Random.Range(1f, 2.5f), Random.Range(0.6f, 1.2f), Random.Range(1f, 2.2f)), matRock,
                new Vector3(0, Random.Range(0, 360f), 0));
        }

        // Torii a l'entrada de l'escala
        if (usePrefabs && placeTorii && toriiPrefab != null)
        {
            float tz = platformSize.z / 2f + 3.5f;
            SpawnPrefab(toriiPrefab, parent, new Vector3(0, GroundY(0, tz), tz), toriiYaw, toriiScale);
        }

        for (int i = 0; i < flowerCount; i++)
        {
            if (!TryPoint(16f, 35f, out p)) continue;
            Transform f = NewGroup("Flower", flowers);
            f.localPosition = p;
            Prim(PrimitiveType.Cylinder, "Stem", f, new Vector3(0, 0.2f, 0), new Vector3(0.04f, 0.2f, 0.04f), matLeaf, null, false);
            Prim(PrimitiveType.Sphere, "Head", f, new Vector3(0, 0.42f, 0), Vector3.one * 0.2f, flowerMats[Random.Range(0, flowerMats.Length)], null, false);
        }
    }

    GameObject SpawnPrefab(GameObject prefab, Transform parent, Vector3 pos, float yaw, float scale)
    {
        var g = Instantiate(prefab, parent);
        g.name = prefab.name;
        g.transform.localPosition = pos;
        g.transform.localRotation = Quaternion.Euler(0, yaw, 0);
        g.transform.localScale = prefab.transform.localScale * scale;
        return g;
    }

#if UNITY_EDITOR
    // Busca els prefabs del pack dins del projecte (només si les llistes són buides)
    void AutoFindPrefabs()
    {
        if (treePrefabs == null || treePrefabs.Length == 0) treePrefabs = FindPrefabs(treeKeywords);
        if (rockPrefabs == null || rockPrefabs.Length == 0) rockPrefabs = FindPrefabs(rockKeywords);
        if (toriiPrefab == null)
        {
            var t = FindPrefabs(toriiKeywords);
            if (t.Length > 0) toriiPrefab = t[0];
        }
    }

    GameObject[] FindPrefabs(string[] keys)
    {
        var list = new List<GameObject>();
        foreach (var guid in UnityEditor.AssetDatabase.FindAssets("t:Prefab"))
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            if (!path.StartsWith("Assets/")) continue;
            if (!string.IsNullOrEmpty(pathFilter) && path.IndexOf(pathFilter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            string n = System.IO.Path.GetFileNameWithoutExtension(path);
            foreach (var k in keys)
                if (n.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (go != null) list.Add(go);
                    break;
                }
        }
        return list.ToArray();
    }

    [ContextMenu("Reset Prefab Lists")]
    void ResetPrefabLists() { treePrefabs = null; rockPrefabs = null; toriiPrefab = null; Generate(); }
#endif

    // ---------------------------------------------------------------- tauler / banqueta
    void CreateBench(string name, Transform parent, float z, float w)
    {
        Transform bench = NewGroup(name, parent);
        float total = benchSlots * w;
        for (int i = 0; i < benchSlots; i++)
            CreateTile($"{name}_Slot_{i}", bench, new Vector3(i * w - total / 2f + w / 2f, 0, z), matBench);
    }

    void CreateTile(string name, Transform parent, Vector3 pos, Material mat)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = pos;
        go.AddComponent<MeshFilter>().sharedMesh = hexMesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        go.AddComponent<MeshCollider>().sharedMesh = hexMesh;
    }

    Mesh BuildHexMesh(float r, float h)
    {
        var m = new Mesh { name = "Hex" };
        var v = new List<Vector3>();
        var t = new List<int>();
        var uv = new List<Vector2>();

        Vector3[] top = new Vector3[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.Deg2Rad * (60f * i + 30f);
            top[i] = new Vector3(Mathf.Cos(a) * r, 0, Mathf.Sin(a) * r);
        }

        v.Add(Vector3.zero); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < 6; i++) { v.Add(top[i]); uv.Add(new Vector2(top[i].x / (2 * r) + 0.5f, top[i].z / (2 * r) + 0.5f)); }
        for (int i = 0; i < 6; i++) { t.Add(0); t.Add(1 + (i + 1) % 6); t.Add(1 + i); }

        for (int i = 0; i < 6; i++)
        {
            Vector3 a = top[i], b = top[(i + 1) % 6];
            int s = v.Count;
            v.Add(a); v.Add(b); v.Add(b + Vector3.down * h); v.Add(a + Vector3.down * h);
            uv.Add(new Vector2(0, 1)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(0, 0));
            t.Add(s); t.Add(s + 1); t.Add(s + 2);
            t.Add(s); t.Add(s + 2); t.Add(s + 3);
        }

        int bs = v.Count;
        v.Add(Vector3.down * h); uv.Add(new Vector2(0.5f, 0.5f));
        for (int i = 0; i < 6; i++) { v.Add(top[i] + Vector3.down * h); uv.Add(Vector2.zero); }
        for (int i = 0; i < 6; i++) { t.Add(bs); t.Add(bs + 1 + i); t.Add(bs + 1 + (i + 1) % 6); }

        m.SetVertices(v); m.SetTriangles(t, 0); m.SetUVs(0, uv);
        m.RecalculateNormals(); m.RecalculateBounds();
        return m;
    }

    // ---------------------------------------------------------------- materials i textures
    void BuildMaterials()
    {
        texStone = Tex("T_Stone", new Color(0.55f, 0.55f, 0.58f), new Color(0.85f, 0.85f, 0.88f), 4f, 4f, true);
        texWood = Tex("T_Wood", new Color(0.35f, 0.22f, 0.12f), new Color(0.65f, 0.45f, 0.25f), 2f, 14f, false);
        texGrass = Tex("T_Grass", new Color(0.18f, 0.40f, 0.12f), new Color(0.35f, 0.62f, 0.20f), 10f, 10f, false);
        texRock = Tex("T_Rock", new Color(0.30f, 0.29f, 0.28f), new Color(0.60f, 0.58f, 0.55f), 6f, 6f, false);

        matTileA = Mat(colorA, texStone, 1f, 0.35f);
        matTileB = Mat(colorB, texStone, 1f, 0.35f);
        matBench = Mat(benchColor, texWood, 1f, 0.2f);
        matPlatform = Mat(platformColor, texStone, 6f, 0.15f);
        matStone = Mat(Color.white, texStone, 2f, 0.15f);
        matWood = Mat(Color.white, texWood, 1f, 0.2f);
        matBark = Mat(new Color(0.7f, 0.55f, 0.4f), texWood, 1f, 0.1f);
        matLeaf = Mat(new Color(0.6f, 0.95f, 0.5f), texGrass, 1f, 0.1f);
        matRock = Mat(Color.white, texRock, 1f, 0.1f);
        flowerMats = new[]
        {
            Mat(new Color(1f, 0.3f, 0.4f)), Mat(new Color(1f, 0.9f, 0.3f)),
            Mat(new Color(0.8f, 0.5f, 1f)), Mat(Color.white)
        };
    }

    Material Mat(Color c, Texture2D tex = null, float tiling = 1f, float smooth = 0.2f)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        Material m = (rp != null && rp.defaultMaterial != null) ? new Material(rp.defaultMaterial) : new Material(Shader.Find("Standard"));
        if (m.HasProperty("_Color")) m.color = c;
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
        if (tex != null)
            foreach (var n in new[] { "_BaseMap", "_BaseColorMap", "_MainTex" })
                if (m.HasProperty(n)) { m.SetTexture(n, tex); m.SetTextureScale(n, new Vector2(tiling, tiling)); }
        return m;
    }

    Material ParticleMat(Color c)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp != null && rp.defaultParticleMaterial != null) return rp.defaultParticleMaterial;
        Shader s = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (s == null) s = Shader.Find("Particles/Standard Unlit");
        if (s == null) s = Shader.Find("HDRP/Unlit");
        var m = s != null ? new Material(s) : new Material(Shader.Find("Standard"));
        if (m.HasProperty("_UnlitColor")) m.SetColor("_UnlitColor", c);
        return m;
    }

    // Textura procedural que es repeteix sense costures
    Texture2D Tex(string name, Color a, Color b, float fu, float fv, bool bricks, int size = 128)
    {
        var t = new Texture2D(size, size, TextureFormat.RGBA32, true) { name = name, wrapMode = TextureWrapMode.Repeat };
        var px = new Color[size * size];
        float off = seed * 7f + 20f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n1 = TileNoise(u, v, fu, fv, off);
                float n2 = TileNoise(u, v, fu * 4f, fv * 4f, off + 50f);
                Color c = Color.Lerp(a, b, n1 * 0.6f + n2 * 0.4f);
                if (bricks && (x % 32 < 2 || y % 32 < 2)) c *= 0.6f;
                px[y * size + x] = c;
            }
        t.SetPixels(px); t.Apply();
        return t;
    }

    float TileNoise(float u, float v, float fu, float fv, float off)
    {
        float n00 = Mathf.PerlinNoise(u * fu + off, v * fv + off);
        float n10 = Mathf.PerlinNoise((u - 1f) * fu + off, v * fv + off);
        float n01 = Mathf.PerlinNoise(u * fu + off, (v - 1f) * fv + off);
        float n11 = Mathf.PerlinNoise((u - 1f) * fu + off, (v - 1f) * fv + off);
        return n00 * (1 - u) * (1 - v) + n10 * u * (1 - v) + n01 * (1 - u) * v + n11 * u * v;
    }

    // ---------------------------------------------------------------- utilitats
    Transform NewGroup(string name, Transform parent)
    {
        var g = new GameObject(name).transform;
        g.SetParent(parent, false);
        return g;
    }

    GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale, Material mat,
                    Vector3? euler = null, bool collider = true)
    {
        var g = GameObject.CreatePrimitive(type);
        g.name = name;
        g.transform.SetParent(parent, false);
        g.transform.localPosition = pos;
        g.transform.localScale = scale;
        if (euler.HasValue) g.transform.localEulerAngles = euler.Value;
        g.GetComponent<Renderer>().sharedMaterial = mat;
        if (!collider) { var c = g.GetComponent<Collider>(); if (c != null) Kill(c); }
        return g;
    }

    void Kill(Object o)
    {
        if (Application.isPlaying) Destroy(o); else DestroyImmediate(o);
    }

    // Intensitat de llum segons pipeline (HDRP usa lux/lumens)
    void SetIntensity(Light l, float normal, float hdrp)
    {
        var rp = GraphicsSettings.currentRenderPipeline;
        if (rp != null && rp.GetType().Name.Contains("HDRender"))
        {
            var type = System.Type.GetType("UnityEngine.Rendering.HighDefinition.HDAdditionalLightData, Unity.RenderPipelines.HighDefinition.Runtime");
            if (type != null)
            {
                var data = l.gameObject.GetComponent(type);
                if (data == null) data = l.gameObject.AddComponent(type);
                var m = type.GetMethod("SetIntensity", new[] { typeof(float) });
                if (m != null) { m.Invoke(data, new object[] { hdrp }); return; }
            }
        }
        l.intensity = normal;
    }
}
using UnityEngine;

// Representació 3D d'una unitat del banc: prisma de la seva forma + estrelles dalt.
public class UnitView : MonoBehaviour
{
    public UnitInstance Unit { get; private set; }

    Transform body, starsRoot;
    Collider bodyCol;
    float radius;
    int shownStar = -1;

    public static UnitView Create(UnitInstance unit, float tileRadius)
    {
        var go = new GameObject("Unit_" + unit.Def.Name);
        var v = go.AddComponent<UnitView>();
        v.Unit = unit;
        v.radius = tileRadius;

        var b = new GameObject("Body");
        b.transform.SetParent(go.transform, false);
        b.AddComponent<MeshFilter>().sharedMesh = UnitShapes.Prism(unit.Def.Shape);
        b.AddComponent<MeshRenderer>().sharedMaterial = UnitShapes.MakeMaterial(UnitShapes.ColorOf(unit.Def));
        var col = b.AddComponent<BoxCollider>(); // la malla va de (-1,0,-1) a (1,1,1)
        col.center = new Vector3(0f, 0.5f, 0f);
        col.size = new Vector3(2f, 1f, 2f);
        v.body = b.transform;
        v.bodyCol = col;

        var s = new GameObject("Stars");
        s.transform.SetParent(go.transform, false);
        v.starsRoot = s.transform;
        return v;
    }

    // Mentre s'arrossega, el collider s'apaga perquè el raig del ratolí no el toqui
    public void SetDragging(bool on)
    {
        if (bodyCol != null) bodyCol.enabled = !on;
    }

    // Actualitza mida i estrelles (la mida creix amb el tier i les estrelles)
    public void Refresh()
    {
        if (Unit == null || shownStar == Unit.Star) return;
        shownStar = Unit.Star;

        float r = radius * (0.7f + 0.08f * Unit.Def.Tier) * (1f + 0.18f * (Unit.Star - 1));
        float h = r * 1.8f;
        body.localScale = new Vector3(r, h, r);

        foreach (Transform c in starsRoot) Destroy(c.gameObject);
        float sr = radius * 0.2f, gap = sr * 2.4f;
        starsRoot.localPosition = new Vector3(0f, h + sr * 1.6f, 0f);
        var gold = new Color(1f, 0.82f, 0.25f);
        for (int i = 0; i < Unit.Star; i++)
        {
            var sph = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(sph.GetComponent<Collider>());
            sph.transform.SetParent(starsRoot, false);
            sph.transform.localScale = Vector3.one * sr * 2f;
            sph.transform.localPosition = new Vector3((i - (Unit.Star - 1) * 0.5f) * gap, 0f, 0f);
            sph.GetComponent<MeshRenderer>().sharedMaterial = UnitShapes.MakeMaterial(gold);
        }
    }
}
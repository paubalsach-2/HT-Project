using UnityEngine;
using UnityEngine.EventSystems;

// Càmera estil TFT / LoL.
// Idea: la càmera no es mou "a pèl". Es treballa amb un PUNT DE FOCUS sobre el terra (el que
// queda al centre de la pantalla) i una ALÇADA (zoom). La posició de la càmera es calcula
// a partir d'aquests dos valors, i la rotació de l'escena no es toca mai.
//
// Controls:
//   Fletxes ............. moure la càmera (Shift = més ràpid)
//   WASD ................ desactivat per defecte (useWasdPan); D és la drecera de reroll
//   Vora de la pantalla . moure la càmera (desactivat a l'editor per defecte)
//   Roda del ratolí ..... zoom suau cap al cursor
//   Clic del mig + arros. agafar el terra i arrossegar
//   Espai (mantenir) .... centrar-se en el personatge (en deixar-lo anar, la càmera es queda allà)
//   Y ................... bloquejar / desbloquejar la càmera sobre el personatge
public class TFTCameraController : MonoBehaviour
{
    [Header("Objectiu (El teu personatge)")]
    public Transform target;

    [Header("Estat")]
    [Tooltip("Càmera bloquejada sobre el personatge (tecla Y)")]
    public bool isLocked = false;
    [Tooltip("Suavitat de tots els moviments (més alt = més sec)")]
    public float smoothFollowSpeed = 12f;

    [Header("Moviment")]
    public float panSpeed = 20f;
    [Tooltip("Multiplicador mantenint Shift")]
    public float fastPanMultiplier = 2f;
    [Tooltip("Com més lluny (zoom out), més ràpid es mou")]
    public bool scalePanWithZoom = true;
    public bool useEdgeScrolling = true;
    [Tooltip("Moure la càmera amb les lletres W A S D (apagat: D és la drecera de reroll)")]
    public bool useWasdPan = false;
    public float panBorderThickness = 15f;
    [Tooltip("A l'editor el cursor surt sovint de la Game view i la càmera se'n va sola; per això està apagat")]
    public bool edgeScrollInEditor = false;
    [Tooltip("Arrossegar el terra amb el botó del mig")]
    public bool dragWithMiddleMouse = true;

    [Header("Límits del mapa (punt del terra al centre de la pantalla)")]
    public Vector2 panLimitX = new Vector2(-30f, 30f);
    public Vector2 panLimitZ = new Vector2(-30f, 30f);

    [Header("Zoom")]
    [Tooltip("Quant s'acosta/allunya a cada pas de la roda (0.12 = 12%)")]
    [Range(0.02f, 0.4f)] public float zoomStep = 0.12f;
    [Tooltip("El zoom s'acosta cap a on tens el cursor, no cap al centre")]
    public bool zoomToCursor = true;
    public float minY = 6f;
    public float maxY = 25f;
    [Tooltip("Alçada actual (es llegeix de l'escena al fer Play)")]
    public float cameraHeight = 15f;

    [Header("Terra")]
    [Tooltip("Alçada (Y) del terra del tauler")]
    public float groundY = 0f;

    Camera cam;
    Vector3 fwd;                    // direcció de mirada (fixa)
    Vector3 focus, targetFocus;     // punt del terra al centre de la pantalla (actual / desitjat)
    float height, targetHeight;     // alçada de la càmera (actual / desitjada)
    float refHeight;                // alçada d'inici, per escalar la velocitat
    bool dragging;
    Vector3 dragAnchor;

    // ---------------------------------------------------------------- inici
    void Awake()
    {
        cam = GetComponent<Camera>();
        if (cam == null) cam = Camera.main;

        fwd = transform.forward;
        if (fwd.y > -0.05f)
        {
            Debug.LogError("[TFTCameraController] La càmera ha d'estar inclinada cap avall per poder orientar-se sobre el terra.", this);
            enabled = false;
            return;
        }

        focus = targetFocus = ProjectToGround(transform.position);
        height = targetHeight = Mathf.Clamp(transform.position.y, minY, maxY);
        refHeight = Mathf.Max(0.1f, height - groundY);
        cameraHeight = height;
    }

    // ---------------------------------------------------------------- API pública
    // Porta la càmera a un punt del món (per exemple, per mirar el tauler d'un altre jugador)
    public void FocusOn(Vector3 worldPos, bool instant = false)
    {
        targetFocus = ClampFocus(ProjectToGround(worldPos));
        if (!instant) return;
        focus = targetFocus;
        transform.position = CamPos(focus, height);
    }

    public void SetLocked(bool locked) { isLocked = locked; }

    // ---------------------------------------------------------------- bucle
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        if (Input.GetKeyDown(KeyCode.Y)) isLocked = !isLocked;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        bool centering = isLocked || Input.GetKey(KeyCode.Space);

        HandleZoom(overUI, centering);

        if (centering)
        {
            dragging = false;
            if (target != null) targetFocus = ProjectToGround(target.position);
        }
        else
        {
            HandleKeyboardAndEdge(dt);
            HandleDrag(overUI);
            targetFocus = ClampFocus(targetFocus);
        }

        // suavitzat independent dels FPS; focus i alçada amb el mateix ritme, així el zoom
        // cap al cursor va en línia recta i el punt sota el cursor no es mou
        float k = 1f - Mathf.Exp(-Mathf.Max(0.1f, smoothFollowSpeed) * dt);
        focus = Vector3.Lerp(focus, targetFocus, k);
        height = Mathf.Lerp(height, targetHeight, k);
        cameraHeight = height;

        transform.position = CamPos(focus, height);
    }

    // ---------------------------------------------------------------- zoom
    void HandleZoom(bool overUI, bool centering)
    {
        if (overUI) return;
        float scroll = Input.GetAxis("Mouse ScrollWheel");   // ~0.1 per cada pas de la roda
        if (Mathf.Abs(scroll) < 0.0001f) return;

        float oldTarget = targetHeight;
        float newTarget = groundY + (oldTarget - groundY) * Mathf.Pow(1f - zoomStep, scroll * 10f);
        newTarget = Mathf.Clamp(newTarget, minY, maxY);

        float oldH = oldTarget - groundY;
        float newH = newTarget - groundY;
        targetHeight = newTarget;

        if (!zoomToCursor || centering || dragging || cam == null || !MouseInsideWindow()) return;
        if (oldH < 0.01f || newH < 0.01f) return;

        // El punt P del terra sota el cursor es queda quiet: el focus s'hi acosta/allunya en la mateixa proporció
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);
        Vector3 origin = CamPos(targetFocus, oldTarget);
        if (!GroundPoint(origin, ray.direction, out Vector3 p)) return;
        float ratio = newH / oldH;
        targetFocus = p + (targetFocus - p) * ratio;
    }

    // ---------------------------------------------------------------- moviment
    void HandleKeyboardAndEdge(float dt)
    {
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();
        Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;

        bool edge = useEdgeScrolling && !dragging && Application.isFocused
                    && (!Application.isEditor || edgeScrollInEditor)
                    && MouseInsideWindow();
        Vector3 m = Input.mousePosition;
        float b = panBorderThickness;

        Vector3 dir = Vector3.zero;
        bool wasd = useWasdPan;
        if ((wasd && Input.GetKey(KeyCode.W)) || Input.GetKey(KeyCode.UpArrow) || (edge && m.y >= Screen.height - b)) dir += forward;
        if ((wasd && Input.GetKey(KeyCode.S)) || Input.GetKey(KeyCode.DownArrow) || (edge && m.y <= b)) dir -= forward;
        if ((wasd && Input.GetKey(KeyCode.D)) || Input.GetKey(KeyCode.RightArrow) || (edge && m.x >= Screen.width - b)) dir += right;
        if ((wasd && Input.GetKey(KeyCode.A)) || Input.GetKey(KeyCode.LeftArrow) || (edge && m.x <= b)) dir -= right;

        if (dir.sqrMagnitude < 0.0001f) return;
        dir.Normalize();   // les diagonals no van més ràpid

        float speed = panSpeed;
        if (scalePanWithZoom) speed *= (targetHeight - groundY) / refHeight;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= fastPanMultiplier;

        targetFocus += dir * speed * dt;
    }

    // Arrossegar amb el botó del mig: el punt del terra que has agafat es queda sota el cursor
    void HandleDrag(bool overUI)
    {
        if (!dragWithMiddleMouse || cam == null) return;

        if (Input.GetMouseButtonDown(2) && !overUI && MouseGround(out dragAnchor)) dragging = true;
        if (!Input.GetMouseButton(2)) dragging = false;
        if (!dragging) return;

        if (MouseGround(out Vector3 p))
        {
            Vector3 delta = dragAnchor - p;
            delta.y = 0f;
            focus = ClampFocus(focus + delta);
            targetFocus = focus;   // sense suavitzat mentre s'arrossega: respon a l'instant
        }
    }

    // ---------------------------------------------------------------- geometria
    // Distància al llarg de la mirada fins al terra, per a una alçada donada
    float DistFor(float h) { return (h - groundY) / -fwd.y; }

    Vector3 CamPos(Vector3 f, float h) { return f - fwd * DistFor(h); }

    // Punt del terra que queda al centre de la pantalla si mirem des de 'p' en direcció fwd
    Vector3 ProjectToGround(Vector3 p)
    {
        float t = (groundY - p.y) / fwd.y;
        Vector3 g = p + fwd * t;
        g.y = groundY;
        return g;
    }

    bool GroundPoint(Vector3 origin, Vector3 dir, out Vector3 point)
    {
        point = origin;
        if (dir.y > -0.0001f) return false;
        point = origin + dir * ((groundY - origin.y) / dir.y);
        return true;
    }

    bool MouseGround(out Vector3 point)
    {
        Ray r = cam.ScreenPointToRay(Input.mousePosition);
        return GroundPoint(r.origin, r.direction, out point);
    }

    Vector3 ClampFocus(Vector3 f)
    {
        f.x = Mathf.Clamp(f.x, panLimitX.x, panLimitX.y);
        f.z = Mathf.Clamp(f.z, panLimitZ.x, panLimitZ.y);
        f.y = groundY;
        return f;
    }

    static bool MouseInsideWindow()
    {
        Vector3 m = Input.mousePosition;
        return m.x >= 0f && m.y >= 0f && m.x <= Screen.width && m.y <= Screen.height;
    }

    // Selecciona la càmera a l'editor per veure els límits (rectangle groc) i el punt de focus
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        var a = new Vector3(panLimitX.x, groundY, panLimitZ.x);
        var b = new Vector3(panLimitX.y, groundY, panLimitZ.x);
        var c = new Vector3(panLimitX.y, groundY, panLimitZ.y);
        var d = new Vector3(panLimitX.x, groundY, panLimitZ.y);
        Gizmos.DrawLine(a, b); Gizmos.DrawLine(b, c); Gizmos.DrawLine(c, d); Gizmos.DrawLine(d, a);

        if (Application.isPlaying)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(focus, 0.4f);
        }
    }
}
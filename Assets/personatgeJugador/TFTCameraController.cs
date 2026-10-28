using UnityEngine;

public class TFTCameraController : MonoBehaviour
{
    [Header("Objectiu (El teu personatge)")]
    public Transform target;

    [Tooltip("Alçada de la càmera. Es guarda sola al donar-li al Play, i canvia si fas zoom.")]
    public float cameraHeight = 15f;

    [Header("Estat de la Càmera")]
    [Tooltip("Marca-ho si vols començar la partida amb la càmera bloquejada (Tecla Y)")]
    public bool isLocked = false;
    public float smoothFollowSpeed = 12f;

    [Header("Moviment de Càmera Lliure")]
    public float panSpeed = 20f;
    public float panBorderThickness = 15f;
    public bool useEdgeScrolling = true;

    [Header("Límits del Mapa")]
    public Vector2 panLimitX = new Vector2(-30f, 30f);
    public Vector2 panLimitZ = new Vector2(-30f, 30f);

    [Header("Zoom")]
    public float scrollSpeed = 20f;
    public float minY = 6f;
    public float maxY = 25f;

    void Start()
    {
        // Guardem l'alçada inicial a la qual tens posada la càmera a l'escena
        cameraHeight = transform.position.y;
    }

    void Update()
    {
        // Prem Y per bloquejar o desbloquejar la càmera sobre el jugador
        if (Input.GetKeyDown(KeyCode.Y))
        {
            isLocked = !isLocked;
        }
    }

    void LateUpdate()
    {
        // 1. Zoom amb la roda del ratolí (funciona tant amb càmera fixa com lliure)
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.001f)
        {
            cameraHeight -= scroll * scrollSpeed * 100f * Time.deltaTime;
            cameraHeight = Mathf.Clamp(cameraHeight, minY, maxY);
        }

        // 2. Si està bloquejada (Y) o mantenim premut l'ESPAI -> Centrem exactament al jugador
        if (isLocked || Input.GetKey(KeyCode.Space))
        {
            if (target != null)
            {
                Vector3 centeredPos = GetCameraPositionCenteredOn(target.position);
                transform.position = Vector3.Lerp(transform.position, centeredPos, Time.deltaTime * smoothFollowSpeed);
            }
            // Sortim aquí: quan deixis anar l'Espai o la Y, la càmera es quedarà en aquest mateix lloc!
            return;
        }

        // 3. Moviment lliure des de la posició actual (on l'hagis deixat)
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        Vector3 forward = Vector3.Cross(right, Vector3.up).normalized;
        Vector3 moveDir = Vector3.zero;

        // Amunt / Avall
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) || (useEdgeScrolling && Input.mousePosition.y >= Screen.height - panBorderThickness))
        {
            moveDir += forward;
        }
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) || (useEdgeScrolling && Input.mousePosition.y <= panBorderThickness))
        {
            moveDir -= forward;
        }

        // Dreta / Esquerra
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) || (useEdgeScrolling && Input.mousePosition.x >= Screen.width - panBorderThickness))
        {
            moveDir += right;
        }
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) || (useEdgeScrolling && Input.mousePosition.x <= panBorderThickness))
        {
            moveDir -= right;
        }

        Vector3 pos = transform.position + moveDir * panSpeed * Time.deltaTime;
        pos.y = cameraHeight;

        // Límits del mapa perquè no s'escapi lluny
        pos.x = Mathf.Clamp(pos.x, panLimitX.x, panLimitX.y);
        pos.z = Mathf.Clamp(pos.z, panLimitZ.x, panLimitZ.y);

        transform.position = pos;
    }

    // Calcula la posició exacta de la càmera perquè el personatge quedi al centre mateix de la pantalla
    Vector3 GetCameraPositionCenteredOn(Vector3 targetPos)
    {
        Vector3 lookDir = transform.forward;

        // Evitem divisions per zero si la càmera mirés totalment horitzontal
        if (Mathf.Abs(lookDir.y) < 0.01f)
            lookDir.y = -0.5f;

        // Calculem quina distància enrere ha d'estar la càmera segons l'angle d'inclinació (X: 65) i l'alçada
        float heightDiff = cameraHeight - targetPos.y;
        float distanceAlongRay = heightDiff / -lookDir.y;

        return targetPos - lookDir * distanceAlongRay;
    }
}
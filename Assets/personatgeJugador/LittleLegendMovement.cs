using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class LittleLegendMovement : MonoBehaviour
{
    [Header("Configuració de Clic")]
    [Tooltip("Quina capa (Layer) és el terra del tauler")]
    public LayerMask groundLayer;

    [Tooltip("El Prefab de la teva animació de clic (el GIF convertit)")]
    public GameObject clickEffectPrefab;

    [Tooltip("Temps que triga a desaparèixer l'efecte de clic")]
    public float clickEffectDuration = 0.8f;

    [Header("Rotació i Animació")]
    public float rotationSpeed = 15f;
    public Animator characterAnimator;

    private NavMeshAgent agent;
    private Camera mainCam;

    // El clic dret ha començat sobre una unitat, el panell d'informació o la UI:
    // no ha de moure el personatge, ni tan sols si després es mou el cursor amb el botó premut.
    private bool holdBlocked;

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        mainCam = Camera.main;

        // Desactivem la rotació automàtica del NavMesh per fer un gir més ràpid i suau estil TFT
        agent.updateRotation = false;
    }

    void Update()
    {
        // Clic dret (1) = Moure's (igual que al TFT/LoL)
        // Si prefereixes clic esquerre, canvia el (1) per (0)
        // El clic dret sobre una unitat (o el panell / la UI) només serveix per veure'n les estadístiques
        // (UnitInspector); el personatge només es mou amb un clic dret al buit.
        if (Input.GetMouseButtonDown(1))
        {
            holdBlocked = UnitInspector.BlocksWorldClick;
            if (!holdBlocked) MoveToCursor(true);
        }
        // Si mantens premut el clic dret, el personatge segueix el cursor sense spamejar l'efecte
        else if (Input.GetMouseButton(1))
        {
            if (!holdBlocked && !UnitInspector.BlocksWorldClick) MoveToCursor(false);
        }
        else
        {
            holdBlocked = false;
        }

        HandleRotation();
        HandleAnimation();
    }

    void MoveToCursor(bool spawnEffect)
    {
        Ray ray = mainCam.ScreenPointToRay(Input.mousePosition);
        RaycastHit hit;

        // Llança un raig des de la càmera fins al terra
        if (Physics.Raycast(ray, out hit, 100f, groundLayer))
        {
            // Mou el personatge
            agent.SetDestination(hit.point);

            // Crea l'animació del clic una mica elevada (0.05f) perquè no es talli amb el terra
            if (spawnEffect && clickEffectPrefab != null)
            {
                Vector3 spawnPos = hit.point + new Vector3(0, 0.05f, 0);
                GameObject effect = Instantiate(clickEffectPrefab, spawnPos, clickEffectPrefab.transform.rotation);
                Destroy(effect, clickEffectDuration);
            }
        }
    }

    void HandleRotation()
    {
        // Si el personatge s'està movent, el girem cap a la direcció on va
        if (agent.velocity.sqrMagnitude > 0.1f)
        {
            Vector3 direction = agent.velocity.normalized;
            direction.y = 0; // Evitem que s'inclini cap amunt o avall
            if (direction != Vector3.zero)
            {
                Quaternion lookRotation = Quaternion.LookRotation(direction);
                transform.rotation = Quaternion.Slerp(transform.rotation, lookRotation, Time.deltaTime * rotationSpeed);
            }
        }
    }

    void HandleAnimation()
    {
        // Si tens un Animator al teu personatge, li envia si està corrent o parat
        if (characterAnimator != null)
        {
            bool isMoving = agent.velocity.sqrMagnitude > 0.1f;
            characterAnimator.SetBool("isRunning", isMoving);
        }
    }
}
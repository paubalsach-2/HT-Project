using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class TFTUnit : MonoBehaviour
{
    public enum Team { Player, Enemy }

    // Llista global amb totes les unitats del tauler
    public static List<TFTUnit> allUnits = new List<TFTUnit>();

    [Header("Equip i Estat")]
    public Team team = Team.Player;
    [Tooltip("Desmarca-ho si el personatge està a la banqueta (caselles taronges) perquè no lluiti")]
    public bool isOnBoard = true;

    [Header("Estadístiques de Combat")]
    public float maxHealth = 500f;
    public float currentHealth;
    public float attackDamage = 50f;
    [Tooltip("Distància d'atac: ~2 per cos a cos (Melee), ~6 o 8 per distància (Ranged)")]
    public float attackRange = 2.0f;
    [Tooltip("Atacs per segon (ex: 1.0 = un cop cada segon)")]
    public float attackSpeed = 1.0f;

    [Header("Animacions (Opcional)")]
    public Animator animator;

    public bool IsDead { get; private set; }

    private NavMeshAgent agent;
    private TFTUnit currentTarget;
    private float attackCooldown;

    // Guardem on estava col·locat per tornar-lo al lloc quan acabi la ronda
    private Vector3 startPosition;
    private Quaternion startRotation;

    void OnEnable()
    {
        if (!allUnits.Contains(this))
            allUnits.Add(this);
    }

    void OnDisable()
    {
        allUnits.Remove(this);
    }

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        currentHealth = maxHealth;
        SaveBoardPosition();
    }

    // Guarda la casella actual com a punt d'inici de la ronda
    public void SaveBoardPosition()
    {
        startPosition = transform.position;
        startRotation = transform.rotation;
    }

    // S'executa a cada frame NOMÉS durant la fase de combat (cridat pel TFTCombatManager)
    public void CombatUpdate()
    {
        if (IsDead || !isOnBoard) return;

        // 1. Si no tenim objectiu o ha mort, busquem l'enemic més proper
        if (currentTarget == null || currentTarget.IsDead || !currentTarget.isOnBoard)
        {
            currentTarget = FindClosestEnemy();
            if (currentTarget == null)
            {
                // No queden enemics vius: s'ha acabat el combat
                StopMoving();
                return;
            }
        }

        // 2. Calculem la distància fins a l'enemic
        float distance = Vector3.Distance(transform.position, currentTarget.transform.position);

        if (distance <= attackRange)
        {
            // Estem dins del rang: ens aturem i ataquem
            StopMoving();
            LookAtTarget(currentTarget.transform.position);

            attackCooldown -= Time.deltaTime;
            if (attackCooldown <= 0f)
            {
                Attack(currentTarget);
                attackCooldown = 1f / Mathf.Max(0.1f, attackSpeed);
            }
        }
        else
        {
            // Estem lluny: caminem cap a l'enemic
            MoveTowardsTarget(currentTarget.transform.position);
        }
    }

    TFTUnit FindClosestEnemy()
    {
        TFTUnit closest = null;
        float minDistance = Mathf.Infinity;

        foreach (TFTUnit unit in allUnits)
        {
            // Ignorem aliats, morts o unitats a la banqueta
            if (unit == this || unit.team == this.team || unit.IsDead || !unit.isOnBoard)
                continue;

            float dist = Vector3.Distance(transform.position, unit.transform.position);
            if (dist < minDistance)
            {
                minDistance = dist;
                closest = unit;
            }
        }
        return closest;
    }

    void MoveTowardsTarget(Vector3 targetPos)
    {
        if (agent.isOnNavMesh)
        {
            agent.isStopped = false;
            agent.SetDestination(targetPos);
        }

        if (animator != null)
            animator.SetBool("isWalking", true);
    }

    void StopMoving()
    {
        if (agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.ResetPath();
        }

        if (animator != null)
            animator.SetBool("isWalking", false);
    }

    void LookAtTarget(Vector3 targetPos)
    {
        Vector3 dir = (targetPos - transform.position).normalized;
        dir.y = 0f;
        if (dir != Vector3.zero)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * 15f);
        }
    }

    void Attack(TFTUnit target)
    {
        if (animator != null)
            animator.SetTrigger("attack");

        target.TakeDamage(attackDamage);
    }

    public void TakeDamage(float damage)
    {
        if (IsDead) return;

        currentHealth -= damage;
        if (currentHealth <= 0f)
        {
            Die();
        }
    }

    void Die()
    {
        IsDead = true;
        StopMoving();
        // Desactivem l'objecte (al final de la ronda el Manager el tornarà a activar)
        gameObject.SetActive(false);
    }

    // Torna el personatge a la seva casella inicial i el cura al 100%
    public void ResetUnit()
    {
        gameObject.SetActive(true);
        IsDead = false;
        currentHealth = maxHealth;
        currentTarget = null;
        attackCooldown = 0f;

        if (agent != null && agent.isOnNavMesh)
        {
            agent.Warp(startPosition);
            agent.ResetPath();
            agent.isStopped = true;
        }
        else
        {
            transform.position = startPosition;
        }

        transform.rotation = startRotation;

        if (animator != null)
            animator.SetBool("isWalking", false);
    }

    // Dibuixa un cercle a l'escena per veure el rang d'atac quan selecciones la unitat
    void OnDrawGizmosSelected()
    {
        Gizmos.color = (team == Team.Player) ? Color.cyan : Color.red;
        Gizmos.DrawWireSphere(transform.position, attackRange);
    }
}
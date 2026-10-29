using System.Collections.Generic;
using UnityEngine;

public enum CombatTeam { Player, Enemy }

// Una unitat durant un combat: vida, objectiu, moviment per la graella i atac.
// Treballa sobre una CÒPIA de la unitat del banc, així el banc del jugador no es toca.
// La barra de vida la dibuixa CombatManager (en pantalla); aquí només hi ha les dades visuals.
public sealed class CombatUnit
{
    public readonly UnitInstance Source;
    public readonly CombatTeam Team;
    public readonly CombatStats Stats;
    public readonly UnitView View;

    public float Hp { get; private set; }
    public bool Alive => Hp > 0f;
    public Vector2Int Coord { get; private set; }
    public CombatUnit Target => target;

    // Dades visuals de la barra
    public float DisplayHp { get; private set; }  // omplert que baixa suau
    public float GhostHp { get; private set; }    // estela blanca que triga una mica a baixar
    public float HitFlash { get; private set; }   // 1 = acaba de rebre un cop, baixa a 0
    public Vector3 BarAnchor => View.transform.position + Vector3.up * anchorHeight;

    // (unitat, dany) cada cop que rep dany
    public event System.Action<CombatUnit, float> Damaged;

    enum State { Idle, Moving, Attacking }

    const float HopHeight = 0.15f;   // salt visual en caminar
    const float AttackAnim = 0.25f;  // durada de l'animació d'atac
    const float LungeDist = 0.35f;   // quant s'inclina cap a l'objectiu en atacar

    readonly HexGrid grid;
    readonly List<CombatUnit> all;
    readonly Vector2Int[] nb = new Vector2Int[6];
    readonly float anchorHeight;

    State state;
    float stateTime, stateDuration;
    float cooldown;
    float ghostDelay;
    bool hitDone;
    CombatUnit target;
    Vector3 basePos, fromPos, toPos;

    public CombatUnit(UnitInstance source, CombatTeam team, UnitView view, HexGrid grid,
                      List<CombatUnit> all, Vector2Int coord, float tileRadius)
    {
        Source = source;
        Team = team;
        View = view;
        this.grid = grid;
        this.all = all;

        Stats = CombatStats.For(source.Def, source.Star);
        Hp = DisplayHp = GhostHp = Stats.MaxHp;

        Coord = coord;
        grid.Place(this, coord);
        basePos = grid.WorldPos(coord);
        view.transform.position = basePos;

        // punt (sobre el cap de la unitat) on s'ancora la barra de vida
        float h = 1f;
        var body = view.transform.Find("Body");
        if (body != null) h = body.localScale.y;
        anchorHeight = h + tileRadius * 1.1f;
    }

    // ---------------------------------------------------------------- bucle
    public void Tick(float dt)
    {
        if (!Alive) return;
        cooldown -= dt;

        switch (state)
        {
            case State.Moving: AdvanceMove(dt); break;
            case State.Attacking: AdvanceAttack(dt); break;
            default: Decide(); break;
        }
        UpdateVisuals(dt);
    }

    // Lliure: tria objectiu, ataca si hi arriba o s'hi acosta
    void Decide()
    {
        if (target == null || !target.Alive) target = FindTarget();
        if (target == null) return;

        int dist = HexGrid.Distance(Coord, target.Coord);
        if (dist <= Stats.Range)
        {
            if (cooldown <= 0f) StartAttack();
            return;
        }

        if (grid.TryFindStep(Coord, target.Coord, Stats.Range, out var next) || TryFallbackStep(out next))
            StartMove(next);
    }

    // L'enemic viu més proper (empat: el més proper a l'espai)
    CombatUnit FindTarget()
    {
        CombatUnit best = null;
        int bestD = int.MaxValue;
        float bestSq = float.MaxValue;
        Vector3 me = grid.WorldPos(Coord);
        for (int i = 0; i < all.Count; i++)
        {
            var u = all[i];
            if (u.Team == Team || !u.Alive) continue;
            int d = HexGrid.Distance(Coord, u.Coord);
            float sq = (grid.WorldPos(u.Coord) - me).sqrMagnitude;
            if (d < bestD || (d == bestD && sq < bestSq)) { best = u; bestD = d; bestSq = sq; }
        }
        return best;
    }

    // Si el camí complet està bloquejat, almenys intenta acostar-se una casella
    bool TryFallbackStep(out Vector2Int next)
    {
        next = Coord;
        int cur = HexGrid.Distance(Coord, target.Coord);
        int best = cur;
        int n = grid.GetNeighbors(Coord, nb);
        for (int i = 0; i < n; i++)
        {
            if (!grid.IsFree(nb[i])) continue;
            int d = HexGrid.Distance(nb[i], target.Coord);
            if (d < best) { best = d; next = nb[i]; }
        }
        return best < cur;
    }

    // ---------------------------------------------------------------- moviment
    void StartMove(Vector2Int next)
    {
        grid.Clear(Coord, this);
        Coord = next;
        grid.Place(this, next); // la casella queda reservada des del principi
        fromPos = View.transform.position;
        toPos = grid.WorldPos(next);
        state = State.Moving;
        stateTime = 0f;
        stateDuration = 1f / Mathf.Max(0.1f, Stats.MoveSpeed);
    }

    void AdvanceMove(float dt)
    {
        stateTime += dt;
        float t = Mathf.Clamp01(stateTime / stateDuration);
        Vector3 p = Vector3.Lerp(fromPos, toPos, t);
        p.y += Mathf.Sin(t * Mathf.PI) * HopHeight;
        View.transform.position = p;
        if (t >= 1f)
        {
            View.transform.position = toPos;
            basePos = toPos;
            state = State.Idle;
        }
    }

    // ---------------------------------------------------------------- atac
    void StartAttack()
    {
        state = State.Attacking;
        stateTime = 0f;
        stateDuration = Mathf.Min(AttackAnim, 0.5f / Stats.AttackSpeed);
        hitDone = false;
        cooldown = 1f / Mathf.Max(0.05f, Stats.AttackSpeed);
    }

    void AdvanceAttack(float dt)
    {
        stateTime += dt;
        float t = Mathf.Clamp01(stateTime / stateDuration);

        Vector3 dir = Vector3.zero;
        if (target != null)
        {
            dir = grid.WorldPos(target.Coord) - basePos;
            dir.y = 0f;
            dir = dir.normalized;
        }
        View.transform.position = basePos + dir * (Mathf.Sin(t * Mathf.PI) * LungeDist);

        if (!hitDone && t >= 0.5f)
        {
            hitDone = true;
            if (target != null && target.Alive) target.TakeDamage(Stats.Attack);
        }
        if (t >= 1f)
        {
            View.transform.position = basePos;
            state = State.Idle;
        }
    }

    public void TakeDamage(float amount)
    {
        if (!Alive) return;
        Hp -= amount;
        ghostDelay = 0.45f;
        HitFlash = 1f;
        Damaged?.Invoke(this, amount);
        if (Hp <= 0f) Die();
    }

    void Die()
    {
        Hp = 0f;
        grid.Clear(Coord, this);
        View.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- visuals de la barra
    void UpdateVisuals(float dt)
    {
        // l'omplert baixa suau cap a la vida real
        DisplayHp = Mathf.Lerp(DisplayHp, Hp, 1f - Mathf.Exp(-14f * dt));

        // l'estela blanca espera una mica i després es buida
        if (ghostDelay > 0f) ghostDelay -= dt;
        else GhostHp = Mathf.MoveTowards(GhostHp, Hp, Stats.MaxHp * 0.6f * dt);
        if (GhostHp < DisplayHp) GhostHp = DisplayHp;

        // petit "cop" de mida en rebre dany
        HitFlash = Mathf.MoveTowards(HitFlash, 0f, dt * 6f);
        View.transform.localScale = Vector3.one * (1f + 0.18f * HitFlash);
    }

    // ---------------------------------------------------------------- neteja
    public void Dispose()
    {
        if (View != null) UnityEngine.Object.Destroy(View.gameObject);
    }
}

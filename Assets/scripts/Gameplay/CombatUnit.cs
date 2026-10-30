using System;
using UnityEngine;

public enum CombatTeam { Player, Enemy }

// Una unitat durant un combat, VERSIÓ VISUAL: envolta una SimUnit (que decideix tot) i
// l'anima (caminar, atacar, barra de vida). No pren cap decisió de joc.
// Treballa sobre una CÒPIA de la unitat del banc, així el banc del jugador no es toca.
public sealed class CombatUnit
{
    public readonly UnitInstance Source;
    public readonly CombatTeam Team;
    public readonly CombatStats Stats;
    public readonly UnitView View;
    public readonly SimUnit Sim;

    public float Hp => Sim.Hp;
    public bool Alive => Sim.Alive;
    public float DamageDealt => Sim.DamageDealt;   // dany total fet (per a la pestanya Estadístiques)
    public HexCoord Coord => Sim.Coord;
    public CombatUnit Target { get; set; }         // l'assigna CombatManager a partir de la simulació

    // Dades visuals de la barra
    public float DisplayHp { get; private set; }   // omplert que baixa suau
    public float GhostHp { get; private set; }     // estela blanca que triga una mica a baixar
    public float HitFlash { get; private set; }    // 1 = acaba de rebre un cop, baixa a 0
    public Vector3 BarAnchor => View.transform.position + Vector3.up * anchorHeight;

    // (unitat, dany) cada cop que rep dany
    public event Action<CombatUnit, float> Damaged;

    enum State { Idle, Moving, Attacking }

    const float HopHeight = 0.15f;   // salt visual en caminar
    const float LungeDist = 0.35f;   // quant s'inclina cap a l'objectiu en atacar

    readonly BoardWorld world;
    readonly float anchorHeight;

    State state;
    float stateTime, stateDuration;
    float ghostDelay;
    Vector3 basePos, fromPos, toPos, lungeDir;

    public CombatUnit(SimUnit sim, UnitInstance source, CombatTeam team, UnitView view, BoardWorld world, float tileRadius)
    {
        Sim = sim;
        Source = source;
        Team = team;
        View = view;
        this.world = world;

        Stats = CombatStats.For(source.Def, source.Star);
        DisplayHp = GhostHp = Stats.MaxHp;

        basePos = world.WorldPos(sim.Coord);
        view.transform.position = basePos;

        // punt (sobre el cap de la unitat) on s'ancora la barra de vida
        float h = 1f;
        var body = view.transform.Find("Body");
        if (body != null) h = body.localScale.y;
        anchorHeight = h + tileRadius * 1.1f;
    }

    // ---------------------------------------------------------------- ordres de la simulació
    public void PlayMove(HexCoord to, float seconds)
    {
        if (state == State.Moving) basePos = toPos;
        else if (state == State.Attacking) View.transform.position = basePos;

        fromPos = View.transform.position;
        toPos = world.WorldPos(to);
        state = State.Moving;
        stateTime = 0f;
        stateDuration = Mathf.Max(0.05f, seconds);
    }

    public void PlayAttack(HexCoord targetCoord)
    {
        if (state == State.Moving)
        {
            View.transform.position = toPos;
            basePos = toPos;
        }
        lungeDir = world.WorldPos(targetCoord) - basePos;
        lungeDir.y = 0f;
        lungeDir = lungeDir.sqrMagnitude > 0.0001f ? lungeDir.normalized : Vector3.zero;

        state = State.Attacking;
        stateTime = 0f;
        stateDuration = SimConfig.AttackAnimTicks / (float)SimConfig.TicksPerSecond;
    }

    // La simulació ja ha restat la vida; aquí només s'activen els efectes visuals
    public void Hit(float amount)
    {
        ghostDelay = 0.45f;
        HitFlash = 1f;
        Damaged?.Invoke(this, amount);
    }

    public void Die()
    {
        if (View != null) View.gameObject.SetActive(false);
    }

    // ---------------------------------------------------------------- animació (cada frame)
    public void Advance(float dt)
    {
        if (Alive)
        {
            switch (state)
            {
                case State.Moving:
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
                    break;

                case State.Attacking:
                    stateTime += dt;
                    float a = Mathf.Clamp01(stateTime / stateDuration);
                    View.transform.position = basePos + lungeDir * (Mathf.Sin(a * Mathf.PI) * LungeDist);
                    if (a >= 1f)
                    {
                        View.transform.position = basePos;
                        state = State.Idle;
                    }
                    break;
            }
        }

        // l'omplert baixa suau cap a la vida real
        DisplayHp = Mathf.Lerp(DisplayHp, Hp, 1f - Mathf.Exp(-14f * dt));

        // l'estela blanca espera una mica i després es buida
        if (ghostDelay > 0f) ghostDelay -= dt;
        else GhostHp = Mathf.MoveTowards(GhostHp, Hp, Stats.MaxHp * 0.6f * dt);
        if (GhostHp < DisplayHp) GhostHp = DisplayHp;

        // petit "cop" de mida en rebre dany
        HitFlash = Mathf.MoveTowards(HitFlash, 0f, dt * 6f);
        if (View != null) View.transform.localScale = Vector3.one * (1f + 0.18f * HitFlash);
    }

    public void Dispose()
    {
        if (View != null) UnityEngine.Object.Destroy(View.gameObject);
    }
}

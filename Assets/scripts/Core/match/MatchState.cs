using System;
using System.Collections.Generic;
using System.Text;

// Estat compartit de la partida, al SERVIDOR (lògica pura, sense Unity ni xarxa):
//  - un únic pool de còpies i un únic generador d'Uid per a tots els jugadors,
//  - una PlayerSession per jugador,
//  - el DIRECTOR de la partida: fases Prep -> Combat -> Resolve -> Prep (ronda + 1), emparellaments,
//    combats (simulats aquí, de cop), danys, eliminacions i final de partida.
// El servidor crida Tick() 20 cops per segon (SimConfig.TicksPerSecond).
public sealed class MatchState
{
    public const int CombatLeadTicks = 10;  // marge abans que comenci l'animació (els clients reben la formació)
    public const int CombatTailTicks = 20;  // pausa en acabar el combat, per veure el resultat

    public readonly UnitPool Pool = new UnitPool();
    public readonly PhaseTimer Timer = new PhaseTimer();

    // Geometria del tauler (la posa la vista). Si és null no hi ha combat real: la fase de combat
    // dura un temps fix (CombatPlaceholderTicks) i no hi ha danys. Així es poden provar les fases soles.
    public IBoardMap BoardMap;

    readonly Dictionary<ulong, PlayerSession> sessions = new Dictionary<ulong, PlayerSession>();
    readonly Dictionary<ulong, ulong> lastOpponent = new Dictionary<ulong, ulong>();
    readonly List<Fight> fights = new List<Fight>();
    readonly Random seedRng;
    int nextUid;
    int combatTicks;
    int combatDuration;
    int startPlayers;
    int startHumans;
    int botCounter;

    // Fase 6: els bots tenen ids virtuals fora del rang dels clients de xarxa
    public const ulong BotIdBase = 1000000UL;

    // Temps que se li dona a un client desconnectat per tornar abans de ser eliminat.
    // Mentrestant el controla la IA simple (BotBrain).
    public int DisconnectGraceTicks = 30 * SimConfig.TicksPerSecond;

    public int CombatPlaceholderTicks = 6 * SimConfig.TicksPerSecond;

    // (nova fase, ronda): es dispara en començar (Prep, ronda 1) i a cada canvi de fase.
    // S'envia DESPRÉS d'aplicar les regles de la fase (ingressos, botigues, combats, bloqueig de moviments).
    public event Action<MatchPhase, int> OnPhaseChanged;
    public event Action OnCombatResolved;               // danys ja aplicats (abans de passar a Resolve)
    public event Action<PlayerSession> OnEliminated;
    public event Action OnMatchOver;
    public event Action OnRosterChanged;                // entra un bot, es desconnecta algú...

    public MatchState(int seed = -1)
    {
        seedRng = seed >= 0 ? new Random(seed) : new Random();
        Timer.OnPhaseChanged += HandlePhase;
    }

    // ---------------------------------------------------------------- jugadors
    public int NextUid() => nextUid++;

    public int PlayerCount => sessions.Count;
    public IEnumerable<PlayerSession> Sessions => sessions.Values;

    public PlayerSession Get(ulong clientId)
    {
        PlayerSession s;
        return sessions.TryGetValue(clientId, out s) ? s : null;
    }

    // Crea la sessió del jugador i li reparteix la primera botiga (surt del pool compartit).
    public PlayerSession AddPlayer(ulong clientId, string name)
    {
        PlayerSession s;
        if (sessions.TryGetValue(clientId, out s)) return s;

        s = new PlayerSession(clientId, name, Pool, NextUid, seedRng.Next());
        sessions[clientId] = s;
        s.Shop.NewRound();
        s.Shop.CanRearrange = !Started || Phase == MatchPhase.Prep;
        return s;
    }

    // Jugador controlat pel servidor (proves amb 8 sessions). Es crea abans de Start().
    public PlayerSession AddBot(string name)
    {
        var s = AddPlayer(BotIdBase + (ulong)(botCounter++), name);
        s.IsBot = true;
        OnRosterChanged?.Invoke();
        return s;
    }

    // Eliminació IMMEDIATA d'un jugador (sense període de gràcia).
    //  - Abans de començar: s'esborra la sessió i les seves unitats tornen al pool.
    //  - En partida: queda eliminat però la sessió es manté perquè surti a la classificació.
    public void RemovePlayer(ulong clientId)
    {
        PlayerSession s;
        if (!sessions.TryGetValue(clientId, out s)) return;

        if (!Started)
        {
            s.Shop.ReleaseAll();
            sessions.Remove(clientId);
            lastOpponent.Remove(clientId);
            OnRosterChanged?.Invoke();
            return;
        }

        s.Disconnected = true;
        if (!Over && s.IsAlive) ForceEliminate(s);
        OnRosterChanged?.Invoke();
        NotifyIfOver();
    }

    // Client desconnectat. Abans de començar = fora. En partida: període de gràcia (DisconnectGraceTicks)
    // durant el qual el controla la IA simple; passat el temps queda eliminat (vegeu TickDisconnects).
    public void Disconnect(ulong clientId)
    {
        PlayerSession s;
        if (!sessions.TryGetValue(clientId, out s) || s.IsBot) return;

        if (!Started) { RemovePlayer(clientId); return; }

        s.Disconnected = true;
        if (!Over && s.IsAlive)
        {
            s.DisconnectTicksLeft = Math.Max(1, DisconnectGraceTicks);
            if (Phase == MatchPhase.Prep) BotBrain.Play(s, Round);   // no es queda sense jugar la ronda actual
        }
        OnRosterChanged?.Invoke();
        NotifyIfOver();   // p. ex. si era l'últim humà connectat
    }

    void ForceEliminate(PlayerSession s)
    {
        s.State.AddLives(-PlayerState.MaxLives);
        Eliminate(s);
    }

    void NotifyIfOver()
    {
        bool was = Over;
        CheckOver();
        if (!was && Over) OnMatchOver?.Invoke();
    }

    // Compta enrere dels desconnectats. Retorna true si la partida ha acabat per això.
    bool TickDisconnects()
    {
        List<PlayerSession> expired = null;
        foreach (var s in sessions.Values)
        {
            if (!s.Disconnected || !s.IsAlive) continue;
            if (--s.DisconnectTicksLeft > 0) continue;
            if (expired == null) expired = new List<PlayerSession>();
            expired.Add(s);
        }
        if (expired == null) return false;

        foreach (var s in expired) ForceEliminate(s);
        CheckOver();
        if (Over) { OnMatchOver?.Invoke(); return true; }
        return false;
    }

    int AliveCount()
    {
        int n = 0;
        foreach (var s in sessions.Values) if (s.IsAlive) n++;
        return n;
    }

    // Humans vius i connectats
    int AliveHumans()
    {
        int n = 0;
        foreach (var s in sessions.Values) if (s.IsAlive && s.IsRemote) n++;
        return n;
    }

    // ---------------------------------------------------------------- fases
    public bool Started => Timer.Started;
    public MatchPhase Phase => Timer.Phase;
    public int Round => Timer.Round;

    public IReadOnlyList<Fight> Fights => fights;
    public bool Over { get; private set; }
    public bool HasWinner { get; private set; }
    public ulong WinnerId { get; private set; }

    // Ticks que queden a la fase actual (el compte enrere que veuen els clients)
    public int TicksLeftInPhase =>
        Phase == MatchPhase.Combat ? Math.Max(0, combatDuration - combatTicks) : Timer.TicksLeft;

    public void Configure(int prepSeconds, int combatSeconds, int resolveSeconds)
    {
        Timer.PrepTicks = Math.Max(1, prepSeconds * SimConfig.TicksPerSecond);
        CombatPlaceholderTicks = Math.Max(1, combatSeconds * SimConfig.TicksPerSecond);
        Timer.ResolveTicks = Math.Max(1, resolveSeconds * SimConfig.TicksPerSecond);
    }

    public void Start()
    {
        if (Timer.Started) return;
        startPlayers = sessions.Count;
        startHumans = 0;
        foreach (var s in sessions.Values) if (!s.IsBot) startHumans++;
        Timer.Start(); // dispara (Prep, ronda 1)
    }

    public void Tick()
    {
        if (!Timer.Started || Over) return;
        if (TickDisconnects()) return;

        if (Timer.Phase == MatchPhase.Combat)
        {
            combatTicks++;
            if (combatTicks >= combatDuration)
            {
                ResolveFights();
                Timer.EndCombat();                       // -> Resolve
                if (Over) OnMatchOver?.Invoke();
            }
            return;
        }
        Timer.Tick();
    }

    // Regles de cada fase (aplicades a TOTS els jugadors)
    void HandlePhase(MatchPhase phase, int round)
    {
        if (phase == MatchPhase.Combat)
        {
            combatTicks = 0;
            combatDuration = CombatPlaceholderTicks;
        }

        foreach (var s in sessions.Values)
        {
            // només es poden moure unitats a la preparació
            s.Shop.CanRearrange = phase == MatchPhase.Prep;

            if (phase == MatchPhase.Prep && round > 1 && s.IsAlive)
            {
                int interest = Economy.InterestFor(s.State.Gold);
                int total = Economy.GoldPerRound + interest;
                s.State.AddGold(total);
                s.Shop.NewRound(); // botiga nova (retorna les unitats no comprades al pool)
                s.PendingMessage = "Ronda " + round + ": +" + total + " or ("
                                   + Economy.GoldPerRound + " base + " + interest + " interès)";
            }

            // bots i jugadors desconnectats (en període de gràcia): juguen sols la preparació
            if (phase == MatchPhase.Prep && s.IsAlive && (s.IsBot || s.Disconnected))
                BotBrain.Play(s, round);
        }

        if (phase == MatchPhase.Combat) StartFights();
        OnPhaseChanged?.Invoke(phase, round);
    }

    // ---------------------------------------------------------------- combats
    void StartFights()
    {
        fights.Clear();
        if (BoardMap == null) return;   // sense geometria: combat de temps fix

        var alive = new List<ulong>();
        foreach (var s in sessions.Values) if (s.IsAlive) alive.Add(s.ClientId);
        alive.Sort();

        int maxTicks = 0;
        foreach (var p in MatchMaker.Pair(alive, lastOpponent, seedRng))
        {
            var f = new Fight { A = p.A, B = p.B };
            var a = sessions[p.A];
            f.Setup.AddRange(Formation(a, false));

            if (!p.BIsGhost)
            {
                f.Setup.AddRange(Formation(sessions[p.B], true));
                lastOpponent[p.A] = p.B;
                lastOpponent[p.B] = p.A;
            }
            else
            {
                var others = new List<ulong>();
                foreach (var id in alive) if (id != p.A) others.Add(id);
                if (others.Count > 0)
                {
                    f.BIsGhost = true;
                    f.GhostOf = others[seedRng.Next(others.Count)];
                    f.Setup.AddRange(Formation(sessions[f.GhostOf], true));
                }
                else
                {
                    f.BIsAI = true;
                    f.Setup.AddRange(EnemyTeam(a));
                }
            }

            f.Result = new CombatSim(f.Setup).RunToEnd();
            fights.Add(f);
            if (f.Result.Ticks > maxTicks) maxTicks = f.Result.Ticks;
        }
        combatDuration = CombatLeadTicks + maxTicks + CombatTailTicks;
    }

    // Unitats del tauler d'un jugador en el marc de la simulació (top = la meitat d'adalt, girada 180°)
    List<SimUnitSetup> Formation(PlayerSession s, bool top)
    {
        var list = new List<SimUnitSetup>();
        for (int i = 0; i < Bench.BoardSize; i++)
        {
            var u = s.Shop.Bench.BoardAt(i);
            if (u == null) continue;
            int c, r;
            if (!BoardMap.TryGetCoord(i, out c, out r)) continue;
            if (top) { c = BoardConfig.Columns - 1 - c; r = BoardConfig.Rows - 1 - r; }
            list.Add(new SimUnitSetup
            {
                Uid = u.Uid, DefId = u.Def.Id, Star = u.Star,
                Team = top ? SimTeam.B : SimTeam.A, Col = c, Row = r
            });
        }
        return list;
    }

    List<SimUnitSetup> EnemyTeam(PlayerSession a)
    {
        var tiles = new List<HexCoord>();
        for (int i = 0; i < Bench.BoardSize; i++)
        {
            int c, r;
            if (BoardMap.TryGetCoord(i, out c, out r))
                tiles.Add(new HexCoord(BoardConfig.Columns - 1 - c, BoardConfig.Rows - 1 - r));
        }
        tiles.Sort((x, y) => x.R != y.R ? x.R.CompareTo(y.R) : x.C.CompareTo(y.C));
        return EnemyTeamGenerator.Generate(Round, a.Shop.Bench.BoardCount, seedRng.Next(), tiles);
    }

    // Danys, eliminacions i final de partida. Només perd vides qui perd (un empat no fa mal).
    void ResolveFights()
    {
        foreach (var f in fights)
        {
            var r = f.Result;
            bool aLost = r.Outcome == SimOutcome.TeamBWon;
            bool bLost = r.Outcome == SimOutcome.TeamAWon;

            if (aLost) f.DamageA = Hit(f.A, Economy.DamageFor(Round, r.SurvivorsB));
            else Note(f.A, bLost ? "Combat guanyat!" : "Combat empatat");

            if (f.RealB)
            {
                if (bLost) f.DamageB = Hit(f.B, Economy.DamageFor(Round, r.SurvivorsA));
                else Note(f.B, aLost ? "Combat guanyat!" : "Combat empatat");
            }
        }
        CheckOver();
        OnCombatResolved?.Invoke();
    }

    void Note(ulong id, string msg)
    {
        PlayerSession s;
        if (sessions.TryGetValue(id, out s) && s.IsAlive) s.PendingMessage = msg;
    }

    int Hit(ulong id, int dmg)
    {
        PlayerSession s;
        if (!sessions.TryGetValue(id, out s) || !s.IsAlive) return 0;
        dmg = Math.Min(Math.Max(dmg, 0), s.State.Lives);   // les vides no baixen de 0
        s.State.AddLives(-dmg);
        if (!s.IsAlive) Eliminate(s);
        else s.PendingMessage = "Combat perdut: -" + dmg + (dmg == 1 ? " vida" : " vides");
        return dmg;
    }

    void Eliminate(PlayerSession s)
    {
        s.Shop.ReleaseAll();                 // botiga i unitats tornen al pool compartit
        s.Place = AliveCount() + 1;          // ja no compta com a viu
        s.PendingMessage = "Has estat eliminat (posició " + s.Place + ")";
        OnEliminated?.Invoke(s);
    }

    // La partida acaba quan:
    //  - amb 2+ jugadors en queda un de viu (amb 1 jugador, quan mor), o
    //  - no queda cap humà viu i connectat (només bots / desconnectats): no té sentit seguir.
    // En el segon cas els supervivents es classifiquen per vides.
    void CheckOver()
    {
        if (Over || startPlayers == 0) return;
        int alive = AliveCount();
        bool lastStanding = alive <= (startPlayers > 1 ? 1 : 0);
        bool noHumans = startHumans > 0 && AliveHumans() == 0;
        if (!lastStanding && !noHumans) return;

        Over = true;
        var survivors = new List<PlayerSession>();
        foreach (var s in sessions.Values) if (s.IsAlive) survivors.Add(s);
        survivors.Sort((a, b) =>
        {
            int c = b.State.Lives.CompareTo(a.State.Lives);
            return c != 0 ? c : a.ClientId.CompareTo(b.ClientId);
        });
        for (int i = 0; i < survivors.Count; i++) survivors[i].Place = i + 1;

        if (survivors.Count > 0)
        {
            var w = survivors[0];
            WinnerId = w.ClientId;
            HasWinner = true;
            if (lastStanding) w.PendingMessage = "Has guanyat la partida!";
        }
    }

    // El nom el posa el jugador: no pot trencar el format "posició;nom;vides"
    static string Clean(string n)
    {
        return (n ?? "").Replace(';', ',').Replace('\n', ' ').Replace('\r', ' ');
    }

    // Classificació final, una línia per jugador: "posició;nom;vides"
    public string RankingText()
    {
        var list = new List<PlayerSession>(sessions.Values);
        list.Sort((a, b) => (a.Place == 0 ? int.MaxValue : a.Place).CompareTo(b.Place == 0 ? int.MaxValue : b.Place));
        var sb = new StringBuilder();
        foreach (var s in list)
            sb.Append(s.Place).Append(';').Append(Clean(s.Name)).Append(';').Append(s.State.Lives).Append('\n');
        return sb.ToString();
    }
}

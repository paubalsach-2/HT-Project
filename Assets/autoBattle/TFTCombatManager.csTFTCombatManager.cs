using System.Collections.Generic;
using UnityEngine;

public class TFTCombatManager : MonoBehaviour
{
    public enum RoundState { Preparation, Combat }

    [Header("Estat de la Partida")]
    public RoundState currentState = RoundState.Preparation;

    // Guardem una còpia de totes les unitats a l'inici del combat per poder reviure les que morin
    private List<TFTUnit> combatUnits = new List<TFTUnit>();

    void Update()
    {
        // Drecera per provar ràpid: Prem la tecla P per començar el combat, i R per reiniciar-lo
        if (Input.GetKeyDown(KeyCode.P) && currentState == RoundState.Preparation)
        {
            StartCombat();
        }
        else if (Input.GetKeyDown(KeyCode.R))
        {
            ResetRound();
        }

        if (currentState == RoundState.Combat)
        {
            UpdateCombat();
        }
    }

    public void StartCombat()
    {
        combatUnits.Clear();

        // Guardem la posició de totes les fitxes abans que es moguin
        foreach (TFTUnit unit in TFTUnit.allUnits)
        {
            if (unit.isOnBoard)
            {
                unit.SaveBoardPosition();
                combatUnits.Add(unit);
            }
        }

        currentState = RoundState.Combat;
        Debug.Log("⚔️ COMENÇA EL COMBAT!");
    }

    void UpdateCombat()
    {
        bool playerAlive = false;
        bool enemyAlive = false;

        for (int i = 0; i < combatUnits.Count; i++)
        {
            TFTUnit unit = combatUnits[i];
            if (!unit.IsDead && unit.isOnBoard)
            {
                unit.CombatUpdate();

                if (unit.team == TFTUnit.Team.Player) playerAlive = true;
                if (unit.team == TFTUnit.Team.Enemy) enemyAlive = true;
            }
        }

        // Comprovem si algun equip s'ha quedat sense unitats
        if (!playerAlive || !enemyAlive)
        {
            EndCombat(playerAlive);
        }
    }

    void EndCombat(bool playerWon)
    {
        if (playerWon)
            Debug.Log("🏆 Victòria del Jugador!");
        else
            Debug.Log("💀 Victòria de l'Enemic!");

        // Tornem a la fase de preparació després de 2 segons
        currentState = RoundState.Preparation;
        Invoke(nameof(ResetRound), 2f);
    }

    public void ResetRound()
    {
        CancelInvoke(nameof(ResetRound));
        currentState = RoundState.Preparation;

        foreach (TFTUnit unit in combatUnits)
        {
            if (unit != null)
                unit.ResetUnit();
        }

        Debug.Log("🔄 Tauler reiniciat: Fase de Preparació.");
    }
}
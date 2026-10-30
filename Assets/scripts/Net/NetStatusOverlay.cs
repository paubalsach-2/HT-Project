using UnityEngine;
using UnityEngine.SceneManagement;

// Indicador de xarxa durant la partida.
//  - Editor / Development build: panell de debug (rol, jugadors, bots) + botó per sortir.
//  - Build final: NOMÉS el botó "Sortir de la partida" (sense informació de debug).
// El crea NetBootstrap; només es veu fora de l'escena del menú.
public class NetStatusOverlay : MonoBehaviour
{
    const float X = 24f, Y = 110f, W = 300f;

    NetBootstrap boot;

    void Awake()
    {
        boot = GetComponent<NetBootstrap>();
    }

    void OnGUI()
    {
        if (boot == null || !boot.IsActive) return;
        if (SceneManager.GetActiveScene().name == boot.menuSceneName) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        const float H = 84f;
        var ms = MatchServer.Instance;
        int total = ms != null && ms.Match != null ? ms.Match.PlayerCount : boot.PlayerCount;
        GUI.Box(new Rect(X, Y, W, H), GUIContent.none);
        string who = boot.IsHost ? "HOST  -  " + total + " jugadors" : "CLIENT";
        GUI.Label(new Rect(X + 10f, Y + 6f, W - 20f, 24f), who);
        if (GUI.Button(new Rect(X + 10f, Y + 38f, W - 20f, 34f), "Sortir de la partida"))
            boot.LeaveToMenu();
#else
        if (GUI.Button(new Rect(X, Y, W, 34f), "Sortir de la partida"))
            boot.LeaveToMenu();
#endif
    }
}

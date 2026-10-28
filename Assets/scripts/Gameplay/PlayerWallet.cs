// Adapta PlayerStats (l'or del HUD) a la interfície que necessita la botiga.
public sealed class PlayerWallet : IWallet
{
    readonly PlayerStats stats;
    public PlayerWallet(PlayerStats stats) { this.stats = stats; }

    public int Gold => stats.Gold;
    public bool TrySpend(int amount) => stats.TrySpend(amount);
    public void AddGold(int amount) => stats.AddGold(amount);
}

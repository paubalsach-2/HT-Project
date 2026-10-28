// El que la botiga necessita de l'economia del jugador.
public interface IWallet
{
    int Gold { get; }
    bool TrySpend(int amount);
    void AddGold(int amount);
}

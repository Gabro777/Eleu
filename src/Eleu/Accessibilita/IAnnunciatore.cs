namespace Eleu.Accessibilita;

/// <summary>
/// Fa pronunciare un messaggio allo screen reader attivo.
/// Serve per le azioni che non hanno un effetto visibile ("File salvato", "Riga 3 di 10").
/// </summary>
public interface IAnnunciatore
{
    void Annuncia(string messaggio);
}

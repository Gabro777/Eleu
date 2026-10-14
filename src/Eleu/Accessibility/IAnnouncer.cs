namespace Eleu.Accessibility;

/// <summary>
/// Makes the active screen reader speak a message.
/// Used for actions with no visible effect ("File salvato", "Riga 3 di 10").
/// </summary>
public interface IAnnouncer
{
    void Announce(string message);
}

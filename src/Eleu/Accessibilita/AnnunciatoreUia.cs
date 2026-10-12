using System.Windows.Forms.Automation;

namespace Eleu.Accessibilita;

/// <summary>
/// Annuncia i messaggi tramite le notifiche UI Automation di Windows,
/// supportate da NVDA, JAWS (2021 e successivi) e Assistente vocale senza librerie esterne.
/// </summary>
public sealed class AnnunciatoreUia(Form finestra) : IAnnunciatore
{
    public void Annuncia(string messaggio)
    {
        if (string.IsNullOrWhiteSpace(messaggio))
            return;

        // La notifica deve partire dal controllo che ha il focus (anche dentro una finestra di dialogo),
        // altrimenti alcuni screen reader la ignorano.
        var attiva = Form.ActiveForm ?? finestra;
        var controllo = attiva.ActiveControl ?? attiva;
        controllo.AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            messaggio);
    }
}

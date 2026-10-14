using System.Windows.Forms.Automation;

namespace Eleu.Accessibility;

/// <summary>
/// Announces messages through Windows UI Automation notifications,
/// supported by NVDA, JAWS (2021 and later) and Narrator without external libraries.
/// </summary>
public sealed class UiaAnnouncer(Form window) : IAnnouncer
{
    public void Announce(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        // The notification must come from the control that has the focus (even inside a dialog),
        // otherwise some screen readers ignore it.
        var activate = Form.ActiveForm ?? window;
        var control = activate.ActiveControl ?? activate;
        control.AccessibilityObject.RaiseAutomationNotification(
            AutomationNotificationKind.Other,
            AutomationNotificationProcessing.ImportantMostRecent,
            message);
    }
}

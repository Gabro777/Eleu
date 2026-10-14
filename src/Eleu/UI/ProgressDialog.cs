using System.Windows.Forms.Automation;

namespace Eleu.UI;

/// <summary>Progress of a long job (e.g. creating the audiobook), with the Cancel button.</summary>
public sealed class ProgressDialog : Form
{
    private readonly Label _state;
    private readonly ProgressBar _bar;
    private readonly CancellationTokenSource _cancel = new();
    private DateTime _lastAnnouncement = DateTime.MinValue;
    private string _lastState = "";

    public CancellationToken Cancellation => _cancel.Token;

    public ProgressDialog(string heading)
    {
        Text = heading;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ControlBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Font = new Font("Segoe UI", 11f);
        Padding = new Padding(12);

        var column = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill };
        _state = new Label { AutoSize = true, MaximumSize = new Size(420, 0), Text = "Preparazione..." };
        _bar = new ProgressBar { Width = 420, Height = 24, AccessibleName = "Avanzamento" };
        var cancel = new Button { Text = "Annulla", AutoSize = true };
        cancel.Click += (_, _) =>
        {
            _cancel.Cancel();
            cancel.Enabled = false;
            _state.Text = "Annullamento in corso...";
        };
        column.Controls.AddRange([_state, _bar, cancel]);
        Controls.Add(column);
        CancelButton = cancel;
        ActiveControl = cancel;
    }

    /// <summary>
    /// Updates text and bar. The screen reader hears how far it got every few seconds (not at every step,
    /// which would cover everything else), so users who cannot see the bar know the job is going on.
    /// </summary>
    public void Report(string state, int @value, int max)
    {
        _state.Text = state;
        _bar.Maximum = Math.Max(1, max);
        _bar.Value = Math.Clamp(@value, 0, _bar.Maximum);
        if (state != _lastState && DateTime.Now - _lastAnnouncement > TimeSpan.FromSeconds(5) && ActiveControl is { } active)
        {
            _lastAnnouncement = DateTime.Now;
            _lastState = state;
            active.AccessibilityObject.RaiseAutomationNotification(AutomationNotificationKind.Other,
                AutomationNotificationProcessing.MostRecent, $"{state}, {100 * _bar.Value / _bar.Maximum} per cento");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _cancel.Dispose();
        base.Dispose(disposing);
    }
}

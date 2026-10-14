using Eleu.Import;

namespace Eleu.UI;

/// <summary>
/// File > Acquisisci da scanner: which scanner and where the pages are (glass or document feeder).
/// The Windows scan dialog is skipped: Eleu chooses settings suited to text recognition.
/// </summary>
public sealed class ScanDialog : DialogBase
{
    private readonly IReadOnlyList<ScannerInfo> _scanners;
    private readonly ComboBox _scanner, _source;
    private readonly List<ScanSource> _sources = [];

    public ScanDialog(IReadOnlyList<ScannerInfo> scanners) : base("Acquisisci da scanner", 320)
    {
        _scanners = scanners;
        _scanner = Field("&Scanner:", ComboBox(scanners.Select(s => s.Name), 0));
        _source = Field("&Fogli:", ComboBox([], -1));
        _scanner.SelectedIndexChanged += (_, _) => FillSources();
        FillSources();

        WholeLine(new Label
        {
            Text = "Metti il foglio sul vetro (o i fogli nell'alimentatore) e premi Scansiona. "
                + "Con il vetro, dopo ogni pagina Eleu chiede se ce n'è un'altra.",
            AutoSize = true,
            MaximumSize = new Size(560, 0),
            Margin = new Padding(0, 8, 0, 0),
        });

        AddButtons();
        ((Button)AcceptButton!).Text = "S&cansiona";
        ActiveControl = scanners.Count > 1 ? _scanner : _source;
    }

    public ScannerInfo Scanner => _scanners[_scanner.SelectedIndex];

    public ScanSource Source => _sources[_source.SelectedIndex];

    private void FillSources()
    {
        _sources.Clear();
        _source.Items.Clear();
        var scanner = _scanners[_scanner.SelectedIndex];
        if (scanner.HasFlatbed)
        {
            _sources.Add(ScanSource.Flatbed);
            _source.Items.Add("Sul vetro, una pagina alla volta");
        }
        if (scanner.HasFeeder)
        {
            _sources.Add(ScanSource.Feeder);
            _source.Items.Add("Nell'alimentatore, tutte insieme");
        }
        _source.SelectedIndex = 0;
    }
}

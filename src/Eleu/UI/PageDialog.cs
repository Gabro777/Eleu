using Eleu.Documents;

namespace Eleu.UI;

/// <summary>
/// File > Imposta pagina, in two tabs: Margins (paper, orientation, margins, header and
/// footer distances) and Headers (texts with variables, inserted with the context menu).
/// "Predefinito" makes these settings the starting point for new documents.
/// </summary>
public sealed class PageDialog : DialogBase
{
    private static readonly string[] Orientations = ["Verticale", "Orizzontale"];
    private static readonly string[] Kinds = ["Intestazioni in tutte le pagine", "Intestazioni in tutte le pagine tranne la prima"];

    private readonly PageLayout _initial;
    private readonly List<PaperFormat> _formats;
    private readonly ComboBox _format, _orientation, _kind;
    private readonly NumberWithUnit _top, _bottom, _left, _right, _headerDistance, _footerDistance;
    private readonly TextBox _header, _footer;
    private readonly CheckBox _default;

    public PageDialog(PageLayout current, bool @default = true) : base("Imposta pagina")
    {
        _initial = current;
        _formats = PageLayout.Formats.ToList();
        if (current.Format is null)
            _formats.Add(new PaperFormat("Personalizzato", current.Width, current.Height));
        int index = current.Format is PaperFormat f ? _formats.IndexOf(f) : _formats.Count - 1;

        var margins = NewGrid(220);
        _format = Field(margins, "&Foglio:", ComboBox(_formats.Select(x => x.Name), index));
        _orientation = Field(margins, "&Orientamento:", ComboBox(Orientations, current.Horizontal ? 1 : 0));
        _top = Field(margins, "S&uperiore:", Centimeters(current.TopMargin));
        _bottom = Field(margins, "I&nferiore:", Centimeters(current.BottomMargin));
        _left = Field(margins, "&Sinistro:", Centimeters(current.LeftMargin));
        _right = Field(margins, "&Destro:", Centimeters(current.RightMargin));
        _headerDistance = Field(margins, "Intestazione da&l bordo:", Centimeters(current.HeaderDistance));
        _footerDistance = Field(margins, "Piè di pagina dal &bordo:", Centimeters(current.FooterDistance));

        var headerRows = NewGrid(320);
        _kind = Field(headerRows, "&Tipologia di pagina:", ComboBox(Kinds, current.SkipFirstPage ? 1 : 0));
        _header = Field(headerRows, "&Intestazione:", TextWithVariables(current.Header));
        _footer = Field(headerRows, "&Piè di pagina:", TextWithVariables(current.Footer));
        WholeLine(headerRows, new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Margin = new Padding(0, 8, 0, 0),
            Text = "Per inserire il numero di pagina o altre variabili, nel campo premi il tasto Applicazioni " +
                   "(o Maiusc+F10) e scegli dal menu. Esempio: Pagina {pagina} di {pagine}.",
        });

        var tabs = new TabControl { Dock = DockStyle.Fill, Height = 360, Width = 540, AccessibleName = "Schede" };
        tabs.TabPages.Add(Tab("Margini", margins));
        tabs.TabPages.Add(Tab("Intestazioni", headerRows));
        WholeLine(tabs);

        _default = DefaultCheckBox(@default);
        AddButtons();
        ActiveControl = _format;
    }

    public bool Default => _default.Checked;

    public PageLayout Result
    {
        get
        {
            var sheet = _format.SelectedIndex >= 0 ? _formats[_format.SelectedIndex] : null;
            return _initial with
            {
                Width = sheet?.Width ?? _initial.Width,
                Height = sheet?.Height ?? _initial.Height,
                Horizontal = _orientation.SelectedIndex == 1,
                TopMargin = Points(_top),
                BottomMargin = Points(_bottom),
                LeftMargin = Points(_left),
                RightMargin = Points(_right),
                HeaderDistance = Points(_headerDistance),
                FooterDistance = Points(_footerDistance),
                Header = _header.Text.Trim(),
                Footer = _footer.Text.Trim(),
                SkipFirstPage = _kind.SelectedIndex == 1,
            };
        }
    }

    /// <summary>Text box with the "Inserisci variabile" context menu (Applications key or Shift+F10).</summary>
    private static TextBox TextWithVariables(string @value)
    {
        var field = new TextBox { Text = @value };
        var menu = new ContextMenuStrip();
        foreach (var variable in PageLayout.Variables)
        {
            menu.Items.Add($"{variable.Description} {variable.Placeholder}", null, (_, _) =>
            {
                field.SelectedText = variable.Placeholder;
                field.Focus();
            });
        }
        field.ContextMenuStrip = menu;
        return field;
    }

    private static NumberWithUnit Centimeters(float points) =>
        new("cm", (decimal)Math.Round(points / PageLayout.PointsPerCentimeter, 2), 0, 10, 0.1m, 2);

    private static float Points(NumericUpDown field) => (float)field.Value * PageLayout.PointsPerCentimeter;
}

/// <summary>Modifica > Vai a: chooses where to go (page, line, paragraph) and the number.</summary>
public sealed class GoToDialog : DialogBase
{
    public static readonly string[] Targets = ["Pagina", "Riga", "Paragrafo"];

    private readonly ComboBox _target;
    private readonly NumericUpDown _number;

    public GoToDialog(int target, int number) : base("Vai a", 160)
    {
        _target = Field("&Vai a:", ComboBox(Targets, target));
        _number = Field("&Numero:", Number(number, 1, 1_000_000, 1, 0));
        AddButtons();
        ActiveControl = _number;
        _number.Select(0, _number.Text.Length);
    }

    public int Target => Math.Max(0, _target.SelectedIndex);

    public int Value => (int)_number.Value;
}

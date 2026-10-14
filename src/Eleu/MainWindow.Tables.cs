using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Tables (Inserisci > Tabelle). Tables are the text box's native ones; to know where you are
// the text structure is read (TextStructure). Rows and columns are added and removed by rewriting
// the table: it is read as a model, changed and put back in its place.
public sealed partial class MainWindow
{
    private (int Table, int Row, int Column)? _lastCell;
    private bool _silentCells;

    /// <summary>
    /// Entering a different cell says where you are ("Riga 2, colonna 3, Età"): the screen reader reads the
    /// cell text, Eleu adds the coordinates. Entering a table also how many rows and columns it has.
    /// </summary>
    private void AnnounceCellChange()
    {
        if (_silentCells || !_text.Focused)
            return;
        var text = _text.Text;
        var cell = TextStructure.CellAt(text, _text.SelectionStart);
        var key = cell is null ? ((int, int, int)?)null : (cell.TableNumber, cell.Row, cell.Column);
        if (key == _lastCell)
            return;
        var previous = _lastCell;
        _lastCell = key;
        if (!_settings.AnnounceCells)
            return;
        if (cell is null)
        {
            _announcer.Announce("Fuori dalla tabella");
            return;
        }
        var where = Coordinates(text, cell);
        if (previous?.Table != cell.TableNumber)
            where = $"Tabella {cell.TableNumber + 1}, {Dimensions(cell.Table)}. {where}";
        _announcer.Announce(where);
    }

    /// <summary>"3 righe, 2 colonne, con intestazione".</summary>
    private string Dimensions(TableInText table)
    {
        int rows = table.Rows.Count, columns = table.Rows.Max(r => r.Count);
        return $"{rows} {(rows == 1 ? "riga" : "righe")}, {columns} {(columns == 1 ? "colonna" : "colonne")}{(HeaderRows(table) > 0 ? ", con intestazione" : "")}";
    }

    /// <summary>"Riga 2, colonna 3, Età": the first row acts as the header and names the column.</summary>
    private static string Coordinates(string text, CellInText cell)
    {
        var where = $"Riga {cell.Row + 1}, colonna {cell.Column + 1}";
        var header = cell.Table.Rows[0];
        if (cell.Row > 0 && cell.Column < header.Count)
        {
            var name = TextStructure.Readable(text[header[cell.Column].Start..header[cell.Column].End]).Trim();
            if (name.Length > 0)
                where += $", {name}";
        }
        return where;
    }

    private static string CellText(string text, (int Start, int End) cell)
    {
        var content = TextStructure.Readable(text[cell.Start..cell.End]).Trim();
        return content.Length == 0 ? "vuota" : content;
    }

    private CellInText? CellHere(bool warn = true)
    {
        var cell = TextStructure.CellAt(_text.Text, _text.SelectionStart);
        if (cell is null && warn)
            _announcer.Announce("Il cursore non è in una tabella");
        return cell;
    }

    /// <summary>Goes to a cell and says where you are and what is written there.</summary>
    private void GoToCell(TableInText table, int tableNumber, int row, int column, string prefix = "")
    {
        var text = _text.Text;
        var cells = table.Rows[row];
        column = Math.Min(column, cells.Count - 1);
        _lastCell = (tableNumber, row, column);
        _text.Select(cells[column].Start, 0);
        _text.ScrollToCaret();
        var cell = new CellInText(table, tableNumber, row, column);
        _announcer.Announce($"{prefix}{Coordinates(text, cell)}: {CellText(text, cells[column])}");
    }

    /// <summary>Ctrl+Alt+arrows: the neighboring cell in the given direction.</summary>
    private void NearbyCell(int rows, int columns)
    {
        if (CellHere() is not { } cell)
            return;
        int row = cell.Row + rows, column = cell.Column + columns;
        if (row < 0 || row >= cell.Table.Rows.Count)
        {
            _announcer.Announce(row < 0 ? "Prima riga" : "Ultima riga");
            return;
        }
        if (column < 0 || column >= cell.Table.Rows[row].Count)
        {
            _announcer.Announce(column < 0 ? "Prima colonna" : "Ultima colonna");
            return;
        }
        GoToCell(cell.Table, cell.TableNumber, row, column);
    }

    // ---------- Add, delete, go to, list ----------

    private void AddTable()
    {
        if (CellHere(warn: false) is not null)
        {
            _announcer.Announce("Il cursore è già in una tabella");
            return;
        }
        using var window = new TableDialog();
        if (window.ShowDialog(this) != DialogResult.OK)
            return;

        // The table goes in an empty paragraph of its own, after the current one.
        var text = _text.Text;
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, _text.SelectionStart)];
        int position = start;
        if (end > start)
        {
            _text.Select(end, 0);
            _text.SelectedText = "\n";
            position = end + 1;
        }
        // Empty table written in RTF, like those read from files.
        var empty = new DocumentContent();
        for (int r = 0; r < window.Rows; r++)
            for (int c = 0; c < window.Columns; c++)
                empty.Paragraphs.Add(new Paragraph { Cell = new CellPosition(0, r, c) });
        _silentCells = true;
        try
        {
            _text.Select(position, 0);
            _text.SelectedRtf = RtfWriter.Write(empty, _settings.BaseFont, _settings.BaseSize);
        }
        finally
        {
            _silentCells = false;
        }
        _text.Modified = true;
        var newOne = TextStructure.CellAt(_text.Text, position + 2);
        if (newOne is null)
            return;
        if (window.Header)
            SetHeaderRows(newOne.Table.Start, 1);
        _lastCell = (newOne.TableNumber, 0, 0);
        _text.Select(newOne.Table.Rows[0][0].Start, 0);
        AnnounceShortly($"Tabella di {Dimensions(newOne.Table)} inserita. Riga 1, colonna 1");
    }

    private void DeleteTable()
    {
        if (CellHere() is not { } cell)
            return;
        SetHeaderRows(cell.Table.Start, 0);
        _silentCells = true;
        try
        {
            _text.Select(cell.Table.Start, cell.Table.End - cell.Table.Start);
            _text.SelectedText = "";
        }
        finally
        {
            _silentCells = false;
        }
        _lastCell = null;
        _text.Modified = true;
        _announcer.Announce($"Tabella eliminata. {DescribeLine(_text.Text, _text.SelectionStart)}");
    }

    private void NearbyTable(bool forward)
    {
        var tables = TextStructure.Tables(_text.Text);
        int here = _text.SelectionStart;
        int i = forward ? tables.FindIndex(t => t.Start > here) : tables.FindLastIndex(t => t.End <= here);
        if (i < 0)
            _announcer.Announce(tables.Count == 0 ? "Nessuna tabella nel documento" : forward ? "Nessuna tabella dopo" : "Nessuna tabella prima");
        else
            GoToTable(tables[i], i);
    }

    private void GoToTable(TableInText table, int number)
    {
        _lastCell = (number, 0, 0);
        _text.Select(table.Rows[0][0].Start, 0);
        _text.ScrollToCaret();
        _announcer.Announce($"Tabella {number + 1}, {Dimensions(table)}. Riga 1, colonna 1: {CellText(_text.Text, table.Rows[0][0])}");
    }

    private void ShowTableList()
    {
        var text = _text.Text;
        var tables = TextStructure.Tables(text);
        var items = tables.Select((t, i) => new ListItem(
            [$"Tabella {i + 1}", Dimensions(t), CellText(text, t.Rows[0][0])], i)).ToList();
        using var window = new ListDialog("Tabelle", ["Tabella", "Dimensioni", "Prima cella"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            _silentCells = true;
            try
            {
                foreach (var i in window.ToDelete.OrderDescending())
                {
                    _text.Select(tables[i].Start, tables[i].End - tables[i].Start);
                    _text.SelectedText = "";
                }
            }
            finally
            {
                _silentCells = false;
            }
            _text.Modified = true;
            AnnounceShortly(window.ToDelete.Count == 1 ? "Tabella eliminata" : $"{window.ToDelete.Count} tabelle eliminate");
        }
        else if (window.Target is int index)
        {
            BeginInvoke(() => GoToTable(tables[index], index));
        }
    }

    // ---------- Rows and columns ----------

    private void InsertRow(bool bottom) => ChangeTable((grid, row, column) =>
    {
        int newOne = bottom ? row + 1 : row;
        grid.InsertRow(newOne);
        return (newOne, column, bottom ? "Riga inserita sotto" : "Riga inserita sopra");
    });

    private void InsertColumn(bool onRight) => ChangeTable((grid, row, column) =>
    {
        int newOne = onRight ? column + 1 : column;
        grid.InsertColumn(newOne);
        return (row, newOne, onRight ? "Colonna inserita a destra" : "Colonna inserita a sinistra");
    });

    private void DeleteRow() => ChangeTable((grid, row, column) =>
    {
        if (grid.Rows.Count == 1)
            return null; // the last row: the table is deleted
        grid.Rows.RemoveAt(row);
        return (Math.Min(row, grid.Rows.Count - 1), column, "Riga eliminata");
    }, DeleteTable);

    private void DeleteColumn() => ChangeTable((grid, row, column) =>
    {
        if (grid.Columns == 1)
            return null;
        grid.DeleteColumn(column);
        return (row, Math.Max(0, column - 1), "Colonna eliminata");
    }, DeleteTable);

    /// <summary>Sorts the rows by the cursor's column; the first row (header) stays on top.</summary>
    private void SortLines(bool ascending) => ChangeTable((grid, _, column) =>
    {
        grid.Sort(column, ascending, header: true);
        return (grid.Rows.Count > 1 ? 1 : 0, column,
            ascending ? "Righe ordinate dalla A alla Z, dal più piccolo al più grande" : "Righe ordinate dalla Z alla A, dal più grande al più piccolo");
    });

    private void ColumnWidth()
    {
        if (CellHere() is not { } cell)
            return;
        _text.Select(cell.Table.Start, cell.Table.End - cell.Table.Start);
        var grid = Grid.From(RtfReader.Read(_text.SelectedRtf ?? "").Paragraphs);
        _text.Select(cell.Text.Start, 0);
        float current = cell.Column < grid.Widths.Count && grid.Widths[cell.Column] > 0
            ? grid.Widths[cell.Column] : 2268 / 20f;
        using var window = new ColumnWidthDialog(cell.Column + 1, current * 2.54f / 72f);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        float points = window.Centimeters * 72f / 2.54f;
        ChangeTable((g, row, column) =>
        {
            while (g.Widths.Count <= column)
                g.Widths.Add(0);
            g.Widths[column] = points;
            return (row, column, $"Colonna {column + 1} larga {window.Centimeters:0.##} centimetri");
        });
    }

    /// <summary>
    /// Reads the cursor's table as a <see cref="Grid"/>, has it changed and puts it back in its place, then
    /// moves the cursor to the given cell. If the change returns null <paramref name="otherwise"/>
    /// is called (for example: deleting the last row deletes the table).
    /// </summary>
    private void ChangeTable(Func<Grid, int, int, (int Row, int Column, string Announcement)?> change, Action? otherwise = null)
    {
        if (CellHere() is not { } cell)
            return;
        var table = cell.Table;
        int header = HeaderRows(table); // the table is rewritten: the header rows are put back afterwards
        _silentCells = true;
        try
        {
            _text.Select(table.Start, table.End - table.Start);
            var grid = Grid.From(RtfReader.Read(_text.SelectedRtf ?? "").Paragraphs);
            if (grid.Rows.Count == 0)
                return;

            var outcome = change(grid, Math.Min(cell.Row, grid.Rows.Count - 1), cell.Column);
            if (outcome is not var (nextLine, nextColumn, announcement))
            {
                _text.Select(cell.Text.Start, 0);
                otherwise?.Invoke();
                return;
            }

            var @new = new DocumentContent();
            @new.Paragraphs.AddRange(grid.Paragraphs());
            // First the old table is removed, then the new one is inserted at the same point: pasting the RTF
            // over the selected table leaves the text box in a state that makes Windows accessibility
            // (UI Automation) fail.
            _text.SelectedText = "";
            _text.Select(table.Start, 0);
            _text.SelectedRtf = RtfWriter.Write(@new, _settings.BaseFont, _settings.BaseSize);
            _text.Modified = true;

            var redone = TextStructure.CellAt(_text.Text, table.Start + 2);
            if (redone is null)
                return;
            SetHeaderRows(redone.Table.Start, Math.Min(header, redone.Table.Rows.Count));
            nextLine = Math.Clamp(nextLine, 0, redone.Table.Rows.Count - 1);
            nextColumn = Math.Clamp(nextColumn, 0, redone.Table.Rows[nextLine].Count - 1);
            _silentCells = false;
            GoToCell(redone.Table, redone.TableNumber, nextLine, nextColumn, $"{announcement}. ");
        }
        finally
        {
            _silentCells = false;
        }
    }

    /// <summary>
    /// If the clipboard holds a table (copied from Word, Excel or another program that provides RTF), pastes it
    /// as a table, cleaned up like Eleu's, in a paragraph of its own. Returns false if there is none.
    /// </summary>
    private bool PasteTable()
    {
        if (!Clipboard.ContainsText(TextDataFormat.Rtf) || CellHere(warn: false) is not null)
            return false;
        var model = RtfReader.Read(Clipboard.GetText(TextDataFormat.Rtf));
        if (!model.Paragraphs.Any(p => p.Cell is not null))
            return false;

        if (_text.SelectionLength > 0)
            _text.SelectedText = "";
        var text = _text.Text;
        var paragraphs = TextStructure.Paragraphs(text);
        var (start, end) = paragraphs[TextStructure.ParagraphOf(paragraphs, _text.SelectionStart)];
        int position = start;
        if (end > start)
        {
            _text.Select(end, 0);
            _text.SelectedText = "\n";
            position = end + 1;
        }
        _silentCells = true;
        try
        {
            _text.Select(position, 0);
            _text.SelectedRtf = RtfWriter.Write(model, _settings.BaseFont, _settings.BaseSize);
        }
        finally
        {
            _silentCells = false;
        }
        _text.Modified = true;
        if (TextStructure.CellAt(_text.Text, position + 2) is { } pasted)
            SetHeaderRows(pasted.Table.Start, HeaderRowsOfModel(model).GetValueOrDefault(0));
        if (TextStructure.CellAt(_text.Text, position + 2) is { } cell)
            GoToCell(cell.Table, cell.TableNumber, 0, 0, $"Tabella incollata, {Dimensions(cell.Table)}. ");
        return true;
    }
}

/// <summary>Inserisci > Tabelle > Larghezza colonna: in centimeters.</summary>
public sealed class ColumnWidthDialog : DialogBase
{
    private readonly NumberWithUnit _centimeters;

    public ColumnWidthDialog(int column, float centimeters) : base($"Larghezza della colonna {column}", 140)
    {
        _centimeters = Field("&Larghezza:", new NumberWithUnit("cm", (decimal)centimeters, 0.5m, 30, 0.25m, 2));
        AddButtons();
        ActiveControl = _centimeters;
    }

    public float Centimeters => (float)_centimeters.Value;
}

/// <summary>Inserisci > Tabelle > Aggiungi tabella: how many rows and how many columns.</summary>
public sealed class TableDialog : DialogBase
{
    private readonly NumericUpDown _rows, _columns;
    private readonly CheckBox _header;

    public TableDialog() : base("Aggiungi tabella", 120)
    {
        _columns = Field("&Colonne:", Number(2, 1, 30, 1, 0));
        _rows = Field("&Righe:", Number(2, 1, 500, 1, 0));
        _header = CheckBox(Grid, "&Prima riga di intestazione (nomi delle colonne, ripetuti su ogni pagina)", true);
        AddButtons();
        ActiveControl = _columns;
    }

    public bool Header => _header.Checked;

    public int Rows => (int)_rows.Value;

    public int Columns => (int)_columns.Value;
}

using Eleu.Documents;
using Eleu.UI;

namespace Eleu;

// Images (Inserisci > Immagini). In the text they are objects of the text box, which shows and prints them; Eleu
// keeps aside the data and the description, which is what screen reader users "see". Each image is a
// one-character Text Object Model range that follows the text while typing.
public sealed partial class MainWindow
{
    private readonly List<(EmbeddedImage Image, dynamic Range)> _images = [];
    private List<(EmbeddedImage Image, int Position)>? _pendingImages;
    private int? _lastImage;

    private void PrepareImages()
    {
        // As for bookmarks and footnotes: if the box recreates its window, the ranges must be recreated.
        _text.HandleDestroyed += (_, _) =>
        {
            _pendingImages = ImageList();
            _images.Clear();
        };
        _text.HandleCreated += (_, _) =>
        {
            if (_pendingImages is { } pending)
                BeginInvoke(() =>
                {
                    if (Tom is { } tom)
                        _images.AddRange(pending.Select(i => (i.Image, (dynamic)tom.Range(i.Position, i.Position + 1))));
                });
            _pendingImages = null;
        };
    }

    /// <summary>The images in position order; those deleted by typing (empty range) are gone.</summary>
    private List<(EmbeddedImage Image, int Position)> ImageList()
    {
        _images.RemoveAll(i => (int)i.Range.Start >= (int)i.Range.End);
        return _images.Select(i => (i.Image, Position: (int)i.Range.Start)).OrderBy(i => i.Position).ToList();
    }

    /// <summary>The image where the cursor is (right before or right after), with its number.</summary>
    private (int Number, EmbeddedImage Image, int Position)? ImageHere()
    {
        int here = _text.SelectionStart;
        var images = ImageList();
        int i = images.FindIndex(m => m.Position == here);
        if (i < 0 && _text.SelectionLength == 0)
            i = images.FindIndex(m => m.Position == here - 1);
        return i < 0 ? null : (i + 1, images[i].Image, images[i].Position);
    }

    private static string Describe(int number, EmbeddedImage image) =>
        $"Immagine {number}: {(image.Description.Length > 0 ? image.Description : "senza descrizione")}, {image.Measures}";

    /// <summary>Arriving on an image with the arrows, Eleu says its description.</summary>
    private void AnnounceImage()
    {
        if (_silentCells || !_text.Focused || _images.Count == 0 || !_settings.AnnounceImages)
            return;
        var here = ImageHere();
        if (here?.Position == _lastImage)
            return;
        _lastImage = here?.Position;
        if (here is var (number, image, _))
            _announcer.Announce(Describe(number, image));
    }

    /// <summary>For Alt+Enter: if the cursor is on an image, its description.</summary>
    private string? ImageDescriptionHere() => ImageHere() is var (number, image, _) ? Describe(number, image) + ". " : null;

    private void AddImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Aggiungi immagine",
            Filter = "Immagini|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff|Tutti i file|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        EmbeddedImage image;
        try
        {
            image = Images.FromData(File.ReadAllBytes(dialog.FileName), "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException)
        {
            MessageBox.Show(this, $"Impossibile aprire l'immagine {Path.GetFileName(dialog.FileName)}.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        using var window = new ImageDialog("Aggiungi immagine", image);
        if (window.ShowDialog(this) != DialogResult.OK || Tom is not { } tom)
            return;
        image = window.Result;
        int position = _text.SelectionStart + _text.SelectionLength;
        _text.Select(position, 0);
        _text.SelectedRtf = Images.RtfImageOnly(image);
        _images.Add((image, tom.Range(position, position + 1)));
        _lastImage = position;
        _text.Select(position, 0);
        _text.Modified = true;
        AnnounceShortly($"Immagine aggiunta. {Describe(ImageHere()?.Number ?? 1, image)}");
    }

    private void EditImage()
    {
        if (ImageHere() is not var (number, image, position) || Tom is not { } tom)
        {
            _announcer.Announce("Il cursore non è su un'immagine");
            return;
        }
        using var window = new ImageDialog($"Modifica immagine {number}", image);
        if (window.ShowDialog(this) != DialogResult.OK)
            return;
        var newOne = window.Result;
        _images.RemoveAll(i => (int)i.Range.Start == position);
        // If the size changes the image is put back; if only the description changes it is enough to remember it.
        if (newOne.Width != image.Width)
        {
            _text.Select(position, 1);
            _text.SelectedRtf = Images.RtfImageOnly(newOne);
            _text.Select(position, 0);
        }
        _images.Add((newOne, tom.Range(position, position + 1)));
        _text.Modified = true;
        AnnounceShortly($"Immagine modificata. {Describe(number, newOne)}");
    }

    private void DeleteImage()
    {
        if (ImageHere() is not var (number, _, position))
        {
            _announcer.Announce("Il cursore non è su un'immagine");
            return;
        }
        DeleteImages([position]);
        _announcer.Announce($"Immagine {number} eliminata");
    }

    private void DeleteImages(IEnumerable<int> positions)
    {
        foreach (int position in positions.OrderDescending())
        {
            _images.RemoveAll(i => (int)i.Range.Start == position);
            _text.Select(position, 1);
            _text.SelectedText = "";
        }
        _lastImage = null;
        _text.Modified = true;
    }

    private void NearbyImage(bool forward)
    {
        var images = ImageList();
        int here = _text.SelectionStart;
        int i = forward ? images.FindIndex(m => m.Position > here) : images.FindLastIndex(m => m.Position < here - 1);
        if (i < 0)
        {
            _announcer.Announce(images.Count == 0 ? "Nessuna immagine nel documento" : forward ? "Nessuna immagine dopo" : "Nessuna immagine prima");
            return;
        }
        GoToImage(i + 1, images[i].Image, images[i].Position);
    }

    private void GoToImage(int number, EmbeddedImage image, int position)
    {
        _lastImage = position;
        _text.Select(position, 0);
        _text.ScrollToCaret();
        _announcer.Announce(Describe(number, image));
    }

    private void ShowImageList()
    {
        var images = ImageList();
        var items = images.Select((m, i) => new ListItem(
            [$"Immagine {i + 1}", m.Image.Description.Length > 0 ? m.Image.Description : "senza descrizione", m.Image.Measures], m.Position)).ToList();
        using var window = new ListDialog("Immagini", ["Immagine", "Descrizione", "Misure"], items);
        window.ShowDialog(this);
        if (window.ToDelete.Count > 0)
        {
            DeleteImages(window.ToDelete);
            AnnounceShortly(window.ToDelete.Count == 1 ? "Immagine eliminata" : $"{window.ToDelete.Count} immagini eliminate");
        }
        else if (window.Target is int position)
        {
            int i = images.FindIndex(m => m.Position == position);
            BeginInvoke(() => GoToImage(i + 1, images[i].Image, position));
        }
    }

    private void SaveImage()
    {
        if (ImageHere() is not var (number, image, _))
        {
            _announcer.Announce("Il cursore non è su un'immagine");
            return;
        }
        var extension = image.Format == "jpeg" ? "jpg" : "png";
        using var dialog = new SaveFileDialog
        {
            Title = "Salva immagine con nome",
            Filter = extension == "jpg" ? "Immagine JPEG|*.jpg" : "Immagine PNG|*.png",
            FileName = $"Immagine {number}.{extension}",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        try
        {
            File.WriteAllBytes(dialog.FileName, image.Data);
            AnnounceShortly($"Immagine salvata in {Path.GetFileName(dialog.FileName)}");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, $"Impossibile salvare l'immagine.\n\n{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Inserisci > Immagini > Grafica in rilievo: the current image (or one chosen from disk) in Braille dots
    /// for the Braille printer, or as a drawing for microcapsule paper.
    /// </summary>
    private void TactileGraphics()
    {
        byte[] data;
        string description;
        if (ImageHere() is var (_, image, _))
        {
            data = image.Data;
            description = image.Description;
        }
        else
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Scegli l'immagine da mettere in rilievo",
                Filter = "Immagini|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.tif;*.tiff|Tutti i file|*.*",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            try
            {
                data = File.ReadAllBytes(dialog.FileName);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                MessageBox.Show(this, $"Impossibile leggere l'immagine.\n\n{e.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            description = Path.GetFileNameWithoutExtension(dialog.FileName);
        }

        Bitmap figure;
        try
        {
            using var stream = new MemoryStream(data);
            using var read = Image.FromStream(stream);
            figure = new Bitmap(read);
        }
        catch (ArgumentException)
        {
            MessageBox.Show(this, "Il file non è un'immagine che Eleu sa leggere.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        using (figure)
        using (var window = new TactileGraphicsDialog(figure, description, Eleu.Braille.BraillePrinter.Installed(), _settings.BraillePrinter,
                   _settings.BrailleCells, _settings.BrailleLines, TranslateIfPossible))
        {
            window.ShowDialog(this);
            if (window.ChosenPrinter is string printer)
                _settings.BraillePrinter = printer;
        }
    }

    private static string? TranslateIfPossible(string text)
    {
        try
        {
            return Eleu.Braille.BrailleTranslator.Translate(text);
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>
/// Description and width of an image. The description is what the screen reader reads: without it, blind users
/// will not know what is there, so Eleu asks for it.
/// </summary>
public sealed class ImageDialog : DialogBase
{
    private readonly EmbeddedImage _image;
    private readonly TextBox _description;
    private readonly NumberWithUnit _width;

    public ImageDialog(string heading, EmbeddedImage image) : base(heading, 360)
    {
        _image = image;
        _description = Field("&Descrizione:", new TextBox
        {
            Text = image.Description,
            Multiline = true,
            Height = 80,
            ScrollBars = ScrollBars.Vertical,
        });
        _width = Field("&Larghezza:", new NumberWithUnit("cm", (decimal)(image.Width * 2.54f / 72f), 0.5m, 40, 0.5m, 1));
        WholeLine(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(480, 0),
            Margin = new Padding(0, 8, 0, 0),
            Text = "La descrizione è ciò che legge lo screen reader al posto dell'immagine. L'altezza segue la larghezza.",
        });
        AddButtons();
        ActiveControl = _description;
        FormClosing += (_, e) =>
        {
            if (DialogResult != DialogResult.OK || Description.Length > 0)
                return;
            var response = MessageBox.Show(this, "L'immagine non ha una descrizione: chi usa lo screen reader non saprà cosa mostra. Continuare senza?",
                heading, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2);
            e.Cancel = response != DialogResult.Yes;
        };
    }

    private string Description => _description.Text.ReplaceLineEndings(" ").Trim();

    public EmbeddedImage Result =>
        Images.WithWidth(_image, (float)_width.Value * 72f / 2.54f) with { Description = Description };
}

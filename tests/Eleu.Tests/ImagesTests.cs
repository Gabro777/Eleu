using Eleu.Documents;
using static Eleu.Tests.Support;

namespace Eleu.Tests;

public class ImagesTests
{
    /// <summary>A 200x100 pixel image at 96 dpi (150x75 points), in the requested format.</summary>
    internal static byte[] Bitmap(System.Drawing.Imaging.ImageFormat format)
    {
        using var bitmap = new System.Drawing.Bitmap(200, 100);
        bitmap.SetResolution(96, 96);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.Clear(System.Drawing.Color.SteelBlue);
        using var stream = new MemoryStream();
        bitmap.Save(stream, format);
        return stream.ToArray();
    }

    [Fact]
    public void FromData_PngStays_BmpBecomesPng_NaturalSize()
    {
        var png = Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Un cielo");
        Assert.Equal(("png", 150f, 75f, "Un cielo"), (png.Format, png.Width, png.Height, png.Description));

        var bmp = Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Bmp), "");
        Assert.Equal("png", bmp.Format);
        Assert.Equal(0x89, bmp.Data[0]); // PNG signature

        var small = Images.WithWidth(png, 75);
        Assert.Equal((75f, 37.5f), (small.Width, small.Height));
    }

    [Fact]
    public void Rtf_ImageWithDescriptionWrittenAndReadBack()
    {
        var image = Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Logo {blu} è");
        var rtf = "{" + "\\rtf1 Prima " + Images.Pict(image) + " dopo}";

        var read = new List<EmbeddedImage>();
        var withPlaceholders = Images.ToPlaceholders(rtf, read);
        var only = Assert.Single(read);
        Assert.Equal(("Logo {blu} è", 150f, 75f), (only.Description, only.Width, only.Height));
        Assert.Equal(image.Data, only.Data);
        Assert.DoesNotContain("pict", withPlaceholders);
        Assert.Contains("pngblip", Images.FromPlaceholders(withPlaceholders, read));
    }

    [Fact]
    public void Rtf_FromWord_KeepsASingleCopy()
    {
        var pict = Images.Pict(Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Foto"));
        var rtf = "{" + "\\rtf1 {\\*\\shppict" + pict + "}{\\nonshppict" + pict + "} fine}";
        var read = new List<EmbeddedImage>();
        var result = Images.ToPlaceholders(rtf, read);
        Assert.Single(read);
        Assert.DoesNotContain("nonshppict", result);
    }

    [Fact]
    public void ThroughTheTextBox_ImageVisible()
    {
        var image = Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Foto");
        int length = 0;
        RunSta(() =>
        {
            using var field = new RichTextBox();
            field.CreateControl();
            field.Text = "ab";
            field.Select(1, 0);
            field.SelectedRtf = Images.RtfImageOnly(image);
            length = field.TextLength;
        });
        Assert.Equal(3, length); // the image is one character
    }

    [Fact]
    public void Docx_ImageWithDescriptionKept()
    {
        var content = new DocumentContent();
        content.Images.Add(Images.FromData(Bitmap(System.Drawing.Imaging.ImageFormat.Jpeg), "Una foto"));
        var p = new Paragraph();
        p.Add(new Segment($"Ecco {Images.Placeholder(0)} fine"));
        content.Paragraphs.Add(p);

        using var stream = new MemoryStream();
        DocxFormat.Write(content, stream);
        stream.Position = 0;
        var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(stream, false));
        Assert.Empty(errors);

        stream.Position = 0;
        var reread = DocxFormat.Read(stream);
        var image = Assert.Single(reread.Images);
        Assert.Equal(("jpeg", "Una foto", 150f, 75f), (image.Format, image.Description, image.Width, image.Height));
        Assert.Equal($"Ecco {Images.Placeholder(0)} fine", reread.Paragraphs[0].Text);
        Assert.Empty(reread.NotPreserved);
    }
}

public class ReadableTests
{
    [Fact]
    public void PlaceholdersBecomeWords_FootnotesAtTheEnd()
    {
        var content = new DocumentContent();
        content.Bookmarks.Add("qui");
        content.Footnotes.Add("Fresche.");
        content.Images.Add(Images.FromData(ImagesTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta"));
        content.Images.Add(Images.FromData(ImagesTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), ""));
        var p = new Paragraph();
        p.Add(new Segment($"{Bookmarks.Placeholder(0)}Uova{FootnoteMarkers.Placeholder(0)} e{Images.Placeholder(0)}farina{Images.Placeholder(1)}"));
        content.Paragraphs.Add(p);

        var texts = Readable.From(content).Paragraphs.Select(x => x.Text).ToList();
        Assert.Equal("Uova [nota 1] e [Immagine: Una torta] farina [Immagine senza descrizione] ", texts[0]);
        Assert.Equal(["Note", "1. Fresche."], texts.Skip(1));
    }
}

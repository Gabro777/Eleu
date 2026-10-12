using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class ImmaginiTests
{
    /// <summary>Un'immagine 200x100 pixel a 96 dpi (150x75 punti), nel formato chiesto.</summary>
    internal static byte[] Bitmap(System.Drawing.Imaging.ImageFormat formato)
    {
        using var bitmap = new System.Drawing.Bitmap(200, 100);
        bitmap.SetResolution(96, 96);
        using (var g = System.Drawing.Graphics.FromImage(bitmap))
            g.Clear(System.Drawing.Color.SteelBlue);
        using var flusso = new MemoryStream();
        bitmap.Save(flusso, formato);
        return flusso.ToArray();
    }

    [Fact]
    public void DaDati_PngResta_BmpDiventaPng_DimensioneNaturale()
    {
        var png = Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Un cielo");
        Assert.Equal(("png", 150f, 75f, "Un cielo"), (png.Formato, png.Larghezza, png.Altezza, png.Descrizione));

        var bmp = Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Bmp), "");
        Assert.Equal("png", bmp.Formato);
        Assert.Equal(0x89, bmp.Dati[0]); // firma PNG

        var piccola = Immagini.ConLarghezza(png, 75);
        Assert.Equal((75f, 37.5f), (piccola.Larghezza, piccola.Altezza));
    }

    [Fact]
    public void Rtf_ImmagineConDescrizioneScrittaERiletta()
    {
        var immagine = Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Logo {blu} è");
        var rtf = "{" + "\\rtf1 Prima " + Immagini.Pict(immagine) + " dopo}";

        var lette = new List<Immagine>();
        var conSegnaposto = Immagini.InSegnaposto(rtf, lette);
        var sola = Assert.Single(lette);
        Assert.Equal(("Logo {blu} è", 150f, 75f), (sola.Descrizione, sola.Larghezza, sola.Altezza));
        Assert.Equal(immagine.Dati, sola.Dati);
        Assert.DoesNotContain("pict", conSegnaposto);
        Assert.Contains("pngblip", Immagini.DaSegnaposto(conSegnaposto, lette));
    }

    [Fact]
    public void Rtf_DaWord_SiTieneUnaSolaCopia()
    {
        var pict = Immagini.Pict(Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Foto"));
        var rtf = "{" + "\\rtf1 {\\*\\shppict" + pict + "}{\\nonshppict" + pict + "} fine}";
        var lette = new List<Immagine>();
        var risultato = Immagini.InSegnaposto(rtf, lette);
        Assert.Single(lette);
        Assert.DoesNotContain("nonshppict", risultato);
    }

    [Fact]
    public void AttraversoIlCampoDiTesto_ImmagineVisibile()
    {
        var immagine = Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Foto");
        int lunghezza = 0;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = "ab";
            campo.Select(1, 0);
            campo.SelectedRtf = Immagini.RtfSoloImmagine(immagine);
            lunghezza = campo.TextLength;
        });
        Assert.Equal(3, lunghezza); // l'immagine è un carattere
    }

    [Fact]
    public void Docx_ImmagineConDescrizioneConservata()
    {
        var contenuto = new ContenutoDocumento();
        contenuto.Immagini.Add(Immagini.DaDati(Bitmap(System.Drawing.Imaging.ImageFormat.Jpeg), "Una foto"));
        var p = new Paragrafo();
        p.Aggiungi(new Segmento($"Ecco {Immagini.Segnaposto(0)} fine"));
        contenuto.Paragrafi.Add(p);

        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(contenuto, flusso);
        flusso.Position = 0;
        var errori = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(flusso, false));
        Assert.Empty(errori);

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        var immagine = Assert.Single(riletto.Immagini);
        Assert.Equal(("jpeg", "Una foto", 150f, 75f), (immagine.Formato, immagine.Descrizione, immagine.Larghezza, immagine.Altezza));
        Assert.Equal($"Ecco {Immagini.Segnaposto(0)} fine", riletto.Paragrafi[0].Testo);
        Assert.Empty(riletto.ElementiNonConservati);
    }
}

public class LeggibileTests
{
    [Fact]
    public void SegnapostoDiventanoParole_NoteInFondo()
    {
        var contenuto = new ContenutoDocumento();
        contenuto.Segnalibri.Add("qui");
        contenuto.Note.Add("Fresche.");
        contenuto.Immagini.Add(Immagini.DaDati(ImmaginiTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), "Una torta"));
        contenuto.Immagini.Add(Immagini.DaDati(ImmaginiTests.Bitmap(System.Drawing.Imaging.ImageFormat.Png), ""));
        var p = new Paragrafo();
        p.Aggiungi(new Segmento($"{Segnalibri.Segnaposto(0)}Uova{Note.Segnaposto(0)} e{Immagini.Segnaposto(0)}farina{Immagini.Segnaposto(1)}"));
        contenuto.Paragrafi.Add(p);

        var testi = Leggibile.Da(contenuto).Paragrafi.Select(x => x.Testo).ToList();
        Assert.Equal("Uova [nota 1] e [Immagine: Una torta] farina [Immagine senza descrizione] ", testi[0]);
        Assert.Equal(["Note", "1. Fresche."], testi.Skip(1));
    }
}

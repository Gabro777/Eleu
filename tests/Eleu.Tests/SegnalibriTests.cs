using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class SegnalibriTests
{
    // Negli RTF dei test "|" sta per la barra rovescia, più leggibile.
    private static string Rtf(string testo) => testo.Replace('|', '\\');

    [Fact]
    public void InSegnaposto_SegnalibriDiventanoCaratteriConIlLoroNome()
    {
        var nomi = new List<string>();
        var rtf = Segnalibri.InSegnaposto(Rtf("{|rtf1 Uno {|*|bkmkstart Capitolo_2}{|*|bkmkend Capitolo_2}due {|*|bkmkstart Citt|'e0}fine}"), nomi);

        Assert.Equal(["Capitolo_2", "Città"], nomi);
        Assert.DoesNotContain("bkmk", rtf);
        Assert.Contains(Rtf("|u") + (short)Segnalibri.Segnaposto(0), rtf);
    }

    [Fact]
    public void AttraversoIlCampoDiTesto_IPuntiRestanoGliStessi()
    {
        string? letto = null, riscritto = null;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            var nomi = new List<string>();
            campo.Rtf = Segnalibri.InSegnaposto(Rtf("{|rtf1 Uno {|*|bkmkstart qui}due|par tre}"), nomi);
            letto = campo.Text;
            riscritto = Segnalibri.DaSegnaposto(campo.Rtf!, nomi);
        });

        Assert.Equal("Uno " + Segnalibri.Segnaposto(0) + "due\ntre", letto);
        Assert.Contains(Rtf("{|*|bkmkstart qui}{|*|bkmkend qui}due"), riscritto);
    }

    [Fact]
    public void Docx_SegnalibriConservati()
    {
        var contenuto = new ContenutoDocumento();
        contenuto.Segnalibri.AddRange(["Capitolo 2", "Capitolo 2", "1 inizio"]);
        var p = new Paragrafo();
        p.Aggiungi(new Segmento($"Uno {Segnalibri.Segnaposto(0)}due{Segnalibri.Segnaposto(2)}"));
        contenuto.Paragrafi.Add(p);
        var q = new Paragrafo();
        q.Aggiungi(new Segmento($"{Segnalibri.Segnaposto(1)}tre"));
        contenuto.Paragrafi.Add(q);

        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(contenuto, flusso);
        flusso.Position = 0;
        var errori = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(flusso, false));
        Assert.Empty(errori);

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        // I nomi diventano validi per Word (unici, senza spazi, iniziando con una lettera) e tornano leggibili.
        Assert.Equal(["Capitolo 2", "S 1 inizio", "Capitolo 2 2"], riletto.Segnalibri);
        Assert.Equal($"Uno {Segnalibri.Segnaposto(0)}due{Segnalibri.Segnaposto(1)}", riletto.Paragrafi[0].Testo);
        Assert.Equal($"{Segnalibri.Segnaposto(2)}tre", riletto.Paragrafi[1].Testo);
    }

    [Fact]
    public void NomeWord_Valido()
    {
        var usati = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Capitolo_2", Segnalibri.NomeWord("Capitolo 2", usati));
        Assert.Equal("capitolo_2_2", Segnalibri.NomeWord("capitolo 2", usati));
        Assert.Equal("S_3_maggio", Segnalibri.NomeWord("3 maggio", usati));
        Assert.Equal(40, Segnalibri.NomeWord(new string('a', 60), usati).Length);
    }
}

public class SegnalibriAccentiTests
{
    [Fact]
    public void NomeConAccentiScrittoERiletto()
    {
        // Il campo di testo scrive il segnaposto come "barra u N ?"; qui lo si simula. "|" sta per la barra rovescia.
        var barra = ((char)92).ToString();
        var rtf = ("{|rtf1 a|u" + (short)Segnalibri.Segnaposto(0) + "?b}").Replace("|", barra);
        rtf = Segnalibri.DaSegnaposto(rtf, ["Città è"]);
        var nomi = new List<string>();
        Segnalibri.InSegnaposto(rtf, nomi);
        Assert.Equal(["Città è"], nomi);
    }
}

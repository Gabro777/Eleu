using Eleu.Documento;
using Eleu.Editor;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class RtfTests
{
    [Fact]
    public void ScritturaELettura_ConservanoIlDocumento()
    {
        var originale = Esempio();
        var riletto = RtfLettore.Leggi(RtfScrittore.Scrivi(originale));
        Assert.Equal(Descrivi(originale), Descrivi(riletto));
    }

    [Fact]
    public void PassaggioNelCampoDiTesto_ConservaIlDocumento()
    {
        // Il percorso reale: DOCX → modello → RTF → RichEdit → RTF di RichEdit → modello → DOCX.
        var originale = Esempio();
        string rtfDelCampo = "";
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Rtf = RtfScrittore.Scrivi(originale);
            rtfDelCampo = campo.Rtf!;
        });
        Assert.Equal(Descrivi(originale), Descrivi(RtfLettore.Leggi(rtfDelCampo)));
    }

    [Fact]
    public void TitoliImpostatiNelCampo_SonoRiconosciuti()
    {
        int livello = 0;
        string rtf = "";
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = "Titolo\nCorpo";
            campo.Select(0, 0);
            RichEditNativo.ImpostaLivelloTitolo(campo, 2);
            livello = RichEditNativo.LivelloTitolo(campo);
            rtf = campo.Rtf!;
        });
        Assert.Equal(2, livello);
        var contenuto = RtfLettore.Leggi(rtf);
        Assert.Equal(2, contenuto.Paragrafi[0].LivelloTitolo);
        Assert.Equal(0, contenuto.Paragrafi[1].LivelloTitolo);
    }

    [Fact]
    public void AllineamentoGiustificato_ImpostatoELetto()
    {
        Allineamento letto = Allineamento.Sinistra;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = "abc";
            RichEditNativo.ImpostaAllineamento(campo, Allineamento.Giustificato);
            letto = RichEditNativo.LeggiAllineamento(campo);
        });
        Assert.Equal(Allineamento.Giustificato, letto);
    }

    [Fact]
    public void EffettoSuSelezioneMista_RisultaMisto()
    {
        bool? stato = false;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = "uno due";
            campo.Select(0, 3);
            RichEditNativo.ImpostaEffetto(campo, RichEditNativo.Effetto.Grassetto, true);
            campo.SelectAll();
            stato = RichEditNativo.StatoEffetto(campo, RichEditNativo.Effetto.Grassetto);
        });
        Assert.Null(stato);
    }

    [Fact]
    public void RtfRealeDiRichEdit_ConElencoPuntato()
    {
        const string rtf = """
            {\rtf1\ansi\deff0\nouicompat{\fonttbl{\f0\fnil Segoe UI;}{\f1\fnil\fcharset2 Symbol;}}
            {\*\generator Riched20 10.0.26100}\viewkind4\uc1
            \pard\b\f0\fs36\lang1040 Capitolo\par
            Sotto\par

            \pard{\pntext\f1\'B7\tab}{\*\pn\pnlvlblt\pnf1\pnindent360{\pntxtb\'B7}}\fi-360\li360 punto\par
            }
            """;
        var contenuto = RtfLettore.Leggi(rtf);
        Assert.Equal(["Capitolo", "Sotto", "punto"], contenuto.Paragrafi.Select(p => p.Testo));
        Assert.True(contenuto.Paragrafi[0].Segmenti[0].Grassetto);
        Assert.Equal(18f, contenuto.Paragrafi[0].Segmenti[0].Dimensione);
        Assert.False(contenuto.Paragrafi[1].Elenco);
        Assert.True(contenuto.Paragrafi[2].Elenco);
    }

    [Fact]
    public void CaratteriSpecialiEUnicode()
    {
        // La sequenza \u seguita dal codice è spezzata apposta, perché nessuno strumento la trasformi nel carattere.
        var rtf = @"{\rtf1\ansi\ansicpg1252\uc1\pard caff\'e8 \" + "u" + @"8364? \{x\} \\ \emdash\par}";
        Assert.Equal("caffè € {x} \\ —", RtfLettore.Leggi(rtf).Paragrafi[0].Testo);
    }
}

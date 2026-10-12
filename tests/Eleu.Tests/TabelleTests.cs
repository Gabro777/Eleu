using Eleu.Documento;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class TabelleTests
{
    /// <summary>Prima, una tabella 2x2 con la riga di intestazione, Dopo.</summary>
    internal static ContenutoDocumento Esempio()
    {
        var contenuto = new ContenutoDocumento();
        void Paragrafo(string testo, PosizioneCella? cella = null, bool grassetto = false)
        {
            var p = new Paragrafo { Cella = cella };
            p.Aggiungi(new Segmento(testo, Grassetto: grassetto));
            contenuto.Paragrafi.Add(p);
        }
        Paragrafo("Prima");
        Paragrafo("Nome", new PosizioneCella(0, 0, 0, 100, true), grassetto: true);
        Paragrafo("Età", new PosizioneCella(0, 0, 1, 50, true), grassetto: true);
        Paragrafo("Anna", new PosizioneCella(0, 1, 0, 100));
        Paragrafo("", new PosizioneCella(0, 1, 1, 50)); // cella vuota
        Paragrafo("Dopo");
        return contenuto;
    }

    private static string Celle(ContenutoDocumento contenuto) =>
        string.Join(" | ", contenuto.Paragrafi.Select(p => p.Cella is { } c
            ? $"[{c.Tabella},{c.Riga},{c.Colonna} {c.Larghezza}{(c.Intestazione ? " int" : "")}]{p.Testo}"
            : p.Testo));

    [Fact]
    public void Rtf_TabellaScrittaERiletta()
    {
        var riletto = RtfLettore.Leggi(RtfScrittore.Scrivi(Esempio()));
        Assert.Equal(Celle(Esempio()), Celle(riletto));
        Assert.True(riletto.Paragrafi[1].Segmenti[0].Grassetto);
    }

    [Fact]
    public void Rtf_AttraversoIlCampoDiTesto()
    {
        string? rtf = null;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Rtf = RtfScrittore.Scrivi(Esempio());
            rtf = campo.Rtf;
        });
        // Il campo di testo scarta la riga di intestazione (trhdr): Eleu considera intestazione la prima riga.
        Assert.Equal(Celle(Esempio()).Replace(" int", ""), Celle(RtfLettore.Leggi(rtf!)));
    }

    [Fact]
    public void Docx_TabellaScrittaERiletta()
    {
        using var flusso = new MemoryStream();
        DocxFormato.Scrivi(Esempio(), flusso);
        flusso.Position = 0;
        var errori = new DocumentFormat.OpenXml.Validation.OpenXmlValidator()
            .Validate(DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(flusso, false));
        Assert.Empty(errori);

        flusso.Position = 0;
        var riletto = DocxFormato.Leggi(flusso);
        Assert.Equal(Celle(Esempio()), Celle(riletto));
        Assert.Empty(riletto.ElementiNonConservati);
    }

    [Fact]
    public void Rtf_ComeLaScriveWord_RigaRipetutaDopoLeCelle()
    {
        // Word mette la definizione della riga prima delle celle e la ripete in un gruppo prima di "row".
        // Negli RTF dei test "|" sta per la barra rovescia.
        static string Riga(string a, string b) =>
            "|trowd|irow0|trgaph108|cellx2000|cellx4000|pard|plain|intbl " + a + "|cell " + b +
            "|cell |pard|plain|intbl {|trowd|irow0|trgaph108|cellx2000|cellx4000|row }";
        var rtf = ("{|rtf1 " + Riga("Nome", "Anni") + Riga("Anna", "40") + "|pard|plain Dopo|par}").Replace('|', (char)92);
        var letto = RtfLettore.Leggi(rtf);
        Assert.Equal("[0,0,0 100]Nome | [0,0,1 100]Anni | [0,1,0 100]Anna | [0,1,1 100]40 | Dopo", Celle(letto));
    }

    [Fact]
    public void Rtf_DueTabelleSeparate()
    {
        var contenuto = Esempio();
        var p = new Paragrafo { Cella = new PosizioneCella(1, 0, 0, 80) };
        p.Aggiungi(new Segmento("Sola"));
        contenuto.Paragrafi.Add(p);
        var riletto = RtfLettore.Leggi(RtfScrittore.Scrivi(contenuto));
        Assert.Equal(Celle(contenuto), Celle(riletto));
    }
}

public class StrutturaTestoTests
{
    // Il testo del campo con "Prima", una tabella 2x2 e "Dopo", come lo dà Editor.CampoTesto.
    private static readonly string Testo = string.Concat(
        "Prima", "\n",
        StrutturaTesto.InizioRiga, "\n", "A1", StrutturaTesto.FineCella, "B1", StrutturaTesto.FineCella, StrutturaTesto.FineRiga, "\n",
        StrutturaTesto.InizioRiga, "\n", "A2", StrutturaTesto.FineCella, "", StrutturaTesto.FineCella, StrutturaTesto.FineRiga, "\n",
        "Dopo");

    [Fact]
    public void Paragrafi_OgniCellaEUnParagrafoELeRigheTecnicheNo()
    {
        var paragrafi = StrutturaTesto.Paragrafi(Testo).Select(p => Testo[p.Inizio..p.Fine]);
        Assert.Equal(["Prima", "A1", "B1", "A2", "", "Dopo"], paragrafi);
    }

    [Fact]
    public void CellaDi_RigaEColonna()
    {
        int b1 = Testo.IndexOf("B1");
        var cella = StrutturaTesto.CellaDi(Testo, b1 + 1)!;
        Assert.Equal((0, 0, 1), (cella.NumeroTabella, cella.Riga, cella.Colonna));
        Assert.Equal("B1", Testo[cella.Testo.Inizio..cella.Testo.Fine]);

        int a2 = Testo.IndexOf("A2");
        Assert.Equal((1, 0), (StrutturaTesto.CellaDi(Testo, a2)!.Riga, StrutturaTesto.CellaDi(Testo, a2)!.Colonna));
        Assert.Equal((1, 1), (StrutturaTesto.CellaDi(Testo, a2 + 3)!.Riga, StrutturaTesto.CellaDi(Testo, a2 + 3)!.Colonna));
        Assert.Null(StrutturaTesto.CellaDi(Testo, 2));
        Assert.Null(StrutturaTesto.CellaDi(Testo, Testo.Length - 1));
    }

    [Fact]
    public void Posizione_LeCelleSonoRighe()
    {
        var posizione = Posizione.Calcola(Testo, Testo.IndexOf("Dopo") + 2);
        Assert.Equal(new Posizione(6, 3, 6), posizione);
        Assert.Equal(3, Posizione.Calcola(Testo, Testo.IndexOf("B1")).Riga);
    }

    [Fact]
    public void Statistiche_SenzaCaratteriTecnici()
    {
        var statistiche = Statistiche.Calcola(Testo);
        Assert.Equal(5, statistiche.Parole);
        Assert.Equal("PrimaA1B1A2Dopo".Length, statistiche.Caratteri);
    }
}

public class GrigliaTests
{
    private static Griglia Esempio()
    {
        var contenuto = new ContenutoDocumento();
        string[][] righe = [["Nome", "Età"], ["Zeno", "9"], ["anna", "10"], ["Bice", ""]];
        for (int r = 0; r < righe.Length; r++)
            for (int c = 0; c < righe[r].Length; c++)
            {
                var p = new Paragrafo { Cella = new PosizioneCella(0, r, c, c == 0 ? 100 : 40) };
                p.Aggiungi(new Segmento(righe[r][c]));
                contenuto.Paragrafi.Add(p);
            }
        return Griglia.Da(contenuto.Paragrafi);
    }

    private static string Colonna(Griglia g, int c) => string.Join(",", g.Righe.Select(r => Griglia.Testo(r[c])));

    [Fact]
    public void Ordina_LettereSenzaMaiuscoleENumeriComeNumeri()
    {
        var g = Esempio();
        g.Ordina(0, crescente: true, intestazione: true);
        Assert.Equal("Nome,anna,Bice,Zeno", Colonna(g, 0));

        g.Ordina(1, crescente: true, intestazione: true);
        Assert.Equal("Età,9,10,", Colonna(g, 1)); // i numeri prima, le celle vuote in fondo

        g.Ordina(1, crescente: false, intestazione: true);
        Assert.Equal("Età,,10,9", Colonna(g, 1));
    }

    [Fact]
    public void InserisciEliminaColonna_LarghezzeAllineate()
    {
        var g = Esempio();
        g.InserisciColonna(1);
        Assert.Equal([100f, 100f, 40f], g.Larghezze);
        Assert.Equal(3, g.Colonne);
        g.EliminaColonna(0);
        Assert.Equal([100f, 40f], g.Larghezze);
        var paragrafi = g.Paragrafi();
        Assert.All(paragrafi, p => Assert.NotNull(p.Cella));
        Assert.Equal(40f, paragrafi.First(p => p.Cella!.Colonna == 1).Cella!.Larghezza);
    }

    [Fact]
    public void Leggibile_UnaRigaPerParagrafoConIlNomeDellaColonna()
    {
        var contenuto = new ContenutoDocumento();
        var prima = new Paragrafo();
        prima.Aggiungi(new Segmento("Prima"));
        contenuto.Paragrafi.Add(prima);
        contenuto.Paragrafi.AddRange(Esempio().Paragrafi());

        var testi = Griglia.Leggibile(contenuto).Paragrafi.Select(p => p.Testo);
        Assert.Equal(["Prima", "Tabella: Nome; Età", "Nome: Zeno; Età: 9", "Nome: anna; Età: 10", "Nome: Bice", "Fine tabella"], testi);
    }
}

public class IntestazioniTabelleTests
{
    [Fact]
    public void IntestazioniDelModello_ContaLePrimeRigheDiFila()
    {
        var c = new Eleu.Documento.ContenutoDocumento();
        void Cella(int t, int r, bool intestazione) => c.Paragrafi.Add(new Eleu.Documento.Paragrafo { Cella = new Eleu.Documento.PosizioneCella(t, r, 0, Intestazione: intestazione) });
        Cella(0, 0, true); Cella(0, 1, true); Cella(0, 2, false);
        Cella(1, 0, false); Cella(1, 1, true); // un'intestazione non in cima non conta
        Cella(2, 0, true);
        var conteggi = FinestraPrincipale.IntestazioniDelModello(c);
        Assert.Equal(2, conteggi[0]);
        Assert.Equal(0, conteggi[1]);
        Assert.Equal(1, conteggi[2]);
    }
}

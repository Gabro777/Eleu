using System.Drawing;
using Eleu.Braille;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class GraficaTattileTests
{
    /// <summary>Un quadrato nero pieno al centro di un foglio bianco.</summary>
    private static Bitmap Quadrato()
    {
        var b = new Bitmap(400, 400);
        using var g = Graphics.FromImage(b);
        g.Clear(Color.White);
        g.FillRectangle(Brushes.Black, 50, 50, 300, 300);
        return b;
    }

    private static int Punti(IEnumerable<string> righe) => righe.Sum(r => r.Sum(c => int.PopCount(c - TraduttoreBraille.CellaVuota)));

    [Fact]
    public void Misure_RispettanoProporzioniEMassimi()
    {
        // Quadrato largo 30 celle (186 mm): alto 186 mm, cioè 19 righe da 10 mm.
        Assert.Equal((30, 19), GraficaTattile.Misure(100, 100, 30, 27));
        // Molto alto: comanda l'altezza, la larghezza si riduce.
        var (celle, righe) = GraficaTattile.Misure(100, 1000, 30, 10);
        Assert.Equal(10, righe);
        Assert.True(celle <= 3);
    }

    [Fact]
    public void Riempito_ConInterno_Contorni_SoloBordo()
    {
        using var q = Quadrato();
        var pieno = GraficaTattile.InCelle(q, 20, 30, new OpzioniGrafica(TrattoTattile.Riempito));
        var contorni = GraficaTattile.InCelle(q, 20, 30, new OpzioniGrafica(TrattoTattile.Contorni));
        Assert.All(pieno.Concat(contorni), r => Assert.Equal(20, r.Length));
        Assert.All(pieno.SelectMany(r => r), c => Assert.InRange(c, '⠀', '⠿'));

        // Al centro: pieno nel primo, vuoto nel secondo.
        int mezzo = pieno.Count / 2;
        Assert.Equal('⠿', pieno[mezzo][10]);
        Assert.Equal('⠀', contorni[mezzo][10]);
        Assert.True(Punti(contorni) > 0);
        Assert.True(Punti(contorni) < Punti(pieno));
        // Il bordo sinistro del quadrato è in rilievo.
        Assert.Contains(contorni[mezzo].Take(6), c => c != '⠀');
    }

    [Fact]
    public void Inverti_ScambiaChiaroEScuro()
    {
        using var q = Quadrato();
        var normale = GraficaTattile.InCelle(q, 20, 30, new OpzioniGrafica(TrattoTattile.Riempito));
        var invertito = GraficaTattile.InCelle(q, 20, 30, new OpzioniGrafica(TrattoTattile.Riempito, Inverti: true));
        int mezzo = normale.Count / 2;
        Assert.Equal('⠿', normale[mezzo][10]);
        Assert.Equal('⠀', invertito[mezzo][10]);
    }

    [Fact]
    public void Pagine_DidascaliaInCimaPoiImmagine()
    {
        var immagine = Enumerable.Repeat(new string('⠿', 5), 30).ToList();
        var pagine = GraficaTattile.Pagine(immagine, 10, 25, "⠁⠃⠀⠉");
        Assert.Equal(2, pagine.Count);
        Assert.Equal("⠁⠃⠀⠉", pagine[0][0]);
        Assert.Equal("", pagine[0][1]);
        Assert.Equal(25, pagine[0].Count);
        Assert.Equal(32 - 25, pagine[1].Count);
        Assert.Contains("\f", FormatoBrf.Testo(pagine));
    }

    [Fact]
    public void AndaACapo_SulleParole()
    {
        var parola = new string('⠁', 4);
        var testo = string.Join('⠀', Enumerable.Repeat(parola, 5)); // 5 parole da 4 celle
        var righe = GraficaTattile.AndaACapo(testo, 10);
        Assert.All(righe, r => Assert.True(r.Length <= 10));
        Assert.Equal(3, righe.Count); // 4+1+4, 4+1+4, 4
        Assert.Equal(new string('⠁', 15), GraficaTattile.AndaACapo(new string('⠁', 15), 10).Aggregate((a, b) => a + b));
    }

    [Fact]
    public void CartaARilievo_NeroSoloDoveServe()
    {
        using var q = Quadrato();
        using var foglio = GraficaTattile.PerCartaARilievo(q, 100, 120, new OpzioniGrafica(TrattoTattile.Contorni), ["⠇"]);
        Assert.Equal(1000, foglio.Width);
        Assert.Equal(1200, foglio.Height);
        // Il punto 1 della didascalia (in alto a sinistra) è nero, il centro del quadrato bianco (solo contorni).
        Assert.Equal(Color.Black.ToArgb(), foglio.GetPixel(8, 8).ToArgb());
        Assert.Equal(Color.White.ToArgb(), foglio.GetPixel(500, 650).ToArgb());
    }

    [Fact]
    public void Finestra_SiApreConAnteprima()
    {
        EseguiSta(() =>
        {
            using var q = Quadrato();
            using var finestra = new Interfaccia.FinestraGraficaTattile(q, "Un quadrato", ["Stampante"], null, 32, 27, _ => "⠁");
            var anteprima = finestra.Controls.Cast<Control>().SelectMany(Tutti).OfType<TextBox>().First(t => t.ReadOnly);
            Assert.Contains('⠀', anteprima.Text);
        });

        static IEnumerable<Control> Tutti(Control c) => c.Controls.Cast<Control>().SelectMany(Tutti).Prepend(c);
    }
}

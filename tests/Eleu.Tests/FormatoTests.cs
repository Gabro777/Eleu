using Eleu.Documento;
using Eleu.Editor;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class FormatoTests
{
    private static T ConCampo<T>(string testo, Func<RichTextBox, T> azione)
    {
        T risultato = default!;
        EseguiSta(() =>
        {
            using var campo = new RichTextBox();
            campo.CreateControl();
            campo.Text = testo;
            risultato = azione(campo);
        });
        return risultato;
    }

    [Fact]
    public void Carattere_ImpostatoERiletto()
    {
        var voluto = new FormatoCarattere("Times New Roman", 14, true, false, false, true, PosizioneTesto.Apice,
            new ValoreColore(0xC00000), new ValoreColore(0xFFFF00), 1.5f);
        var letto = ConCampo("prova", campo =>
        {
            campo.SelectAll();
            RichEditNativo.ImpostaFormatoCarattere(campo, voluto);
            return RichEditNativo.LeggiFormatoCarattere(campo);
        });
        Assert.Equal(voluto, letto);
    }

    [Fact]
    public void Carattere_SoloLeProprietaIndicateCambiano()
    {
        var letto = ConCampo("prova", campo =>
        {
            campo.SelectAll();
            RichEditNativo.ImpostaFormatoCarattere(campo, new FormatoCarattere(Grassetto: true, Colore: new ValoreColore(0x0000FF)));
            RichEditNativo.ImpostaFormatoCarattere(campo, new FormatoCarattere(Colore: new ValoreColore(null)));
            return RichEditNativo.LeggiFormatoCarattere(campo);
        });
        Assert.True(letto.Grassetto);
        Assert.Equal(new ValoreColore(null), letto.Colore);
    }

    [Fact]
    public void Paragrafo_ImpostatoERiletto()
    {
        var voluto = new FormatoParagrafo(Allineamento.Giustificato, 36, 18, -18, 6, 12, 1.5f, TipoElenco.Nessuno);
        var letto = ConCampo("prova", campo =>
        {
            RichEditNativo.ImpostaFormatoParagrafo(campo, voluto);
            return RichEditNativo.LeggiFormatoParagrafo(campo);
        });
        Assert.Equal(voluto, letto);
    }

    [Fact]
    public void ElencoNumerato_RiconosciutoNelSalvataggio()
    {
        var rtf = ConCampo("uno\ndue", campo =>
        {
            campo.SelectAll();
            RichEditNativo.ImpostaFormatoParagrafo(campo, new FormatoParagrafo(Elenco: TipoElenco.Numerato, RientroSinistro: 0));
            return campo.Rtf!;
        });
        var contenuto = RtfLettore.Leggi(rtf);
        Assert.All(contenuto.Paragrafi, p => Assert.True(p.Numerato));
        Assert.All(contenuto.Paragrafi, p => Assert.Equal(0f, p.RientroSinistro));
        Assert.Equal(["uno", "due"], contenuto.Paragrafi.Select(p => p.Testo));
    }

    [Theory]
    [InlineData(Maiuscole.Maiuscolo, "CIAO MONDO")]
    [InlineData(Maiuscole.Minuscolo, "ciao mondo")]
    [InlineData(Maiuscole.Iniziali, "Ciao Mondo")]
    public void Maiuscole_CambiaIlTestoEConservaLaFormattazione(Maiuscole tipo, string atteso)
    {
        var (testo, grassetto) = ConCampo("ciao Mondo", campo =>
        {
            campo.Select(0, 4);
            RichEditNativo.ImpostaEffetto(campo, RichEditNativo.Effetto.Grassetto, true);
            RichEditNativo.CambiaMaiuscole(campo, 0, campo.TextLength, tipo);
            campo.Select(0, 4);
            return (campo.Text, RichEditNativo.StatoEffetto(campo, RichEditNativo.Effetto.Grassetto));
        });
        Assert.Equal(atteso, testo);
        Assert.True(grassetto);
    }
}

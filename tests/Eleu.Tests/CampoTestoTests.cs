using Eleu.Editor;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class CampoTestoTests
{
    [Fact]
    public void TestoNormale_FuoriDallaPresaDelFocus()
    {
        string letto = "";
        EseguiSta(() =>
        {
            using var campo = new CampoTesto();
            campo.CreateControl();
            campo.Text = "uno\ndue";
            letto = campo.Text;
        });
        Assert.Equal("uno\ndue", letto);
    }

    [Fact]
    public void DurantePresaDelFocus_IlTestoRisultaVuoto()
    {
        // È ciò che legge la notifica di Windows Forms: vuota, lo screen reader non ripete il documento.
        string letto = "non letto";
        EseguiSta(() =>
        {
            using var campo = new CampoDiProva();
            campo.CreateControl();
            campo.Text = "documento lungo";
            campo.SimulaPresaFocus();
            letto = campo.TestoDuranteFocus;
        });
        Assert.Equal("", letto);
    }

    private sealed class CampoDiProva : CampoTesto
    {
        public string TestoDuranteFocus { get; private set; } = "non letto";

        public CampoDiProva() => GotFocus += (_, _) => TestoDuranteFocus = Text;

        public void SimulaPresaFocus() => OnGotFocus(EventArgs.Empty);
    }
}

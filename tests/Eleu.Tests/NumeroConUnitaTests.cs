using Eleu.Interfaccia;
using static Eleu.Tests.Supporto;

namespace Eleu.Tests;

public class NumeroConUnitaTests
{
    private static (decimal Valore, string Testo) Scrivi(string scritto)
    {
        (decimal, string) risultato = default;
        EseguiSta(() =>
        {
            using var modulo = new Form();
            var campo = new NumeroConUnita("cm", 2, 0, 10, 0.1m, 2);
            modulo.Controls.Add(campo);
            modulo.CreateControl();
            campo.Text = scritto; // come se l'utente scrivesse nel campo
            risultato = (campo.Value, campo.Text);
        });
        return risultato;
    }

    [Fact]
    public void MostraIlValoreConLUnita()
    {
        var (_, testo) = Scrivi("2");
        Assert.EndsWith(" cm", testo);
    }

    [Theory]
    [InlineData("3,5", 3.5)]
    [InlineData("3.5 cm", 3.5)]
    [InlineData("4 cm", 4)]
    [InlineData("99", 10)]
    [InlineData("abc", 2)]
    public void InterpretaCiòCheSiScrive(string scritto, double atteso)
    {
        Assert.Equal((decimal)atteso, Scrivi(scritto).Valore);
    }
}

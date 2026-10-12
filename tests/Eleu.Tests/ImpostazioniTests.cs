namespace Eleu.Tests;

public sealed class ImpostazioniTests : IDisposable
{
    private readonly string _cartella = Path.Combine(Path.GetTempPath(), "eleu-test-" + Guid.NewGuid());

    public void Dispose()
    {
        if (Directory.Exists(_cartella))
            Directory.Delete(_cartella, true);
    }

    [Fact]
    public void FileRecenti_InCimaSenzaDoppioniMassimoCinque()
    {
        var impostazioni = new Impostazioni();
        for (int i = 1; i <= 7; i++)
            impostazioni.AggiungiRecente($@"C:\doc{i}.docx");
        impostazioni.AggiungiRecente(@"c:\DOC5.docx");

        Assert.Equal([@"c:\DOC5.docx", @"C:\doc7.docx", @"C:\doc6.docx", @"C:\doc4.docx", @"C:\doc3.docx"], impostazioni.FileRecenti);
    }

    [Fact]
    public void SalvaECarica()
    {
        var percorso = Path.Combine(_cartella, "impostazioni.json");
        var impostazioni = new Impostazioni { Zoom = 1.5f };
        impostazioni.AggiungiRecente(@"C:\lettera.docx");
        impostazioni.Salva(percorso);

        var caricate = Impostazioni.Carica(percorso);
        Assert.Equal(1.5f, caricate.Zoom);
        Assert.Equal([@"C:\lettera.docx"], caricate.FileRecenti);
    }

    [Fact]
    public void FileMancanteOCorrotto_DannoIValoriPredefiniti()
    {
        Assert.Equal(1f, Impostazioni.Carica(Path.Combine(_cartella, "manca.json")).Zoom);

        Directory.CreateDirectory(_cartella);
        var corrotto = Path.Combine(_cartella, "corrotto.json");
        File.WriteAllText(corrotto, "{ non è json");
        Assert.Equal(1f, Impostazioni.Carica(corrotto).Zoom);
    }

    [Fact]
    public void Ripristino_SalvaLeggiElimina()
    {
        using var ripristino = new Ripristino(_cartella);
        Assert.False(ripristino.Esiste);

        ripristino.Salva(@"{\rtf1 ciao}", @"C:\lettera.docx");
        Assert.True(ripristino.Esiste);
        Assert.Equal((@"{\rtf1 ciao}", @"C:\lettera.docx"), ripristino.Leggi());

        ripristino.Elimina();
        Assert.False(ripristino.Esiste);
    }

    [Fact]
    public void Ripristino_SoloDaFinestreChiuse()
    {
        var aperta = new Ripristino(_cartella);
        aperta.Salva("testo di prova", null);
        // Un secondo Eleu non deve proporre il documento di una finestra ancora aperta.
        Assert.Null(Ripristino.Abbandonato(_cartella));

        aperta.Dispose(); // come se Eleu si chiudesse male: la copia resta, il blocco no
        using var abbandonata = Ripristino.Abbandonato(_cartella);
        Assert.NotNull(abbandonata);
        Assert.Equal(("testo di prova", (string?)null), abbandonata.Leggi());

        abbandonata.Elimina();
        Assert.Null(Ripristino.Abbandonato(_cartella));
    }
}

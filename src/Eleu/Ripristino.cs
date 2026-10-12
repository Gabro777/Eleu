namespace Eleu;

/// <summary>
/// Copia di sicurezza del documento non salvato, scritta periodicamente.
/// Se Eleu si chiude in modo anomalo, al riavvio viene proposto il recupero.
/// Non tocca mai il file originale dell'utente.
/// Ogni finestra di Eleu ha la sua copia e la tiene "bloccata" finché è aperta: un secondo Eleu
/// propone di recuperare solo le copie di finestre che non ci sono più.
/// </summary>
public sealed class Ripristino(string cartella, string? id = null) : IDisposable
{
    public static string CartellaPredefinita { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eleu");

    private const string Prefisso = "ripristino";

    private readonly string _id = id ?? Guid.NewGuid().ToString("N")[..8];
    private FileStream? _blocco;

    // L'id vuoto è la copia unica delle versioni precedenti di Eleu ("ripristino.rtf").
    private string Nome(string estensione) =>
        Path.Combine(cartella, _id.Length > 0 ? $"{Prefisso}-{_id}.{estensione}" : $"{Prefisso}.{estensione}");

    private string FileRtf => Nome("rtf");

    private string FilePercorso => Nome("txt");

    private string FileBlocco => Nome("lock");

    public bool Esiste => File.Exists(FileRtf);

    /// <summary>La copia lasciata da un Eleu che non è più aperto, se c'è.</summary>
    public static Ripristino? Abbandonato(string cartella)
    {
        if (!Directory.Exists(cartella))
            return null;
        foreach (var rtf in Directory.EnumerateFiles(cartella, $"{Prefisso}*.rtf"))
        {
            var nome = Path.GetFileNameWithoutExtension(rtf);
            var id = nome.Length > Prefisso.Length + 1 ? nome[(Prefisso.Length + 1)..] : "";
            var candidato = new Ripristino(cartella, id);
            if (!candidato.InUso())
                return candidato;
        }
        return null;
    }

    /// <summary>Vero se la finestra che ha scritto la copia è ancora aperta (tiene il blocco).</summary>
    private bool InUso()
    {
        try
        {
            using var prova = new FileStream(FileBlocco, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    public void Salva(string rtf, string? percorsoOriginale)
    {
        try
        {
            Directory.CreateDirectory(cartella);
            // Il blocco sparisce da solo quando la finestra si chiude, anche se Eleu si chiude male.
            _blocco ??= new FileStream(FileBlocco, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
            File.WriteAllText(FileRtf, rtf, System.Text.Encoding.Latin1);
            File.WriteAllText(FilePercorso, percorsoOriginale ?? "");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Il ripristino è una rete di sicurezza: un errore qui non deve disturbare chi scrive.
        }
    }

    public (string Rtf, string? PercorsoOriginale) Leggi()
    {
        var rtf = File.ReadAllText(FileRtf, System.Text.Encoding.Latin1);
        var percorso = File.Exists(FilePercorso) ? File.ReadAllText(FilePercorso).Trim() : "";
        return (rtf, percorso.Length > 0 ? percorso : null);
    }

    public void Elimina()
    {
        try
        {
            File.Delete(FileRtf);
            File.Delete(FilePercorso);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    public void Dispose()
    {
        _blocco?.Dispose();
        _blocco = null;
    }
}

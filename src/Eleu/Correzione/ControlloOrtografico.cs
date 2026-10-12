using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Eleu.Correzione;

/// <summary>
/// Il controllo ortografico incluso in Windows (lo stesso di Edge e delle app di sistema),
/// con il dizionario della lingua scelta. Non serve installare dizionari a parte.
/// </summary>
public sealed class ControlloOrtografico
{
    private readonly ISpellChecker _correttore;

    private ControlloOrtografico(ISpellChecker correttore) => _correttore = correttore;

    /// <summary>Il correttore per la lingua indicata, oppure null se Windows non la supporta.</summary>
    public static ControlloOrtografico? Crea(string lingua = "it-IT")
    {
        try
        {
            var tipo = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            if (tipo is null || Activator.CreateInstance(tipo) is not ISpellCheckerFactory fabbrica)
                return null;
            return fabbrica.IsSupported(lingua) ? new ControlloOrtografico(fabbrica.CreateSpellChecker(lingua)) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Le parole sconosciute nel testo, come (inizio, lunghezza).</summary>
    public IEnumerable<(int Inizio, int Lunghezza)> ParoleSconosciute(string testo)
    {
        if (testo.Length == 0)
            yield break;
        var errori = _correttore.Check(testo);
        while (errori.Next(out var errore) == 0 && errore is not null)
            yield return ((int)errore.StartIndex, (int)errore.Length);
    }

    public IReadOnlyList<string> Suggerimenti(string parola)
    {
        var risultati = new List<string>();
        var elenco = _correttore.Suggest(parola);
        var uno = new string[1];
        while (elenco.Next(1, uno, IntPtr.Zero) == 0)
            risultati.Add(uno[0]);
        return risultati;
    }

    public bool Corretta(string parola) => !ParoleSconosciute(parola).Any();

    // Interfacce COM dell'API di controllo ortografico di Windows (spellcheck.h).
    // L'ordine dei metodi deve corrispondere alla vtable: quelli non usati restano come segnaposto.

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { get; }

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string lingua);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string lingua);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string testo);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string parola);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError? errore);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }

        uint Length { get; }
    }
}

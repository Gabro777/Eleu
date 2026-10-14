using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Eleu.Proofing;

/// <summary>
/// The spell checker built into Windows (the same as Edge and the system apps),
/// with the dictionary of the chosen language. No separate dictionaries need to be installed.
/// </summary>
public sealed class SpellChecker
{
    private readonly ISpellChecker _speller;

    private SpellChecker(ISpellChecker speller) => _speller = speller;

    /// <summary>The speller for the given language, or null if Windows does not support it.</summary>
    public static SpellChecker? Create(string language = "it-IT")
    {
        try
        {
            var kind = Type.GetTypeFromCLSID(new Guid("7AB36653-1796-484B-BDFA-E74F1DB7C1DC"));
            if (kind is null || Activator.CreateInstance(kind) is not ISpellCheckerFactory factory)
                return null;
            return factory.IsSupported(language) ? new SpellChecker(factory.CreateSpellChecker(language)) : null;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>The unknown words in the text, as (start, length).</summary>
    public IEnumerable<(int Start, int Length)> UnknownWords(string text)
    {
        if (text.Length == 0)
            yield break;
        var errors = _speller.Check(text);
        while (errors.Next(out var error) == 0 && error is not null)
            yield return ((int)error.StartIndex, (int)error.Length);
    }

    public IReadOnlyList<string> Suggestions(string word)
    {
        var results = new List<string>();
        var list = _speller.Suggest(word);
        var one = new string[1];
        while (list.Next(1, one, IntPtr.Zero) == 0)
            results.Add(one[0]);
        return results;
    }

    public bool IsCorrect(string word) => !UnknownWords(word).Any();

    // COM interfaces of the Windows spell checking API (spellcheck.h).
    // The method order must match the vtable: unused ones stay as placeholders.

    [ComImport, Guid("8E018A9D-2415-4677-BF08-794EA61F94BB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellCheckerFactory
    {
        IEnumString SupportedLanguages { get; }

        [return: MarshalAs(UnmanagedType.Bool)]
        bool IsSupported([MarshalAs(UnmanagedType.LPWStr)] string language);

        ISpellChecker CreateSpellChecker([MarshalAs(UnmanagedType.LPWStr)] string language);
    }

    [ComImport, Guid("B6FD0B71-E2BC-4653-8D05-F197E412770B"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellChecker
    {
        string LanguageTag { [return: MarshalAs(UnmanagedType.LPWStr)] get; }

        IEnumSpellingError Check([MarshalAs(UnmanagedType.LPWStr)] string text);

        IEnumString Suggest([MarshalAs(UnmanagedType.LPWStr)] string word);
    }

    [ComImport, Guid("803E3BD4-2828-4410-8290-418D1D73C762"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumSpellingError
    {
        [PreserveSig]
        int Next(out ISpellingError? error);
    }

    [ComImport, Guid("B7C82D61-FBE8-4B47-9B27-6C0D2E0DE0A3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ISpellingError
    {
        uint StartIndex { get; }

        uint Length { get; }
    }
}

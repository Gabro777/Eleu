using System.Runtime.InteropServices;

namespace Eleu.Accessibilita;

/// <summary>
/// Imposta il nome che lo screen reader legge per un controllo Win32 nativo.
/// Il RichEdit espone un proprio provider UI Automation che ignora <see cref="Control.AccessibleName"/>
/// e si presenta come "RichEdit Control": qui lo si sovrascrive con le annotazioni di accessibilità di Windows.
/// </summary>
public static class NomeAccessibile
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const uint ChildIdSelf = 0;

    private static readonly Guid PropIdAccName = new("608d3df8-8128-4aa7-a428-f55e49267291");
    private static readonly Guid UiaNameProperty = new("c3a6921b-4a99-44f1-bca6-61187052c431");

    public static void Imposta(Control controllo, string nome)
    {
        controllo.AccessibleName = nome;
        if (controllo.IsHandleCreated)
            Annota(controllo.Handle, nome);
        else
            controllo.HandleCreated += (_, _) => Annota(controllo.Handle, nome);
    }

    private static void Annota(IntPtr hwnd, string nome)
    {
        var servizi = (IAccPropServices)new AccPropServices();
        servizi.SetHwndPropStr(hwnd, ObjIdClient, ChildIdSelf, PropIdAccName, nome);
        servizi.SetHwndPropStr(hwnd, ObjIdClient, ChildIdSelf, UiaNameProperty, nome);
    }

    [ComImport, Guid("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7")]
    private class AccPropServices;

    [ComImport, Guid("6e26e776-04f0-495d-80e4-3330352e3169"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccPropServices
    {
        void SetPropValue(); // non usato, segnaposto per l'ordine della vtable
        void SetPropServer(); // non usato
        void ClearProps(); // non usato
        void SetHwndProp(); // non usato

        void SetHwndPropStr(IntPtr hwnd, uint idObject, uint idChild, Guid idProp,
            [MarshalAs(UnmanagedType.LPWStr)] string str);
    }
}

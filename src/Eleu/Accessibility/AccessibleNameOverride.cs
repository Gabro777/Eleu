using System.Runtime.InteropServices;

namespace Eleu.Accessibility;

/// <summary>
/// Sets the name the screen reader reads for a native Win32 control.
/// RichEdit exposes its own UI Automation provider that ignores <see cref="Control.AccessibleName"/>
/// and presents itself as "RichEdit Control": here it is overridden with Windows accessibility annotations.
/// </summary>
public static class AccessibleNameOverride
{
    private const uint ObjIdClient = 0xFFFFFFFC;
    private const uint ChildIdSelf = 0;

    private static readonly Guid PropIdAccName = new("608d3df8-8128-4aa7-a428-f55e49267291");
    private static readonly Guid UiaNameProperty = new("c3a6921b-4a99-44f1-bca6-61187052c431");

    public static void Set(Control control, string name)
    {
        control.AccessibleName = name;
        if (control.IsHandleCreated)
            Annotate(control.Handle, name);
        else
            control.HandleCreated += (_, _) => Annotate(control.Handle, name);
    }

    private static void Annotate(IntPtr hwnd, string name)
    {
        var services = (IAccPropServices)new AccPropServices();
        services.SetHwndPropStr(hwnd, ObjIdClient, ChildIdSelf, PropIdAccName, name);
        services.SetHwndPropStr(hwnd, ObjIdClient, ChildIdSelf, UiaNameProperty, name);
    }

    [ComImport, Guid("b5f8350b-0548-48b1-a6ee-88bd00b4a5e7")]
    private class AccPropServices;

    [ComImport, Guid("6e26e776-04f0-495d-80e4-3330352e3169"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAccPropServices
    {
        void SetPropValue(); // unused, placeholder for the vtable order
        void SetPropServer(); // unused
        void ClearProps(); // unused
        void SetHwndProp(); // unused

        void SetHwndPropStr(IntPtr hwnd, uint idObject, uint idChild, Guid idProp,
            [MarshalAs(UnmanagedType.LPWStr)] string str);
    }
}

using System.Runtime.InteropServices;
using System.Text;

namespace MonCore.MacOS;

public static partial class MacBootIdentity
{
    public static string? Read()
    {
        if (!OperatingSystem.IsMacOS()) return null;
        var bytes = new byte[64];
        nuint length = (nuint)bytes.Length;
        if (Sysctl("kern.bootsessionuuid", bytes, ref length, IntPtr.Zero, 0) != 0 || length == 0 || length > (nuint)bytes.Length) return null;
        var text = Encoding.UTF8.GetString(bytes, 0, (int)length).TrimEnd('\0');
        return Guid.TryParse(text, out var id) ? id.ToString("D") : null;
    }

    [LibraryImport("/usr/lib/libSystem.B.dylib", EntryPoint = "sysctlbyname", StringMarshalling = StringMarshalling.Utf8)]
    private static partial int Sysctl(string name, [Out] byte[] value, ref nuint length, IntPtr newValue, nuint newLength);
}

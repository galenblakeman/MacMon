using System.Runtime.InteropServices;

namespace MonCore.MacOS;

/// <summary>Bounded, type-checked access to borrowed CoreFoundation values.</summary>
internal static partial class DarwinObjects
{
    internal const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    internal const string CF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    internal static IntPtr Key(string key) => CFStringCreateWithCString(IntPtr.Zero, key, 0x08000100);
    internal static IntPtr Get(IntPtr dictionary, string key)
    {
        if (dictionary == IntPtr.Zero || CFGetTypeID(dictionary) != CFDictionaryGetTypeID()) return IntPtr.Zero;
        var name = Key(key);
        try { return name == IntPtr.Zero ? IntPtr.Zero : CFDictionaryGetValue(dictionary, name); }
        finally { Release(name); }
    }
    internal static IntPtr Property(uint entry, string key)
    {
        var name = Key(key);
        try { return name == IntPtr.Zero ? IntPtr.Zero : IORegistryEntryCreateCFProperty(entry, name, IntPtr.Zero, 0); }
        finally { Release(name); }
    }
    internal static T Property<T>(uint entry, string key, Func<IntPtr, T> read)
    { var value = Property(entry, key); try { return read(value); } finally { Release(value); } }
    internal static double? Number(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() &&
        CFNumberGetValue(value, 13, out var number) && double.IsFinite(number) ? number : null;
    internal static ulong? Counter(IntPtr value) => value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() &&
        CFNumberGetInteger(value, 4, out var number) && number >= 0 ? (ulong)number : null;
    internal static string Text(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFStringGetTypeID() || CFStringGetLength(value) > 1024) return "";
        var buffer = Marshal.AllocHGlobal(4097);
        try { return CFStringGetCString(value, buffer, 4097, 0x08000100) ? Marshal.PtrToStringUTF8(buffer) ?? "" : ""; }
        finally { Marshal.FreeHGlobal(buffer); }
    }
    internal static byte[] Data(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFDataGetTypeID()) return [];
        var count = CFDataGetLength(value);
        if (count is <= 0 or > 8192) return [];
        var result = new byte[(int)count]; Marshal.Copy(CFDataGetBytePtr(value), result, 0, result.Length); return result;
    }
    internal static IntPtr[] Array(IntPtr value)
    {
        if (value == IntPtr.Zero || CFGetTypeID(value) != CFArrayGetTypeID()) return [];
        var count = CFArrayGetCount(value);
        return count is >= 0 and <= 1024 ? Enumerable.Range(0, (int)count).Select(i => CFArrayGetValueAtIndex(value, i)).ToArray() : [];
    }
    internal static List<T> Services<T>(string name, Func<uint, T> read)
    {
        var result = new List<T>(); var match = IOServiceMatching(name);
        if (match == IntPtr.Zero || IOServiceGetMatchingServices(0, match, out var iterator) != 0) return result;
        try
        {
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            { try { result.Add(read(service)); } finally { IOObjectRelease(service); } }
        }
        finally { IOObjectRelease(iterator); }
        return result;
    }
    internal static void Release(IntPtr value) { if (value != IntPtr.Zero) CFRelease(value); }
    [LibraryImport(CF)] internal static partial void CFRelease(IntPtr value);
    [LibraryImport(CF)] internal static partial nuint CFGetTypeID(IntPtr value);
    [LibraryImport(CF)] private static partial nuint CFDictionaryGetTypeID();
    [LibraryImport(CF)] private static partial nuint CFNumberGetTypeID();
    [LibraryImport(CF)] private static partial nuint CFStringGetTypeID();
    [LibraryImport(CF)] private static partial nuint CFDataGetTypeID();
    [LibraryImport(CF)] private static partial nuint CFArrayGetTypeID();
    [LibraryImport(CF)] private static partial nint CFStringGetLength(IntPtr value);
    [LibraryImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static partial bool CFStringGetCString(IntPtr value, IntPtr buffer, nint size, uint encoding);
    [LibraryImport(CF, StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string value, uint encoding);
    [LibraryImport(CF)] private static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [LibraryImport(CF)] [return: MarshalAs(UnmanagedType.I1)] private static partial bool CFNumberGetValue(IntPtr value, int type, out double number);
    [LibraryImport(CF, EntryPoint = "CFNumberGetValue")] [return: MarshalAs(UnmanagedType.I1)] private static partial bool CFNumberGetInteger(IntPtr value, int type, out long number);
    [LibraryImport(CF)] private static partial nint CFDataGetLength(IntPtr value);
    [LibraryImport(CF)] private static partial IntPtr CFDataGetBytePtr(IntPtr value);
    [LibraryImport(CF)] private static partial nint CFArrayGetCount(IntPtr value);
    [LibraryImport(CF)] private static partial IntPtr CFArrayGetValueAtIndex(IntPtr value, nint index);
    [LibraryImport(IOKit, StringMarshalling = StringMarshalling.Utf8)] private static partial IntPtr IOServiceMatching(string name);
    [LibraryImport(IOKit)] private static partial int IOServiceGetMatchingServices(uint port, IntPtr matching, out uint iterator);
    [LibraryImport(IOKit)] internal static partial uint IOIteratorNext(uint iterator);
    [LibraryImport(IOKit)] internal static partial int IOObjectRelease(uint value);
    [LibraryImport(IOKit)] private static partial IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
    [LibraryImport(IOKit)] internal static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong id);
}

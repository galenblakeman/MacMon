using System.Runtime.InteropServices;

namespace MonCore.MacOS;

/// <summary>Aggregate counters from IOBlockStorageDriver Statistics; device-set changes require a fresh baseline.</summary>
internal static partial class DarwinStorageReader
{
    private const string IOKit = "/System/Library/Frameworks/IOKit.framework/IOKit";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    internal static (string Key, ulong? Read, ulong? Written) Read()
    {
        var matching = IOServiceMatching("IOBlockStorageDriver");
        if (matching == IntPtr.Zero) return ("", null, null);
        // MatchingServices consumes the matching dictionary, including on failure.
        if (IOServiceGetMatchingServices(0, matching, out var iterator) != 0) return ("", null, null);
        var statisticsKey = CFStringCreateWithCString(IntPtr.Zero, "Statistics", 0x08000100);
        var readKey = CFStringCreateWithCString(IntPtr.Zero, "Bytes (Read)", 0x08000100);
        var writeKey = CFStringCreateWithCString(IntPtr.Zero, "Bytes (Write)", 0x08000100);
        try
        {
            if (statisticsKey == IntPtr.Zero || readKey == IntPtr.Zero || writeKey == IntPtr.Zero) return ("", null, null);
            var ids = new List<ulong>();
            ulong read = 0, written = 0;
            uint service;
            while ((service = IOIteratorNext(iterator)) != 0)
            {
                try
                {
                    if (IORegistryEntryGetRegistryEntryID(service, out var id) != 0) return ("", null, null);
                    var dictionary = IORegistryEntryCreateCFProperty(service, statisticsKey, IntPtr.Zero, 0);
                    if (dictionary == IntPtr.Zero) return ("", null, null);
                    try
                    {
                        if (CFGetTypeID(dictionary) != CFDictionaryGetTypeID()) return ("", null, null);
                        var bytesRead = Number(dictionary, readKey);
                        var bytesWritten = Number(dictionary, writeKey);
                        if (bytesRead is null || bytesWritten is null) return ("", null, null);
                        read = checked(read + bytesRead.Value);
                        written = checked(written + bytesWritten.Value);
                        ids.Add(id);
                    }
                    finally { CFRelease(dictionary); }
                }
                finally { IOObjectRelease(service); }
            }
            ids.Sort();
            return ids.Count > 0 ? (string.Join(',', ids), read, written) : ("", null, null);
        }
        catch (OverflowException) { return ("", null, null); }
        finally
        {
            if (statisticsKey != IntPtr.Zero) CFRelease(statisticsKey);
            if (readKey != IntPtr.Zero) CFRelease(readKey);
            if (writeKey != IntPtr.Zero) CFRelease(writeKey);
            IOObjectRelease(iterator);
        }
    }

    private static ulong? Number(IntPtr dictionary, IntPtr key)
    {
        if (key == IntPtr.Zero) return null;
        var value = CFDictionaryGetValue(dictionary, key);
        return value != IntPtr.Zero && CFGetTypeID(value) == CFNumberGetTypeID() &&
               CFNumberGetValue(value, 4, out var number) && number >= 0 ? (ulong)number : null;
    }

    [LibraryImport(IOKit, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr IOServiceMatching(string name);
    [LibraryImport(IOKit)]
    private static partial int IOServiceGetMatchingServices(uint mainPort, IntPtr matching, out uint iterator);
    [LibraryImport(IOKit)]
    private static partial uint IOIteratorNext(uint iterator);
    [LibraryImport(IOKit)]
    private static partial int IOObjectRelease(uint obj);
    [LibraryImport(IOKit)]
    private static partial int IORegistryEntryGetRegistryEntryID(uint entry, out ulong id);
    [LibraryImport(IOKit)]
    private static partial IntPtr IORegistryEntryCreateCFProperty(uint entry, IntPtr key, IntPtr allocator, uint options);
    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);
    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);
    [LibraryImport(CoreFoundation)]
    private static partial nuint CFGetTypeID(IntPtr value);
    [LibraryImport(CoreFoundation)]
    private static partial nuint CFDictionaryGetTypeID();
    [LibraryImport(CoreFoundation)]
    private static partial nuint CFNumberGetTypeID();
    [LibraryImport(CoreFoundation)]
    private static partial IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
    [LibraryImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool CFNumberGetValue(IntPtr number, int type, out long value);
}

using System.Runtime.InteropServices;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace MacMon;

/// <summary>Read-only AppKit bundle icons. The bounded cache owns its small bitmaps for the app lifetime.</summary>
public static class ApplicationIcons
{
    private const string ObjC = "/usr/lib/libobjc.A.dylib";
    private static readonly Dictionary<string, IImage> Cache = new(StringComparer.Ordinal);
    private static readonly IImage Generic = new DrawingImage(new GeometryDrawing
    {
        Brush = new SolidColorBrush(Color.Parse("#8BA0B4")),
        Geometry = Geometry.Parse("M3,1 L10,1 L14,5 L14,15 L3,15 Z M9,2 L9,6 L13,6 L9,2")
    });
    private static bool _loaded;
    // Called lazily by visible row bindings on the Avalonia UI thread, never by the collector.
    public static IImage For(string path)
    {
        var bundle = TablePresentation.ApplicationBundle(path);
        if (bundle is null) return Generic;
        lock (Cache)
        {
            if (Cache.TryGetValue(bundle, out var cached)) return cached;
            if (Cache.Count >= 128) return Generic;
            var icon = Load(bundle) ?? Generic;
            Cache.Add(bundle, icon);
            return icon;
        }
    }
    private static IImage? Load(string bundle)
    {
        if (!OperatingSystem.IsMacOS() || !Directory.Exists(bundle)) return null;
        nint pool = 0;
        try
        {
            if (!_loaded) { NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit"); _loaded = true; }
            pool = Send(Class("NSAutoreleasePool"), Selector("new"));
            var workspace = Send(Class("NSWorkspace"), Selector("sharedWorkspace"));
            var file = StringMessage(Class("NSString"), Selector("stringWithUTF8String:"), bundle);
            var image = SendArgument(workspace, Selector("iconForFile:"), file);
            if (image == 0) return null;
            var tiff = Send(image, Selector("TIFFRepresentation"));
            if (tiff == 0) return null;
            var rep = SendArgument(Class("NSBitmapImageRep"), Selector("imageRepWithData:"), tiff);
            if (rep == 0) return null;
            var properties = Send(Class("NSDictionary"), Selector("dictionary"));
            var png = Encode(rep, Selector("representationUsingType:properties:"), 4, properties);
            if (png == 0) return null;
            var length = (long)Send(png, Selector("length"));
            var bytes = Send(png, Selector("bytes"));
            if (length <= 0 || length > 16 * 1024 * 1024 || bytes == 0) return null;
            var data = new byte[(int)length];
            Marshal.Copy(bytes, data, 0, data.Length);
            using var stream = new MemoryStream(data, false);
            return Bitmap.DecodeToWidth(stream, 32);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or ExternalException or DllNotFoundException)
        { return null; }
        finally { if (pool != 0) Send(pool, Selector("drain")); }
    }
    [DllImport(ObjC, EntryPoint = "objc_getClass")] private static extern nint Class([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "sel_registerName")] private static extern nint Selector([MarshalAs(UnmanagedType.LPUTF8Str)] string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint SendArgument(nint receiver, nint selector, nint argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint StringMessage(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string argument);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern nint Encode(nint receiver, nint selector, nuint type, nint properties);
}

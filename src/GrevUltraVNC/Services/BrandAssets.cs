using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrevUltraVNC.Services;

public static class BrandAssets
{
    private static readonly Lazy<byte[]?> IconBytesLazy = new(LoadIconBytes);
    private static readonly Lazy<ImageSource?> LogoLazy = new(CreateLogo);

    public static ImageSource? Logo => LogoLazy.Value;

    public static Icon? CreateDrawingIcon()
    {
        var bytes = IconBytesLazy.Value;
        if (bytes is null || bytes.Length == 0) return null;

        using var stream = new MemoryStream(bytes, writable: false);
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    private static ImageSource? CreateLogo()
    {
        var bytes = IconBytesLazy.Value;
        if (bytes is null || bytes.Length == 0) return null;

        using var stream = new MemoryStream(bytes, writable: false);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(x => x.PixelWidth * x.PixelHeight).FirstOrDefault();
        frame?.Freeze();
        return frame;
    }

    private static byte[]? LoadIconBytes()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "GrevUltraVNC.ico.b64"),
            Path.Combine(AppContext.BaseDirectory, "GrevUltraVNC.ico.b64"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "GrevUltraVNC.ico.b64")
        };

        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return null;

        try
        {
            return Convert.FromBase64String(File.ReadAllText(path).Trim());
        }
        catch
        {
            return null;
        }
    }
}

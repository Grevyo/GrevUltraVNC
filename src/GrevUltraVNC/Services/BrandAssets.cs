using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrevUltraVNC.Services;

public static class BrandAssets
{
    private static readonly Lazy<byte[]?> LogoBytesLazy = new(LoadLogoBytes);
    private static readonly Lazy<ImageSource?> LogoLazy = new(CreateLogo);

    public static ImageSource? Logo => LogoLazy.Value;

    public static Icon? CreateDrawingIcon()
    {
        try
        {
            var bytes = LogoBytesLazy.Value;
            if (bytes is null || bytes.Length == 0) return null;

            using var stream = new MemoryStream(bytes, writable: false);
            using var source = new Bitmap(stream);
            using var bitmap = new Bitmap(source, new Size(128, 128));
            var handle = bitmap.GetHicon();

            try
            {
                using var temporary = Icon.FromHandle(handle);
                return (Icon)temporary.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? CreateLogo()
    {
        try
        {
            var bytes = LogoBytesLazy.Value;
            if (bytes is null || bytes.Length == 0) return null;

            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch
        {
            return null;
        }
    }

    private static byte[]? LoadLogoBytes()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Assets", "GrevLogo.jpg.b64"),
            Path.Combine(AppContext.BaseDirectory, "GrevLogo.jpg.b64"),
            Path.Combine(Environment.CurrentDirectory, "Assets", "GrevLogo.jpg.b64"),
            Path.Combine(Environment.CurrentDirectory, "src", "GrevUltraVNC", "Assets", "GrevLogo.jpg.b64")
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

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

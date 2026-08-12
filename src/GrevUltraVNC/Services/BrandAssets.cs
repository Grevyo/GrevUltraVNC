using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GrevUltraVNC.Services;

public static class BrandAssets
{
    private static readonly Lazy<byte[]?> LogoBytesLazy = new(LoadFirstValidLogoBytes);
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
            using var bitmap = new Bitmap(256, 256, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(System.Drawing.Color.Transparent);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height));
            }

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
        var bytes = LogoBytesLazy.Value;
        if (bytes is null || bytes.Length == 0) return null;

        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
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

    private static byte[]? LoadFirstValidLogoBytes()
    {
        foreach (var path in GetLogoCandidates())
        {
            if (!File.Exists(path)) continue;

            try
            {
                var bytes = Convert.FromBase64String(File.ReadAllText(path).Trim());
                if (bytes.Length == 0) continue;

                // Validate the decoded image before accepting it. This means a bad/new
                // branding asset can never blank every logo in the application; the
                // loader simply falls through to the known-good legacy asset.
                using var stream = new MemoryStream(bytes, writable: false);
                using var image = System.Drawing.Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
                if (image.Width <= 0 || image.Height <= 0) continue;

                return bytes;
            }
            catch
            {
                // Try the next candidate.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetLogoCandidates()
    {
        // Prefer the higher-resolution asset, but always keep the original supplied
        // logo as a fallback. Both are copied beside the built application.
        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "GrevLogo256.jpg.b64");
        yield return Path.Combine(AppContext.BaseDirectory, "GrevLogo256.jpg.b64");
        yield return Path.Combine(Environment.CurrentDirectory, "Assets", "GrevLogo256.jpg.b64");
        yield return Path.Combine(Environment.CurrentDirectory, "src", "GrevUltraVNC", "Assets", "GrevLogo256.jpg.b64");

        yield return Path.Combine(AppContext.BaseDirectory, "Assets", "GrevLogo.jpg.b64");
        yield return Path.Combine(AppContext.BaseDirectory, "GrevLogo.jpg.b64");
        yield return Path.Combine(Environment.CurrentDirectory, "Assets", "GrevLogo.jpg.b64");
        yield return Path.Combine(Environment.CurrentDirectory, "src", "GrevUltraVNC", "Assets", "GrevLogo.jpg.b64");
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ECCR.Services;

/// <summary>
/// Extracts an .exe's associated shell icon as a PNG, for games added without a user-supplied
/// banner image. Prefers Windows' 256x256 "jumbo" shell icon (via <c>SHGetImageList</c>/
/// <c>IImageList</c>, the same source Explorer's large-icon views use) over
/// <see cref="Icon.ExtractAssociatedIcon"/>'s ~32x32 result - the small icon was the actual cause
/// of visibly blurry game banners, since <see cref="TryExtractIconAsBanner"/> stretches it several
/// times larger to fill the card. Falls back through progressively smaller shell icon sizes, and
/// finally to <see cref="Icon.ExtractAssociatedIcon"/>, for the rare exe that has no jumbo icon
/// registered. Uses <see cref="System.Drawing.Common"/> rather than a fully custom decoder - the
/// app already targets net-windows exclusively, so the Windows-only package carries no
/// cross-platform cost.
/// </summary>
public static class GameIconExtractor
{
    public static bool TryExtractIcon(string exePath, string destinationPngPath)
    {
        try
        {
            using var bitmap = GetBestAvailableIconBitmap(exePath);
            if (bitmap == null) return false;

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPngPath)!);
            bitmap.Save(destinationPngPath, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Last-resort banner for a game with no locally-cached box art: extracts the exe's best
    /// available icon, then composites it onto a portrait canvas - a softened, darkened, stretched
    /// copy of the icon as the background fill, with the crisp original centered on top - so a
    /// square icon doesn't sit awkwardly letterboxed in the Games page's portrait card slot.
    /// </summary>
    public static bool TryExtractIconAsBanner(string exePath, string destinationPngPath, int width = 600, int height = 900)
    {
        try
        {
            using var iconBitmap = GetBestAvailableIconBitmap(exePath);
            if (iconBitmap == null) return false;

            using var canvas = new Bitmap(width, height);
            using (var g = Graphics.FromImage(canvas))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;

                // Blurred/darkened background: shrinking then stretching the icon back up softens
                // it into a cheap approximation of a gaussian blur, no dedicated blur kernel needed.
                int smallWidth = Math.Max(1, width / 12);
                int smallHeight = Math.Max(1, height / 12);
                using (var tiny = new Bitmap(iconBitmap, smallWidth, smallHeight))
                using (var softened = new Bitmap(tiny, width, height))
                {
                    using var darken = new ImageAttributes();
                    var matrix = new ColorMatrix { Matrix00 = 0.55f, Matrix11 = 0.55f, Matrix22 = 0.55f, Matrix33 = 1f, Matrix44 = 1f };
                    darken.SetColorMatrix(matrix);
                    g.DrawImage(softened, new Rectangle(0, 0, width, height), 0, 0, width, height, GraphicsUnit.Pixel, darken);
                }

                // Foreground: the crisp original icon, centered, at a fixed fraction of the canvas.
                // Capped at the icon's own native size so a small source icon is never stretched
                // beyond its real resolution (that stretch was the original blur complaint).
                int fgSize = Math.Min((int)(width * 0.4), Math.Max(iconBitmap.Width, iconBitmap.Height));
                g.DrawImage(iconBitmap, new Rectangle((width - fgSize) / 2, (height - fgSize) / 2, fgSize, fgSize));
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPngPath)!);
            canvas.Save(destinationPngPath, ImageFormat.Png);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Highest-resolution icon Windows has for this exe: tries the Shell's jumbo (256x256) then extra-large (48x48) image lists, falling back to <see cref="Icon.ExtractAssociatedIcon"/>'s small icon if neither Shell API is available.</summary>
    private static Bitmap? GetBestAvailableIconBitmap(string exePath)
    {
        return TryGetShellIconBitmap(exePath, ShilJumbo)
            ?? TryGetShellIconBitmap(exePath, ShilExtraLarge)
            ?? TryGetFallbackIconBitmap(exePath);
    }

    private static Bitmap? TryGetFallbackIconBitmap(string exePath)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(exePath);
            return icon?.ToBitmap();
        }
        catch
        {
            return null;
        }
    }

    // --- Shell jumbo/extra-large icon retrieval (SHGetFileInfo + SHGetImageList/IImageList) ---
    // Standard, widely-used technique for icons larger than Icon.ExtractAssociatedIcon's ~32x32
    // result: look up this exe's index into the shell's system image list, then pull the icon at
    // that index out of the specific-sized image list (jumbo = 256x256, extra-large = 48x48).

    private const int ShilJumbo = 0x4;
    private const int ShilExtraLarge = 0x2;
    private const uint ShgfiSysIconIndex = 0x4000;
    private const int IldTransparent = 1;
    private static readonly Guid IidImageList = new("46eb5926-582e-4017-9fdf-e8998daa0950");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ShFileInfo
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [ComImport]
    [Guid("46eb5926-582e-4017-9fdf-e8998daa0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IImageList
    {
        [PreserveSig] int Add(IntPtr hbmImage, IntPtr hbmMask, ref int pi);
        [PreserveSig] int ReplaceIcon(int i, IntPtr hicon, ref int pi);
        [PreserveSig] int SetOverlayImage(int iImage, int iOverlay);
        [PreserveSig] int Replace(int i, IntPtr hbmImage, IntPtr hbmMask);
        [PreserveSig] int AddMasked(IntPtr hbmImage, int crMask, ref int pi);
        [PreserveSig] int Draw(IntPtr pimldp);
        [PreserveSig] int Remove(int i);
        [PreserveSig] int GetIcon(int i, int flags, ref IntPtr picon);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref ShFileInfo psfi, uint cbFileInfo, uint uFlags);

    [DllImport("shell32.dll", EntryPoint = "#727")]
    private static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private static Bitmap? TryGetShellIconBitmap(string exePath, int shilSize)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var info = new ShFileInfo();
            uint size = (uint)Marshal.SizeOf<ShFileInfo>();
            if (SHGetFileInfo(exePath, 0, ref info, size, ShgfiSysIconIndex) == IntPtr.Zero) return null;

            var iid = IidImageList;
            if (SHGetImageList(shilSize, ref iid, out var imageList) != 0 || imageList == null) return null;

            if (imageList.GetIcon(info.iIcon, IldTransparent, ref hIcon) != 0 || hIcon == IntPtr.Zero) return null;

            using var icon = Icon.FromHandle(hIcon);
            return (Bitmap)icon.ToBitmap().Clone();
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hIcon != IntPtr.Zero) DestroyIcon(hIcon);
        }
    }
}

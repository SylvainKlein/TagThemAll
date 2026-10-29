using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TagThemAll.Services;

/// <summary>
/// Extracts thumbnails via the Windows Shell COM API.
/// All COM calls run on a dedicated long-lived STA thread, which is required
/// by IShellItemImageFactory. Results are cached in-memory.
/// </summary>
public sealed class ShellThumbnailService
{
    private static readonly Lazy<ShellThumbnailService> _instance
        = new(() => new ShellThumbnailService());
    public static ShellThumbnailService Instance => _instance.Value;

    // Cache: key = "path|size"
    private readonly ConcurrentDictionary<string, BitmapSource> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    // Work queue for the STA thread
    private readonly BlockingCollection<Action> _staQueue = new();
    private readonly Thread _staThread;

    private ShellThumbnailService()
    {
        // Dedicated STA thread for all Shell COM calls
        _staThread = new Thread(StaThreadLoop)
        {
            Name = "ThumbnailSTAThread",
            IsBackground = true
        };
        _staThread.SetApartmentState(ApartmentState.STA);
        _staThread.Start();
    }

    private void StaThreadLoop()
    {
        foreach (var action in _staQueue.GetConsumingEnumerable())
        {
            try { action(); }
            catch { /* individual errors handled per-request */ }
        }
    }

    /// <summary>Enqueue work on the STA thread and await the result.</summary>
    private Task<T?> OnStaThread<T>(Func<T?> work) where T : class
    {
        var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _staQueue.Add(() =>
        {
            try   { tcs.SetResult(work()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    public async Task<BitmapSource?> GetThumbnailAsync(
        string filePath,
        int targetSize = 256,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;

        string key = $"{filePath}|{targetSize}";
        if (_cache.TryGetValue(key, out var hit)) return hit;
        if (!File.Exists(filePath)) return null;

        if (ct.IsCancellationRequested) return null;

        // 1. Try Windows Shell thumbnail (STA-safe)
        BitmapSource? bmp = await OnStaThread(() => ExtractShellThumbnail(filePath, targetSize));

        // 2. Direct WPF decoder fallback for standard images
        if (bmp == null && IsDirectDecodable(filePath))
        {
            bmp = await Task.Run(() => ExtractDirectImage(filePath, targetSize), ct);
        }

        // 3. Shell icon fallback (still STA)
        if (bmp == null)
        {
            bmp = await OnStaThread(() => ExtractShellIcon(filePath));
        }

        if (bmp != null)
        {
            _cache.TryAdd(key, bmp);
        }

        return bmp;
    }

    // ─── COM Extraction (runs on STA thread) ──────────────────────────────

    private static BitmapSource? ExtractShellThumbnail(string filePath, int size)
    {
        IntPtr hbm = IntPtr.Zero;
        try
        {
            Guid iid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");
            int hr = NativeMethods.SHCreateItemFromParsingName(filePath, IntPtr.Zero, ref iid, out var factory);
            if (hr != 0 || factory == null) return null;

            var s = new NativeMethods.SIZE { cx = size, cy = size };

            // Try BIGGERSIZEOK first, then RESIZETOFIT, then default
            hr = factory.GetImage(s, NativeMethods.SIIGBF.SIIGBF_BIGGERSIZEOK, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
                hr = factory.GetImage(s, NativeMethods.SIIGBF.SIIGBF_RESIZETOFIT, out hbm);
            if (hr != 0 || hbm == IntPtr.Zero)
                hr = factory.GetImage(s, 0, out hbm);

            if (hr != 0 || hbm == IntPtr.Zero) return null;

            var src = Imaging.CreateBitmapSourceFromHBitmap(
                hbm, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (hbm != IntPtr.Zero) NativeMethods.DeleteObject(hbm);
        }
    }

    private static BitmapSource? ExtractShellIcon(string filePath)
    {
        IntPtr hIcon = IntPtr.Zero;
        try
        {
            var shinfo = new NativeMethods.SHFILEINFO();
            const uint SHGFI_ICON = 0x100;
            const uint SHGFI_LARGEICON = 0x0;

            IntPtr res = NativeMethods.SHGetFileInfo(filePath, 0, ref shinfo,
                (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

            if (res == IntPtr.Zero || shinfo.hIcon == IntPtr.Zero) return null;
            hIcon = shinfo.hIcon;

            var src = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        catch { return null; }
        finally
        {
            if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
        }
    }

    // ─── Direct WPF decoder (MTA-safe) ────────────────────────────────────

    private static readonly string[] _directExts =
        [".jpg", ".jpeg", ".png", ".bmp", ".gif", ".tif", ".tiff", ".webp", ".ico"];

    private static bool IsDirectDecodable(string path) =>
        Array.IndexOf(_directExts, Path.GetExtension(path).ToLowerInvariant()) >= 0;

    private static BitmapSource? ExtractDirectImage(string filePath, int size)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.OnLoad);

            if (decoder.Frames.Count == 0) return null;
            var frame = decoder.Frames[0];

            int maxDim = Math.Max(frame.PixelWidth, frame.PixelHeight);
            if (maxDim <= size)
            {
                var clone = frame.Clone();
                clone.Freeze();
                return clone;
            }

            double scale = (double)size / maxDim;
            var scaled = new TransformedBitmap(frame,
                new ScaleTransform(scale, scale));
            scaled.Freeze();
            return scaled;
        }
        catch { return null; }
    }

    // ─── Cache invalidation ───────────────────────────────────────────────

    public void InvalidateCache(string filePath)
    {
        foreach (var key in _cache.Keys)
            if (key.StartsWith(filePath, StringComparison.OrdinalIgnoreCase))
                _cache.TryRemove(key, out _);
    }
}

// ─── Win32 Interop ────────────────────────────────────────────────────────

internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx; public int cy; }

    [Flags]
    public enum SIIGBF
    {
        SIIGBF_RESIZETOFIT = 0x00,
        SIIGBF_BIGGERSIZEOK = 0x01,
        SIIGBF_MEMORYONLY = 0x02,
        SIIGBF_ICONONLY = 0x04,
        SIIGBF_THUMBNAILONLY = 0x08,
        SIIGBF_INCACHEONLY = 0x10
    }

    [ComImport]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, SIIGBF flags, out IntPtr phbm);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    public static extern int SHCreateItemFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory ppv);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]  public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath, uint dwFileAttributes,
        ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);
}

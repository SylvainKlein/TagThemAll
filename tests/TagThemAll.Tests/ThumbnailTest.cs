using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TagThemAll.Tests;

public class ThumbnailTest
{
    [STAThread]
    public static void Main()
    {
        string sourcePng = @"W:\WORKS.Perso\TagThemAll\TagThemAll-icon-01.png";
        string outIco = @"W:\WORKS.Perso\TagThemAll\src\TagThemAll\Resources\app.ico";
        string targetPng = @"W:\WORKS.Perso\TagThemAll\src\TagThemAll\Resources\app-icon.png";

        Console.WriteLine($"Loading source image {sourcePng}...");
        File.Copy(sourcePng, targetPng, true);
        Console.WriteLine($"Copied to {targetPng}");

        GenerateIcoFromPng(sourcePng, outIco);

        var fi = new FileInfo(outIco);
        Console.WriteLine($"Generated app.ico successfully: {fi.Length} bytes at {fi.FullName}");
    }

    private static void GenerateIcoFromPng(string inputPngPath, string outputPath)
    {
        var srcBmp = new BitmapImage();
        using (var stream = new FileStream(inputPngPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
        {
            srcBmp.BeginInit();
            srcBmp.CacheOption = BitmapCacheOption.OnLoad;
            srcBmp.StreamSource = stream;
            srcBmp.EndInit();
        }
        srcBmp.Freeze();

        Console.WriteLine($"Source PNG resolution: {srcBmp.PixelWidth}x{srcBmp.PixelHeight}");

        int[] sizes = new[] { 256, 128, 64, 48, 32, 24, 16 };
        var pngFrames = new List<byte[]>();

        foreach (int size in sizes)
        {
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
                RenderOptions.SetEdgeMode(visual, EdgeMode.Aliased);
                dc.DrawImage(srcBmp, new Rect(0, 0, size, size));
            }

            var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var ms = new MemoryStream();
            encoder.Save(ms);
            pngFrames.Add(ms.ToArray());
        }

        // Write .ico file format
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        // Header: 6 bytes
        bw.Write((ushort)0); // reserved
        bw.Write((ushort)1); // type 1 = ICO
        bw.Write((ushort)sizes.Length); // count

        // Calculate offset to first image
        int offset = 6 + (sizes.Length * 16);

        // Directory entries: 16 bytes each
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            byte[] png = pngFrames[i];

            bw.Write((byte)(s >= 256 ? 0 : s)); // Width
            bw.Write((byte)(s >= 256 ? 0 : s)); // Height
            bw.Write((byte)0); // Color count
            bw.Write((byte)0); // Reserved
            bw.Write((ushort)1); // Planes
            bw.Write((ushort)32); // Bits per pixel
            bw.Write((uint)png.Length); // Image size
            bw.Write((uint)offset); // Offset

            offset += png.Length;
        }

        // Image data
        for (int i = 0; i < sizes.Length; i++)
        {
            bw.Write(pngFrames[i]);
        }
    }
}

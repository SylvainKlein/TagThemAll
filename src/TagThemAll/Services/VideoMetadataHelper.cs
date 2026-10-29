using System;
using System.IO;
using System.Runtime.InteropServices;

namespace TagThemAll.Services;

public static class VideoMetadataHelper
{
    /// <summary>
    /// Returns the video rotation in degrees (0, 90, 180, 270).
    /// </summary>
    public static int GetVideoRotation(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return 0;

        try
        {
            // 1. Try direct MP4 / MOV container parsing (fastest & handles iPhone / Android)
            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (ext is ".mp4" or ".mov" or ".m4v" or ".3gp")
            {
                int rot = GetMp4Rotation(filePath);
                if (rot != 0) return rot;
            }

            // 2. Fallback to Windows Shell Property System (System.Video.Orientation)
            int shellRot = GetShellVideoOrientation(filePath);
            if (shellRot != 0) return shellRot;
        }
        catch
        {
            // Ignore any read errors
        }

        return 0;
    }

    private static int GetMp4Rotation(string filePath)
    {
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new BinaryReader(stream);

        // Find 'moov' atom
        if (!FindAtom(stream, reader, "moov", stream.Length, out long moovEnd))
            return 0;

        // Inside 'moov', iterate through child atoms
        while (stream.Position < moovEnd)
        {
            long atomStart = stream.Position;
            if (!ReadAtomHeader(stream, reader, moovEnd, out string type, out long atomEnd))
                break;

            if (string.Equals(type, "trak", StringComparison.OrdinalIgnoreCase))
            {
                // Inside 'trak', look for 'tkhd'
                long trakEnd = atomEnd;
                while (stream.Position < trakEnd)
                {
                    long subStart = stream.Position;
                    if (!ReadAtomHeader(stream, reader, trakEnd, out string subType, out long subEnd))
                        break;

                    if (string.Equals(subType, "tkhd", StringComparison.OrdinalIgnoreCase))
                    {
                        int rot = ParseTkhdMatrix(reader);
                        if (rot != 0) return rot;
                    }

                    stream.Seek(subEnd, SeekOrigin.Begin);
                }
            }

            stream.Seek(atomEnd, SeekOrigin.Begin);
        }

        return 0;
    }

    private static bool FindAtom(Stream stream, BinaryReader reader, string targetType, long searchLimit, out long atomEnd)
    {
        atomEnd = 0;
        while (stream.Position + 8 <= searchLimit)
        {
            if (!ReadAtomHeader(stream, reader, searchLimit, out string type, out atomEnd))
                return false;

            if (string.Equals(type, targetType, StringComparison.OrdinalIgnoreCase))
                return true;

            stream.Seek(atomEnd, SeekOrigin.Begin);
        }
        return false;
    }

    private static bool ReadAtomHeader(Stream stream, BinaryReader reader, long searchLimit, out string type, out long atomEnd)
    {
        type = string.Empty;
        atomEnd = 0;
        if (stream.Position + 8 > searchLimit) return false;

        long boxStart = stream.Position;
        uint size = ReadBigEndianUInt32(reader);
        byte[] typeBytes = reader.ReadBytes(4);
        if (typeBytes.Length < 4) return false;
        type = System.Text.Encoding.ASCII.GetString(typeBytes);

        long boxLength = size;
        if (size == 1) // 64-bit size
        {
            if (stream.Position + 8 > searchLimit) return false;
            boxLength = (long)ReadBigEndianUInt64(reader);
        }
        else if (size == 0) // Extends to search limit
        {
            boxLength = searchLimit - boxStart;
        }

        if (boxLength < 8) return false;
        atomEnd = boxStart + boxLength;
        return atomEnd <= searchLimit && atomEnd > boxStart;
    }

    private static int ParseTkhdMatrix(BinaryReader reader)
    {
        byte version = reader.ReadByte();
        reader.ReadBytes(3); // flags

        if (version == 0)
        {
            reader.ReadBytes(4 + 4 + 4 + 4 + 4); // creation, mod, track_id, reserved, duration
        }
        else // version == 1
        {
            reader.ReadBytes(8 + 8 + 4 + 4 + 8);
        }

        // reserved (8) + layer (2) + alt_group (2) + volume (2) + reserved (2) = 16 bytes
        reader.ReadBytes(16);

        // Matrix (9 integers in 16.16 fixed-point)
        int a = ReadBigEndianInt32(reader); // [0]
        int b = ReadBigEndianInt32(reader); // [1]
        reader.ReadBytes(4);                // [2] u
        int c = ReadBigEndianInt32(reader); // [3]
        int d = ReadBigEndianInt32(reader); // [4]
        reader.ReadBytes(4 * 4);            // [5] v, [6] x, [7] y, [8] w

        // Fixed-point values:
        // 0x00010000 = 1.0, 0 = 0.0, 0xFFFF0000 (-65536) = -1.0
        // Matrix rotation mappings:
        // 0°   : a =  1, b =  0, c =  0, d =  1
        // 90°  : a =  0, b =  1, c = -1, d =  0
        // 180° : a = -1, b =  0, c =  0, d = -1
        // 270° : a =  0, b = -1, c =  1, d =  0

        if (b > 0 && c < 0) return 90;
        if (a < 0 && d < 0) return 180;
        if (b < 0 && c > 0) return 270;

        return 0;
    }

    private static uint ReadBigEndianUInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) return 0;
        return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
    }

    private static int ReadBigEndianInt32(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length < 4) return 0;
        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private static ulong ReadBigEndianUInt64(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(8);
        if (bytes.Length < 8) return 0;
        return ((ulong)bytes[0] << 56) | ((ulong)bytes[1] << 48) | ((ulong)bytes[2] << 40) | ((ulong)bytes[3] << 32)
             | ((ulong)bytes[4] << 24) | ((ulong)bytes[5] << 16) | ((ulong)bytes[6] << 8) | bytes[7];
    }

    // ── Windows Property Store Fallback ───────────────────────────────────

    private static readonly Guid PKEY_Video_Orientation_FMTID = new("64440492-4c8b-11d1-8b70-080036b11a03");
    private const uint PID_Video_Orientation = 40;

    private static int GetShellVideoOrientation(string filePath)
    {
        try
        {
            Guid propertyStoreGuid = new("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99");
            int hr = SHGetPropertyStoreFromParsingName(filePath, IntPtr.Zero, 0, ref propertyStoreGuid, out var store);
            if (hr != 0 || store == null) return 0;

            var key = new PROPERTYKEY { fmtid = PKEY_Video_Orientation_FMTID, pid = PID_Video_Orientation };
            store.GetValue(ref key, out var pv);

            if (pv.vt == 19) // VT_UI4
            {
                return (int)pv.uintVal;
            }
            if (pv.vt == 23) // VT_INT / VT_I4
            {
                return pv.intVal;
            }
        }
        catch { }

        return 0;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint uintVal;
        [FieldOffset(8)] public int intVal;
    }

    [ComImport]
    [Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHGetPropertyStoreFromParsingName(
        [In, MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        [In] IntPtr pbc,
        [In] uint flags,
        [In] ref Guid riid,
        [Out, MarshalAs(UnmanagedType.Interface)] out IPropertyStore ppv);
}

using System.Buffers.Binary;
using System.Text;

namespace TaskNova.Core.Shell;

/// <summary>
/// Reads target and arguments of a Windows shortcut (.lnk, MS-SHLLINK) without COM – fast and Native-AOT friendly.
/// Order: LinkInfo local path → environment variable block → relative path.
/// </summary>
public static class LnkParser
{
    private const int HeaderSize = 0x4C;
    private const uint HasLinkTargetIdList = 0x01;
    private const uint HasLinkInfo = 0x02;
    private const uint HasName = 0x04;
    private const uint HasRelativePath = 0x08;
    private const uint HasWorkingDir = 0x10;
    private const uint HasArguments = 0x20;
    private const uint HasIconLocation = 0x40;
    private const uint IsUnicode = 0x80;
    private const uint EnvironmentBlockSignature = 0xA0000001;

    public sealed record LnkTarget(string? Path, string? Arguments, string? WorkingDirectory);

    public static LnkTarget? TryRead(string file)
    {
        try
        {
            var data = File.ReadAllBytes(file);
            var result = Parse(data);
            if (result is { Path: { } path } && !System.IO.Path.IsPathRooted(path))
            {
                var folder = System.IO.Path.GetDirectoryName(file) ?? "";
                result = result with { Path = System.IO.Path.GetFullPath(System.IO.Path.Combine(folder, path)) };
            }

            return result;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    public static LnkTarget? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderSize || BinaryPrimitives.ReadUInt32LittleEndian(data) != HeaderSize)
        {
            return null;
        }

        var flags = BinaryPrimitives.ReadUInt32LittleEndian(data[0x14..]);
        var pos = HeaderSize;

        if ((flags & HasLinkTargetIdList) != 0)
        {
            if (pos + 2 > data.Length)
            {
                return null;
            }

            pos += 2 + BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        }

        string? localPath = null;
        if ((flags & HasLinkInfo) != 0)
        {
            if (pos + 4 > data.Length)
            {
                return null;
            }

            var infoSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
            if (infoSize < 0x1C || pos + infoSize > data.Length)
            {
                return null;
            }

            localPath = ReadLinkInfo(data.Slice(pos, infoSize));
            pos += infoSize;
        }

        var unicode = (flags & IsUnicode) != 0;
        string? relative = null;
        string? workingDir = null;
        string? arguments = null;
        if ((flags & HasName) != 0)
        {
            ReadStringData(data, ref pos, unicode);
        }

        if ((flags & HasRelativePath) != 0)
        {
            relative = ReadStringData(data, ref pos, unicode);
        }

        if ((flags & HasWorkingDir) != 0)
        {
            workingDir = ReadStringData(data, ref pos, unicode);
        }

        if ((flags & HasArguments) != 0)
        {
            arguments = ReadStringData(data, ref pos, unicode);
        }

        if ((flags & HasIconLocation) != 0)
        {
            ReadStringData(data, ref pos, unicode);
        }

        string? environmentTarget = null;
        while (pos >= 0 && pos + 8 <= data.Length)
        {
            var blockSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(data[pos..]);
            if (blockSize < 8 || pos + blockSize > data.Length)
            {
                break;
            }

            var signature = BinaryPrimitives.ReadUInt32LittleEndian(data[(pos + 4)..]);
            if (signature == EnvironmentBlockSignature && blockSize >= 8 + 260 + 520)
            {
                var unicodeTarget = ReadZeroTerminatedUtf16(data.Slice(pos + 8 + 260, 520));
                environmentTarget = string.IsNullOrEmpty(unicodeTarget) ? ReadZeroTerminatedAnsi(data.Slice(pos + 8, 260)) : unicodeTarget;
            }

            pos += blockSize;
        }

        var path = localPath ?? (environmentTarget is null ? null : Environment.ExpandEnvironmentVariables(environmentTarget)) ?? relative;
        if (path is null && arguments is null)
        {
            return null;
        }

        return new LnkTarget(string.IsNullOrWhiteSpace(path) ? null : path, string.IsNullOrWhiteSpace(arguments) ? null : arguments,
            string.IsNullOrWhiteSpace(workingDir) ? null : workingDir);
    }

    private static string? ReadLinkInfo(ReadOnlySpan<byte> info)
    {
        var headerSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[4..]);
        var infoFlags = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        if ((infoFlags & 0x1) == 0)
        {
            return null;
        }

        var baseOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x10..]);
        var suffixOffset = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x18..]);
        if (headerSize >= 0x24 && info.Length >= 0x24)
        {
            var baseUnicode = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x1C..]);
            var suffixUnicode = (int)BinaryPrimitives.ReadUInt32LittleEndian(info[0x20..]);
            if (baseUnicode > 0 && baseUnicode < info.Length)
            {
                var basePath = ReadZeroTerminatedUtf16(info[baseUnicode..]);
                var suffix = suffixUnicode > 0 && suffixUnicode < info.Length ? ReadZeroTerminatedUtf16(info[suffixUnicode..]) : "";
                return Combine(basePath, suffix);
            }
        }

        if (baseOffset <= 0 || baseOffset >= info.Length)
        {
            return null;
        }

        var ansiBase = ReadZeroTerminatedAnsi(info[baseOffset..]);
        var ansiSuffix = suffixOffset > 0 && suffixOffset < info.Length ? ReadZeroTerminatedAnsi(info[suffixOffset..]) : "";
        return Combine(ansiBase, ansiSuffix);
    }

    private static string? Combine(string basePath, string suffix)
    {
        if (string.IsNullOrEmpty(basePath))
        {
            return null;
        }

        if (string.IsNullOrEmpty(suffix))
        {
            return basePath;
        }

        return basePath.EndsWith('\\') ? basePath + suffix : basePath + "\\" + suffix;
    }

    private static string? ReadStringData(ReadOnlySpan<byte> data, ref int pos, bool unicode)
    {
        if (pos + 2 > data.Length)
        {
            pos = data.Length;
            return null;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(data[pos..]);
        pos += 2;
        var bytes = unicode ? count * 2 : count;
        if (pos + bytes > data.Length)
        {
            pos = data.Length;
            return null;
        }

        var text = unicode ? Encoding.Unicode.GetString(data.Slice(pos, bytes)) : Encoding.Latin1.GetString(data.Slice(pos, bytes));
        pos += bytes;
        return text;
    }

    private static string ReadZeroTerminatedAnsi(ReadOnlySpan<byte> data)
    {
        var end = data.IndexOf((byte)0);
        return Encoding.Latin1.GetString(end < 0 ? data : data[..end]);
    }

    private static string ReadZeroTerminatedUtf16(ReadOnlySpan<byte> data)
    {
        var length = 0;
        while (length + 1 < data.Length && (data[length] != 0 || data[length + 1] != 0))
        {
            length += 2;
        }

        return Encoding.Unicode.GetString(data[..length]);
    }
}

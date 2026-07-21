using System.Buffers.Binary;
using System.Text;

namespace CustomLauncher.Core;

public enum NbtTagType : byte
{
    End = 0, Byte = 1, Short = 2, Int = 3, Long = 4, Float = 5, Double = 6,
    ByteArray = 7, String = 8, List = 9, Compound = 10, IntArray = 11, LongArray = 12,
}

/// <summary>An NBT list. The element type is part of the format and must survive a round trip.</summary>
public sealed class NbtList(NbtTagType elementType) : List<object>
{
    public NbtTagType ElementType { get; set; } = elementType;
}

/// <summary>
/// An NBT compound. Values are the CLR equivalents of the tag types: <see cref="byte"/>,
/// <see cref="short"/>, <see cref="int"/>, <see cref="long"/>, <see cref="float"/>,
/// <see cref="double"/>, <c>byte[]</c>, <see cref="string"/>, <see cref="NbtList"/>,
/// <see cref="NbtCompound"/>, <c>int[]</c> and <c>long[]</c>.
/// </summary>
public sealed class NbtCompound : Dictionary<string, object>
{
    public NbtCompound() : base(StringComparer.Ordinal) { }

    public string? GetString(string name) => TryGetValue(name, out var value) ? value as string : null;
}

/// <summary>
/// Just enough NBT to edit <c>servers.dat</c>, which is stored uncompressed and big-endian.
/// Every tag type is handled even though the file only uses a few: the launcher rewrites a file the
/// player owns, and dropping tags another launcher or mod wrote would quietly destroy their data.
/// </summary>
public static class Nbt
{
    public static NbtCompound Read(Stream stream)
    {
        var reader = new Reader(stream);
        var type = (NbtTagType)reader.ReadByte();
        if (type != NbtTagType.Compound)
            throw new InvalidDataException($"Expected a root compound tag but found {type}.");
        reader.ReadString(); // Root name, conventionally empty.
        return (NbtCompound)reader.ReadPayload(NbtTagType.Compound);
    }

    public static void Write(Stream stream, NbtCompound root, string rootName = "")
    {
        var writer = new Writer(stream);
        writer.WriteByte((byte)NbtTagType.Compound);
        writer.WriteString(rootName);
        writer.WritePayload(NbtTagType.Compound, root);
    }

    internal static NbtTagType TypeOf(object value) => value switch
    {
        byte => NbtTagType.Byte,
        short => NbtTagType.Short,
        int => NbtTagType.Int,
        long => NbtTagType.Long,
        float => NbtTagType.Float,
        double => NbtTagType.Double,
        byte[] => NbtTagType.ByteArray,
        string => NbtTagType.String,
        NbtList => NbtTagType.List,
        NbtCompound => NbtTagType.Compound,
        int[] => NbtTagType.IntArray,
        long[] => NbtTagType.LongArray,
        _ => throw new InvalidDataException($"{value.GetType()} is not an NBT value."),
    };

    private sealed class Reader(Stream stream)
    {
        public byte ReadByte()
        {
            var value = stream.ReadByte();
            if (value < 0) throw new EndOfStreamException();
            return (byte)value;
        }

        private ReadOnlySpan<byte> ReadBytes(int count)
        {
            var buffer = new byte[count];
            stream.ReadExactly(buffer);
            return buffer;
        }

        private short ReadShort() => BinaryPrimitives.ReadInt16BigEndian(ReadBytes(2));
        private int ReadInt() => BinaryPrimitives.ReadInt32BigEndian(ReadBytes(4));
        private long ReadLong() => BinaryPrimitives.ReadInt64BigEndian(ReadBytes(8));

        public string ReadString()
        {
            var length = (ushort)ReadShort();
            return length == 0 ? string.Empty : ModifiedUtf8.Decode(ReadBytes(length));
        }

        public object ReadPayload(NbtTagType type)
        {
            switch (type)
            {
                case NbtTagType.Byte: return ReadByte();
                case NbtTagType.Short: return ReadShort();
                case NbtTagType.Int: return ReadInt();
                case NbtTagType.Long: return ReadLong();
                case NbtTagType.Float:
                    return BitConverter.Int32BitsToSingle(ReadInt());
                case NbtTagType.Double:
                    return BitConverter.Int64BitsToDouble(ReadLong());
                case NbtTagType.String: return ReadString();
                case NbtTagType.ByteArray:
                    return ReadBytes(RequireLength(ReadInt())).ToArray();
                case NbtTagType.IntArray:
                    {
                        var count = RequireLength(ReadInt());
                        var values = new int[count];
                        for (var index = 0; index < count; index++) values[index] = ReadInt();
                        return values;
                    }
                case NbtTagType.LongArray:
                    {
                        var count = RequireLength(ReadInt());
                        var values = new long[count];
                        for (var index = 0; index < count; index++) values[index] = ReadLong();
                        return values;
                    }
                case NbtTagType.List:
                    {
                        var elementType = (NbtTagType)ReadByte();
                        var count = RequireLength(ReadInt());
                        var list = new NbtList(elementType);
                        // An empty list is written with element type End; there is nothing to read.
                        if (elementType == NbtTagType.End) return list;
                        for (var index = 0; index < count; index++) list.Add(ReadPayload(elementType));
                        return list;
                    }
                case NbtTagType.Compound:
                    {
                        var compound = new NbtCompound();
                        while (true)
                        {
                            var childType = (NbtTagType)ReadByte();
                            if (childType == NbtTagType.End) return compound;
                            var name = ReadString();
                            compound[name] = ReadPayload(childType);
                        }
                    }
                default:
                    throw new InvalidDataException($"Unknown NBT tag type {(byte)type}.");
            }
        }

        private static int RequireLength(int length) => length >= 0
            ? length
            : throw new InvalidDataException($"Negative NBT length {length}.");
    }

    private sealed class Writer(Stream stream)
    {
        public void WriteByte(byte value) => stream.WriteByte(value);

        private void WriteShort(short value)
        {
            Span<byte> buffer = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(buffer, value);
            stream.Write(buffer);
        }

        private void WriteInt(int value)
        {
            Span<byte> buffer = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(buffer, value);
            stream.Write(buffer);
        }

        private void WriteLong(long value)
        {
            Span<byte> buffer = stackalloc byte[8];
            BinaryPrimitives.WriteInt64BigEndian(buffer, value);
            stream.Write(buffer);
        }

        public void WriteString(string value)
        {
            var bytes = ModifiedUtf8.Encode(value);
            if (bytes.Length > ushort.MaxValue)
                throw new InvalidDataException("An NBT string cannot exceed 65535 bytes.");
            WriteShort((short)(ushort)bytes.Length);
            stream.Write(bytes);
        }

        public void WritePayload(NbtTagType type, object value)
        {
            switch (type)
            {
                case NbtTagType.Byte: WriteByte((byte)value); break;
                case NbtTagType.Short: WriteShort((short)value); break;
                case NbtTagType.Int: WriteInt((int)value); break;
                case NbtTagType.Long: WriteLong((long)value); break;
                case NbtTagType.Float: WriteInt(BitConverter.SingleToInt32Bits((float)value)); break;
                case NbtTagType.Double: WriteLong(BitConverter.DoubleToInt64Bits((double)value)); break;
                case NbtTagType.String: WriteString((string)value); break;
                case NbtTagType.ByteArray:
                    {
                        var bytes = (byte[])value;
                        WriteInt(bytes.Length);
                        stream.Write(bytes);
                        break;
                    }
                case NbtTagType.IntArray:
                    {
                        var values = (int[])value;
                        WriteInt(values.Length);
                        foreach (var item in values) WriteInt(item);
                        break;
                    }
                case NbtTagType.LongArray:
                    {
                        var values = (long[])value;
                        WriteInt(values.Length);
                        foreach (var item in values) WriteLong(item);
                        break;
                    }
                case NbtTagType.List:
                    {
                        var list = (NbtList)value;
                        var elementType = list.Count == 0 ? list.ElementType : TypeOf(list[0]);
                        WriteByte((byte)elementType);
                        WriteInt(list.Count);
                        foreach (var item in list) WritePayload(elementType, item);
                        break;
                    }
                case NbtTagType.Compound:
                    {
                        foreach (var (name, child) in (NbtCompound)value)
                        {
                            var childType = TypeOf(child);
                            WriteByte((byte)childType);
                            WriteString(name);
                            WritePayload(childType, child);
                        }
                        WriteByte((byte)NbtTagType.End);
                        break;
                    }
                default:
                    throw new InvalidDataException($"Cannot write NBT tag type {(byte)type}.");
            }
        }
    }
}

/// <summary>
/// Java's "modified UTF-8", which NBT strings use. It differs from real UTF-8 in two places:
/// U+0000 is written as two bytes, and characters above the BMP are written as a surrogate pair of
/// three-byte sequences instead of one four-byte sequence. Using plain UTF-8 would round-trip an
/// emoji in someone's server name into a different string.
/// </summary>
public static class ModifiedUtf8
{
    public static byte[] Encode(string value)
    {
        var buffer = new List<byte>(value.Length + 8);
        foreach (var character in value)
        {
            int code = character;
            if (code is >= 0x01 and <= 0x7F)
            {
                buffer.Add((byte)code);
            }
            else if (code <= 0x7FF)
            {
                buffer.Add((byte)(0xC0 | (code >> 6)));
                buffer.Add((byte)(0x80 | (code & 0x3F)));
            }
            else
            {
                // Includes U+0000 and each half of a surrogate pair, encoded on its own.
                buffer.Add((byte)(0xE0 | (code >> 12)));
                buffer.Add((byte)(0x80 | ((code >> 6) & 0x3F)));
                buffer.Add((byte)(0x80 | (code & 0x3F)));
            }
        }
        return [.. buffer];
    }

    public static string Decode(ReadOnlySpan<byte> bytes)
    {
        var builder = new StringBuilder(bytes.Length);
        var index = 0;
        while (index < bytes.Length)
        {
            var first = bytes[index];
            if ((first & 0x80) == 0)
            {
                builder.Append((char)first);
                index++;
            }
            else if ((first & 0xE0) == 0xC0)
            {
                if (index + 1 >= bytes.Length) throw new InvalidDataException("Truncated NBT string.");
                builder.Append((char)(((first & 0x1F) << 6) | (bytes[index + 1] & 0x3F)));
                index += 2;
            }
            else if ((first & 0xF0) == 0xE0)
            {
                if (index + 2 >= bytes.Length) throw new InvalidDataException("Truncated NBT string.");
                builder.Append((char)(((first & 0x0F) << 12) |
                                      ((bytes[index + 1] & 0x3F) << 6) |
                                      (bytes[index + 2] & 0x3F)));
                index += 3;
            }
            else
            {
                throw new InvalidDataException($"Invalid modified UTF-8 byte 0x{first:X2}.");
            }
        }
        return builder.ToString();
    }
}

using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal readonly record struct Rgb(byte Red, byte Green, byte Blue)
{
    public static Rgb Black => new(0, 0, 0);

    public static Rgb White => new(252, 252, 252);
}

internal sealed class PngCanvas
{
    private readonly byte[] pixels;

    public PngCanvas(int width, int height, Rgb background)
    {
        Width = width;
        Height = height;
        pixels = new byte[width * height * 3];
        FillRectangle(0, 0, width, height, background);
    }

    public int Width { get; }

    public int Height { get; }

    public void FillRectangle(int x, int y, int width, int height, Rgb colour)
    {
        int startX = Math.Max(0, x);
        int startY = Math.Max(0, y);
        int endX = Math.Min(Width, x + width);
        int endY = Math.Min(Height, y + height);
        for (int row = startY; row < endY; row++)
        {
            for (int column = startX; column < endX; column++)
            {
                SetPixel(column, row, colour);
            }
        }
    }

    public void DrawRectangle(int x, int y, int width, int height, Rgb light, Rgb dark)
    {
        FillRectangle(x, y, width, 2, light);
        FillRectangle(x, y, 2, height, light);
        FillRectangle(x, y + height - 2, width, 2, dark);
        FillRectangle(x + width - 2, y, 2, height, dark);
    }

    public void DrawLine(int x0, int y0, int x1, int y1, Rgb colour, int thickness)
    {
        int dx = Math.Abs(x1 - x0);
        int sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0);
        int sy = y0 < y1 ? 1 : -1;
        int error = dx + dy;
        while (true)
        {
            FillRectangle(x0 - (thickness / 2), y0 - (thickness / 2), thickness, thickness, colour);
            if (x0 == x1 && y0 == y1)
            {
                break;
            }

            int doubled = error * 2;
            if (doubled >= dy)
            {
                error += dy;
                x0 += sx;
            }

            if (doubled <= dx)
            {
                error += dx;
                y0 += sy;
            }
        }
    }

    public void DrawText(int x, int y, string text, Rgb colour, int scale, Rgb? shadow = null)
    {
        if (shadow is { } shadowColour)
        {
            DrawText(x + scale, y + scale, text, shadowColour, scale);
        }

        int cursor = x;
        foreach (char character in text.ToUpperInvariant())
        {
            byte[] glyph = BitmapFont.Get(character);
            for (int row = 0; row < glyph.Length; row++)
            {
                for (int column = 0; column < 5; column++)
                {
                    if ((glyph[row] & (1 << (4 - column))) != 0)
                    {
                        FillRectangle(cursor + (column * scale), y + (row * scale), scale, scale, colour);
                    }
                }
            }

            cursor += 6 * scale;
        }
    }

    public void DrawTextCentered(int centreX, int y, string text, Rgb colour, int scale, Rgb? shadow = null)
        => DrawText(centreX - (MeasureText(text, scale) / 2), y, text, colour, scale, shadow);

    public int MeasureText(string text, int scale) => Math.Max(0, (text.Length * 6 * scale) - scale);

    public Stream EncodePng()
    {
        var output = new MemoryStream();
        output.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, Width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], Height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(output, "IHDR", header);

        using var raw = new MemoryStream();
        for (int row = 0; row < Height; row++)
        {
            raw.WriteByte(0);
            raw.Write(pixels, row * Width * 3, Width * 3);
        }

        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            raw.CopyTo(zlib);
        }

        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        output.Position = 0;
        return output;
    }

    private void SetPixel(int x, int y, Rgb colour)
    {
        if ((uint)x >= Width || (uint)y >= Height)
        {
            return;
        }

        int offset = ((y * Width) + x) * 3;
        pixels[offset] = colour.Red;
        pixels[offset + 1] = colour.Green;
        pixels[offset + 2] = colour.Blue;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        output.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);

        uint crc = 0xFFFFFFFF;
        foreach (byte value in typeBytes)
        {
            crc = UpdateCrc(crc, value);
        }

        foreach (byte value in data)
        {
            crc = UpdateCrc(crc, value);
        }

        Span<byte> checksum = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        output.Write(checksum);
    }

    private static uint UpdateCrc(uint crc, byte value)
    {
        crc ^= value;
        for (int bit = 0; bit < 8; bit++)
        {
            crc = (crc & 1) != 0 ? 0xEDB88320U ^ (crc >> 1) : crc >> 1;
        }

        return crc;
    }

    private static class BitmapFont
    {
        private static readonly Dictionary<char, byte[]> Glyphs = new()
        {
            [' '] = Rows("00000", "00000", "00000", "00000", "00000", "00000", "00000"),
            ['A'] = Rows("01110", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['B'] = Rows("11110", "10001", "10001", "11110", "10001", "10001", "11110"),
            ['C'] = Rows("01111", "10000", "10000", "10000", "10000", "10000", "01111"),
            ['D'] = Rows("11110", "10001", "10001", "10001", "10001", "10001", "11110"),
            ['E'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "11111"),
            ['F'] = Rows("11111", "10000", "10000", "11110", "10000", "10000", "10000"),
            ['G'] = Rows("01111", "10000", "10000", "10111", "10001", "10001", "01111"),
            ['H'] = Rows("10001", "10001", "10001", "11111", "10001", "10001", "10001"),
            ['I'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "11111"),
            ['J'] = Rows("00111", "00010", "00010", "00010", "10010", "10010", "01100"),
            ['K'] = Rows("10001", "10010", "10100", "11000", "10100", "10010", "10001"),
            ['L'] = Rows("10000", "10000", "10000", "10000", "10000", "10000", "11111"),
            ['M'] = Rows("10001", "11011", "10101", "10101", "10001", "10001", "10001"),
            ['N'] = Rows("10001", "11001", "10101", "10011", "10001", "10001", "10001"),
            ['O'] = Rows("01110", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['P'] = Rows("11110", "10001", "10001", "11110", "10000", "10000", "10000"),
            ['Q'] = Rows("01110", "10001", "10001", "10001", "10101", "10010", "01101"),
            ['R'] = Rows("11110", "10001", "10001", "11110", "10100", "10010", "10001"),
            ['S'] = Rows("01111", "10000", "10000", "01110", "00001", "00001", "11110"),
            ['T'] = Rows("11111", "00100", "00100", "00100", "00100", "00100", "00100"),
            ['U'] = Rows("10001", "10001", "10001", "10001", "10001", "10001", "01110"),
            ['V'] = Rows("10001", "10001", "10001", "10001", "10001", "01010", "00100"),
            ['W'] = Rows("10001", "10001", "10001", "10101", "10101", "10101", "01010"),
            ['X'] = Rows("10001", "10001", "01010", "00100", "01010", "10001", "10001"),
            ['Y'] = Rows("10001", "10001", "01010", "00100", "00100", "00100", "00100"),
            ['Z'] = Rows("11111", "00001", "00010", "00100", "01000", "10000", "11111"),
            ['0'] = Rows("01110", "10001", "10011", "10101", "11001", "10001", "01110"),
            ['1'] = Rows("00100", "01100", "00100", "00100", "00100", "00100", "01110"),
            ['2'] = Rows("01110", "10001", "00001", "00010", "00100", "01000", "11111"),
            ['3'] = Rows("11110", "00001", "00001", "01110", "00001", "00001", "11110"),
            ['4'] = Rows("00010", "00110", "01010", "10010", "11111", "00010", "00010"),
            ['5'] = Rows("11111", "10000", "10000", "11110", "00001", "00001", "11110"),
            ['6'] = Rows("01110", "10000", "10000", "11110", "10001", "10001", "01110"),
            ['7'] = Rows("11111", "00001", "00010", "00100", "01000", "01000", "01000"),
            ['8'] = Rows("01110", "10001", "10001", "01110", "10001", "10001", "01110"),
            ['9'] = Rows("01110", "10001", "10001", "01111", "00001", "00001", "01110"),
            ['#'] = Rows("01010", "11111", "01010", "01010", "11111", "01010", "00000"),
            ['$'] = Rows("00100", "01111", "10100", "01110", "00101", "11110", "00100"),
            ['-'] = Rows("00000", "00000", "00000", "11111", "00000", "00000", "00000"),
            ['.'] = Rows("00000", "00000", "00000", "00000", "00000", "00110", "00110"),
            [','] = Rows("00000", "00000", "00000", "00000", "00110", "00100", "01000"),
            [':'] = Rows("00000", "00110", "00110", "00000", "00110", "00110", "00000"),
            ['/'] = Rows("00001", "00010", "00010", "00100", "01000", "01000", "10000"),
            ['&'] = Rows("01100", "10010", "10100", "01000", "10101", "10010", "01101"),
            ['('] = Rows("00010", "00100", "01000", "01000", "01000", "00100", "00010"),
            [')'] = Rows("01000", "00100", "00010", "00010", "00010", "00100", "01000"),
            ['?'] = Rows("01110", "10001", "00001", "00010", "00100", "00000", "00100"),
        };

        public static byte[] Get(char character) => Glyphs.GetValueOrDefault(character, Glyphs['?']);

        private static byte[] Rows(params string[] rows) => rows.Select(row => Convert.ToByte(row, 2)).ToArray();
    }
}

using System.Buffers.Binary;
using System.IO.Compression;
using OpenttdDiscord.Infrastructure.Ottd.Game;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class ProfitChartShould
{
    [Fact]
    public void RenderAnOpenTtdStylePngWithTheCompanyColour()
    {
        const uint companyColour = 0x123ABC;
        var companies = new[]
        {
            new CompanyProfit(1, "Rail & Road", new long[] { 100, -50, 0 }, companyColour),
            new CompanyProfit(2, "Air Cargo", new long[] { 100, 50 }),
            new CompanyProfit(3, "New Company", System.Array.Empty<long>()),
        };

        using var chart = ProfitChart.Render(companies, 2026, 10);
        using var data = new MemoryStream();
        chart.CopyTo(data);
        byte[] png = data.ToArray();

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
        Assert.Equal(1152, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.True(BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)) >= 442);
        Assert.True(DecodePixels(png).AsSpan().IndexOf(new byte[] { 0x12, 0x3A, 0xBC }) >= 0);
    }

    private static byte[] DecodePixels(byte[] png)
    {
        using var compressed = new MemoryStream();
        int offset = 8;
        while (offset < png.Length)
        {
            int length = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(offset, 4));
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                compressed.Write(png, offset + 8, length);
            }

            offset += length + 12;
        }

        compressed.Position = 0;
        using var decompressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            zlib.CopyTo(decompressed);
        }

        return decompressed.ToArray();
    }
}

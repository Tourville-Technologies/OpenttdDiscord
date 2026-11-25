using System.Xml.Linq;
using OpenttdDiscord.Infrastructure.Ottd.Game;

namespace OpenttdDiscord.Infrastructure.Tests.Ottd;

public class ProfitChartShould
{
    [Fact]
    public void AlignAllCompaniesToTheSameQuarterAndProfitScales()
    {
        var companies = new[]
        {
            new CompanyProfit(1, "Rail & Road", new long[] { 100, -50, 0 }),
            new CompanyProfit(2, "Air Cargo", new long[] { 100, 50 }),
            new CompanyProfit(3, "New Company", System.Array.Empty<long>()),
        };
        using var chart = ProfitChart.Render(companies);
        var svg = XDocument.Load(chart);
        XNamespace svgNamespace = "http://www.w3.org/2000/svg";
        var lines = svg.Descendants(svgNamespace + "polyline").ToArray();
        Assert.Equal(2, lines.Length);

        string[] longer = lines[0].Attribute("points")!.Value.Split(' ');
        string[] shorter = lines[1].Attribute("points")!.Value.Split(' ');
        Assert.Equal(longer[2], shorter[1]);
        Assert.Equal(longer[1].Split(',')[0], shorter[0].Split(',')[0]);
        Assert.NotEqual(lines[0].Attribute("stroke")!.Value, lines[1].Attribute("stroke")!.Value);
        Assert.Contains(svg.Descendants(svgNamespace + "text"), label => label.Value == "#3 New Company · no data");
    }
}

using System.Globalization;
using System.Security;
using System.Text;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal static class ProfitChart
{
    private const int Width = 1000;
    private const int PlotLeft = 90;
    private const int PlotRight = 970;
    private const int PlotTop = 72;
    private const int PlotBottom = 410;

    public static Stream Render(IReadOnlyList<CompanyProfit> companies)
    {
        const int legendColumns = 3;
        const int legendTop = 470;
        const int legendRowHeight = 24;
        int rows = (companies.Count + legendColumns - 1) / legendColumns;
        int height = legendTop + (rows * legendRowHeight) + 16;

        var values = companies.SelectMany(company => company.Quarters).DefaultIfEmpty(0).ToArray();
        decimal high = Math.Max(0, values.Max());
        decimal low = Math.Min(0, values.Min());
        if (high == low)
        {
            high = 1;
            low = -1;
        }

        int quarterCount = companies.Select(company => company.Quarters.Count).DefaultIfEmpty(0).Max();
        var svg = new StringBuilder();
        svg.Append(CultureInfo.InvariantCulture, $"<svg xmlns='http://www.w3.org/2000/svg' width='{Width}' height='{height}' viewBox='0 0 {Width} {height}' role='img'>");
        svg.Append("<title>OpenTTD company operating profit</title><rect width='100%' height='100%' fill='#111827'/>");
        svg.Append("<g fill='#f8fafc' font-family='system-ui,sans-serif'><text x='20' y='30' font-size='20' font-weight='700'>Company operating profit</text>");
        svg.Append("<text x='20' y='51' font-size='12' fill='#a9b8cc'>Completed quarters · OpenTTD base currency</text>");
        DrawAxes(svg, quarterCount, low, high);

        for (int index = 0; index < companies.Count; index++)
        {
            var company = companies[index];
            string name = SecurityElement.Escape(company.Name) ?? string.Empty;
            int hue = (index * 137) % 360;
            int style = index / 12;
            int lightness = 64 + ((style % 3) * 8);
            string color = FormattableString.Invariant($"hsl({hue},76%,{lightness}%)");
            string dash = (style % 4) switch
            {
                1 => "8 4",
                2 => "3 4",
                3 => "10 3 2 3",
                _ => "none",
            };

            DrawCompany(svg, company, quarterCount, low, high, color, dash, name);

            int legendX = 24 + ((index % legendColumns) * 326);
            int legendY = legendTop + ((index / legendColumns) * legendRowHeight);
            string label = SecurityElement.Escape(string.Concat(company.Name.EnumerateRunes().Take(28))) ?? string.Empty;
            string suffix = company.Quarters.Count == 0 ? " · no data" : string.Empty;
            svg.Append(CultureInfo.InvariantCulture,
                $"<g transform='translate({legendX} {legendY})'><title>#{company.Id} {name}{suffix}</title>" +
                $"<path d='M0 -4H24' stroke='{color}' stroke-width='3' stroke-dasharray='{dash}'/>" +
                $"<text x='32' font-size='12'>#{company.Id} {label}{suffix}</text></g>");
        }

        svg.Append("</g></svg>");
        return new MemoryStream(Encoding.UTF8.GetBytes(svg.ToString()), writable: false);
    }

    private static void DrawAxes(StringBuilder svg, int quarterCount, decimal low, decimal high)
    {
        svg.Append(CultureInfo.InvariantCulture,
            $"<rect x='{PlotLeft}' y='{PlotTop}' width='{PlotRight - PlotLeft}' height='{PlotBottom - PlotTop}' fill='#182235'/>");

        for (int tick = 0; tick <= 4; tick++)
        {
            decimal value = high - (((high - low) * tick) / 4);
            decimal y = ChartY(value, low, high);
            svg.Append(CultureInfo.InvariantCulture,
                $"<path d='M{PlotLeft} {y:F2}H{PlotRight}' stroke='#334155'/>" +
                $"<text x='{PlotLeft - 10}' y='{y + 4:F2}' font-size='12' text-anchor='end'>{Label(value)}</text>");
        }

        svg.Append(CultureInfo.InvariantCulture,
            $"<path d='M{PlotLeft} {ChartY(0, low, high):F2}H{PlotRight}' stroke='#94a3b8' stroke-dasharray='4 4'/>");
        if (quarterCount == 0)
        {
            svg.Append("<text x='530' y='240' text-anchor='middle' fill='#a9b8cc'>No completed quarters</text>");
            return;
        }

        if (quarterCount > 1)
        {
            svg.Append(CultureInfo.InvariantCulture, $"<text x='{PlotLeft}' y='436' font-size='12'>{quarterCount} quarters ago</text>");
        }

        svg.Append(CultureInfo.InvariantCulture, $"<text x='{PlotRight}' y='436' font-size='12' text-anchor='end'>Latest quarter</text>");
    }

    private static void DrawCompany(StringBuilder svg, CompanyProfit company, int quarterCount, decimal low, decimal high, string color, string dash, string name)
    {
        if (company.Quarters.Count == 0)
        {
            return;
        }

        var points = company.Quarters.Select((value, age) =>
        {
            decimal x = PlotRight - (((PlotRight - PlotLeft) * (decimal)age) / Math.Max(1, quarterCount - 1));
            return FormattableString.Invariant($"{x:F2},{ChartY(value, low, high):F2}");
        }).Reverse();
        svg.Append(CultureInfo.InvariantCulture,
            $"<polyline points='{string.Join(' ', points)}' fill='none' stroke='{color}' stroke-width='2.5' stroke-dasharray='{dash}'>" +
            $"<title>#{company.Id} {name}</title></polyline>" +
            $"<circle cx='{PlotRight}' cy='{ChartY(company.Quarters[0], low, high):F2}' r='3' fill='{color}'>" +
            $"<title>#{company.Id} {name} · Latest quarter {company.Quarters[0]:N0}</title></circle>");
    }

    private static decimal ChartY(decimal value, decimal low, decimal high)
        => PlotBottom - (((value - low) / (high - low)) * (PlotBottom - PlotTop));

    private static string Label(decimal value)
    {
        return Math.Abs(value) switch
        {
            >= 1_000_000_000_000_000 => FormattableString.Invariant($"{value / 1_000_000_000_000_000:0.##}Q"),
            >= 1_000_000_000_000 => FormattableString.Invariant($"{value / 1_000_000_000_000:0.##}T"),
            >= 1_000_000_000 => FormattableString.Invariant($"{value / 1_000_000_000:0.##}B"),
            >= 1_000_000 => FormattableString.Invariant($"{value / 1_000_000:0.##}M"),
            >= 1_000 => FormattableString.Invariant($"{value / 1_000:0.##}K"),
            _ => value.ToString("0.##", CultureInfo.InvariantCulture),
        };
    }
}

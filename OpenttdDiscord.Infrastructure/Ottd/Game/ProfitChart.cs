using System.Globalization;

namespace OpenttdDiscord.Infrastructure.Ottd.Game;

internal static class ProfitChart
{
    private const int Width = 1152;
    private const int PlotLeft = 112;
    private const int PlotRight = 1120;
    private const int PlotTop = 52;
    private const int PlotBottom = 372;
    private const int QuarterSlots = 24;
    private const int HorizontalLines = 8;

    private static readonly Rgb[] FallbackCompanyColours =
    {
        new(0, 72, 184), new(104, 160, 64), new(204, 104, 124), new(232, 184, 16),
        new(196, 0, 0), new(56, 144, 232), new(84, 132, 20), new(44, 68, 32),
        new(24, 120, 220), new(212, 188, 148), new(132, 132, 164), new(112, 68, 168),
        new(252, 136, 0), new(124, 84, 44), new(168, 168, 168), new(252, 252, 252),
    };

    public static Stream Render(IReadOnlyList<CompanyProfit> companies, int currentYear = 0, int currentMonth = 0)
    {
        const int legendColumns = 4;
        int legendRows = Math.Max(1, (companies.Count + legendColumns - 1) / legendColumns);
        int height = 420 + (legendRows * 22);
        var image = new PngCanvas(Width, height, new Rgb(156, 140, 96));

        DrawWindow(image, height);
        var interval = GetInterval(companies);
        DrawAxes(image, interval, currentYear, currentMonth);

        int quarterCount = Math.Min(QuarterSlots, companies.Select(company => company.Quarters.Count).DefaultIfEmpty(0).Max());
        for (int index = 0; index < companies.Count; index++)
        {
            Rgb colour = CompanyColour(companies[index], index);
            DrawCompany(image, companies[index], interval, colour);
            DrawLegendItem(image, companies[index], index, legendColumns, colour);
        }

        if (quarterCount == 0)
        {
            image.DrawTextCentered((PlotLeft + PlotRight) / 2, 205, "NO COMPLETED QUARTERS", new Rgb(168, 168, 168), 2);
        }

        return image.EncodePng();
    }

    private static void DrawWindow(PngCanvas image, int height)
    {
        var borderLight = new Rgb(220, 204, 152);
        var borderDark = new Rgb(76, 68, 44);
        image.FillRectangle(0, 0, Width, 32, new Rgb(112, 92, 48));
        image.FillRectangle(4, 4, Width - 8, 24, new Rgb(156, 140, 96));
        image.DrawRectangle(3, 3, Width - 6, height - 6, borderLight, borderDark);
        image.DrawTextCentered(Width / 2, 8, "OPERATING PROFIT GRAPH", Rgb.White, 2, Rgb.Black);
        image.DrawText(PlotRight - 45, 10, "KEY", Rgb.Black, 1);
        image.FillRectangle(PlotLeft, PlotTop, PlotRight - PlotLeft, PlotBottom - PlotTop, new Rgb(32, 32, 32));
    }

    private static void DrawAxes(PngCanvas image, GraphInterval interval, int currentYear, int currentMonth)
    {
        int xStep = (PlotRight - PlotLeft) / QuarterSlots;
        int yStep = (PlotBottom - PlotTop) / HorizontalLines;
        var grid = new Rgb(48, 48, 48);
        var yearGrid = new Rgb(82, 80, 82);

        int firstMonth = 0;
        int firstYear = 0;
        if (currentYear != 0)
        {
            firstMonth = ((currentMonth / 3) - QuarterSlots) * 3;
            firstYear = currentYear;
            while (firstMonth < 0)
            {
                firstMonth += 12;
                firstYear--;
            }
        }

        int month = firstMonth;
        int year = firstYear;
        string[] months = { "JAN", "APR", "JUL", "OCT" };
        for (int slot = 0; slot <= QuarterSlots; slot++)
        {
            int x = PlotLeft + (slot * xStep);
            bool yearBoundary = currentYear != 0 && month == 0;
            image.FillRectangle(x, PlotTop, 1, PlotBottom - PlotTop, yearBoundary ? yearGrid : grid);
            if (slot < QuarterSlots)
            {
                string quarterLabel = currentYear == 0 ? $"-{QuarterSlots - slot}Q" : months[month / 3];
                image.DrawText(x + 2, PlotBottom + 6, quarterLabel, Rgb.Black, 1);
                if (currentYear != 0 && month == 0)
                {
                    image.DrawText(x + 2, PlotBottom + 14, year.ToString(CultureInfo.InvariantCulture), Rgb.Black, 1);
                }

                month += 3;
                if (month >= 12)
                {
                    month = 0;
                    year++;
                }
            }
        }

        long label = interval.High;
        for (int line = 0; line <= HorizontalLines; line++)
        {
            int y = PlotTop + (line * yStep);
            Rgb colour = label == 0 ? new Rgb(148, 149, 148) : grid;
            image.FillRectangle(PlotLeft, y, PlotRight - PlotLeft, label == 0 ? 2 : 1, colour);
            string text = CurrencyLabel(label);
            int textWidth = image.MeasureText(text, 1);
            image.DrawText(PlotLeft - textWidth - 8, y - 3, text, label < 0 ? new Rgb(252, 52, 52) : Rgb.Black, 1);
            label -= interval.Step;
        }
    }

    private static void DrawCompany(PngCanvas image, CompanyProfit company, GraphInterval interval, Rgb colour)
    {
        int count = Math.Min(QuarterSlots, company.Quarters.Count);
        if (count == 0)
        {
            return;
        }

        int xStep = (PlotRight - PlotLeft) / QuarterSlots;
        var values = company.Quarters.Take(count).Reverse().ToArray();
        int firstSlot = QuarterSlots - values.Length;
        int previousX = -1;
        int previousY = -1;
        for (int index = 0; index < values.Length; index++)
        {
            int x = PlotLeft + ((firstSlot + index) * xStep) + (xStep / 2);
            int y = PlotTop + checked((int)Math.Round((interval.High - (decimal)values[index]) * (PlotBottom - PlotTop) / interval.Size));
            image.FillRectangle(x - 2, y - 2, 5, 5, colour);
            if (previousX >= 0)
            {
                image.DrawLine(previousX, previousY, x, y, colour, 3);
            }

            previousX = x;
            previousY = y;
        }
    }

    private static void DrawLegendItem(PngCanvas image, CompanyProfit company, int index, int columns, Rgb colour)
    {
        int columnWidth = (Width - 32) / columns;
        int x = 20 + ((index % columns) * columnWidth);
        int y = 414 + ((index / columns) * 22);
        image.FillRectangle(x, y, 18, 8, colour);
        string name = company.Name.Length > 25 ? company.Name[..25] : company.Name;
        string suffix = company.Quarters.Count == 0 ? " (NO DATA)" : string.Empty;
        image.DrawText(x + 25, y + 1, $"#{company.Id} {name}{suffix}", Rgb.Black, 1);
    }

    private static GraphInterval GetInterval(IReadOnlyList<CompanyProfit> companies)
    {
        var values = companies.SelectMany(company => company.Quarters.Take(QuarterSlots)).DefaultIfEmpty(0L).ToArray();
        double positive = Math.Max(0, values.Max()) * 1.1;
        double negative = Math.Max(0, -(double)values.Min()) * 1.1;
        int positiveLines;
        long step;

        if (positive == 0 && negative == 0)
        {
            positiveLines = HorizontalLines / 2;
            step = 1;
        }
        else
        {
            positiveLines = (int)Math.Floor(0.5 + (HorizontalLines * positive / (positive + negative)));
            if (positive > 0 && positiveLines == 0)
            {
                positiveLines++;
            }

            if (negative > 0 && positiveLines == HorizontalLines)
            {
                positiveLines--;
            }

            long positiveStep = positive > 0 ? checked((long)Math.Ceiling(positive / positiveLines)) : 0;
            int negativeLines = HorizontalLines - positiveLines;
            long negativeStep = negative > 0 ? checked((long)Math.Ceiling(negative / negativeLines)) : 0;
            step = Math.Max(1, Math.Max(positiveStep, negativeStep));
        }

        long high = positiveLines * step;
        long low = -(HorizontalLines - positiveLines) * step;
        return new GraphInterval(high, low, step);
    }

    private static Rgb CompanyColour(CompanyProfit company, int index)
    {
        if (company.Colour != 0)
        {
            return new Rgb((byte)(company.Colour >> 16), (byte)(company.Colour >> 8), (byte)company.Colour);
        }

        return FallbackCompanyColours[index % FallbackCompanyColours.Length];
    }

    private static string CurrencyLabel(long value)
    {
        decimal absolute = Math.Abs((decimal)value);
        return absolute switch
        {
            >= 1_000_000_000_000 => $"${value / 1_000_000_000_000M:0.##}T",
            >= 1_000_000_000 => $"${value / 1_000_000_000M:0.##}B",
            >= 1_000_000 => $"${value / 1_000_000M:0.##}M",
            >= 1_000 => $"${value / 1_000M:0.##}K",
            _ => $"${value.ToString(CultureInfo.InvariantCulture)}",
        };
    }

    private readonly record struct GraphInterval(long High, long Low, long Step)
    {
        public decimal Size => High - (decimal)Low;
    }
}

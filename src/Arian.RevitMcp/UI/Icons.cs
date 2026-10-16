using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Arian.RevitMcp.UI;

/// <summary>Draws ribbon icons at runtime as crisp vector glyphs on a gradient tile (no image resources needed).</summary>
public static class Icons
{
    public static readonly Color Green = Color.FromRgb(0x1F, 0x9D, 0x55);
    public static readonly Color Red = Color.FromRgb(0xD9, 0x3F, 0x3F);
    public static readonly Color Blue = Color.FromRgb(0x2B, 0x6C, 0xD9);
    public static readonly Color Orange = Color.FromRgb(0xE0, 0x8A, 0x1E);
    public static readonly Color Purple = Color.FromRgb(0x7A, 0x4F, 0xD1);
    public static readonly Color Gray = Color.FromRgb(0x5B, 0x65, 0x73);
    public static readonly Color Teal = Color.FromRgb(0x14, 0x8F, 0x9C);

    /// <summary>Legacy text-glyph icon (kept for callers that pass a character).</summary>
    public static BitmapSource Make(string glyph, Color bg, int size = 32, string font = "Segoe UI Symbol")
    {
        string kind = glyph switch { "▶" => "play", "■" => "stop", "⚙" => "gear", "⚠" => "warn", "✖" => "close", "≡" => "list", "i" => "info", "!" => "warn", "🔌" => "plug", "💬" => "bubble", _ => "text:" + glyph };
        return Draw(kind, bg, size);
    }

    public static BitmapSource Draw(string kind, Color bg, int size = 32)
    {
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            double s = size / 32.0;
            dc.DrawRoundedRectangle(new LinearGradientBrush(Lighten(bg, 0.22), Darken(bg, 0.08), 90), new Pen(new SolidColorBrush(Darken(bg, 0.25)), 0.8), new Rect(0.5, 0.5, size - 1, size - 1), 7 * s, 7 * s);
            dc.PushTransform(new ScaleTransform(s, s));
            var white = Brushes.White;
            var pen = new Pen(white, 2.6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            var thin = new Pen(white, 1.8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            void Line(double x1, double y1, double x2, double y2, Pen p = null) => dc.DrawLine(p ?? pen, new Point(x1, y1), new Point(x2, y2));
            switch (kind)
            {
                case "play": dc.DrawGeometry(white, null, Geometry.Parse("M12,8 L24.5,16 L12,24 Z")); break;
                case "stop": dc.DrawRoundedRectangle(white, null, new Rect(10, 10, 12, 12), 2, 2); break;
                case "plug":
                    Line(12.5, 6, 12.5, 12); Line(19.5, 6, 19.5, 12);
                    dc.DrawRoundedRectangle(null, pen, new Rect(9, 12, 14, 8), 3, 3); Line(16, 20, 16, 26); break;
                case "gear":
                    dc.DrawEllipse(null, pen, new Point(16, 16), 5.5, 5.5);
                    for (int i = 0; i < 8; i++) { double a = i * Math.PI / 4; Line(16 + 9 * Math.Cos(a), 16 + 9 * Math.Sin(a), 16 + 12 * Math.Cos(a), 16 + 12 * Math.Sin(a)); }
                    break;
                case "warn":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M16,6 L27,25 L5,25 Z")); Line(16, 12.5, 16, 18.5); dc.DrawEllipse(white, null, new Point(16, 22), 1.4, 1.4); break;
                case "bubble":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M7,8 H25 Q27,8 27,10 V19 Q27,21 25,21 H15 L10,26 V21 H7 Q5,21 5,19 V10 Q5,8 7,8 Z"));
                    foreach (var x in new[] { 11.0, 16.0, 21.0 }) dc.DrawEllipse(white, null, new Point(x, 14.5), 1.3, 1.3); break;
                case "close": Line(10, 10, 22, 22); Line(22, 10, 10, 22); break;
                case "list":
                    for (int i = 0; i < 3; i++) { double y = 9.5 + i * 6.5; dc.DrawEllipse(white, null, new Point(8.5, y), 1.4, 1.4); Line(12.5, y, 24.5, y, thin); }
                    break;
                case "info": dc.DrawEllipse(null, pen, new Point(16, 16), 9.5, 9.5); Line(16, 14.5, 16, 21.5); dc.DrawEllipse(white, null, new Point(16, 10.8), 1.4, 1.4); break;
                case "panel":
                    dc.DrawRoundedRectangle(null, pen, new Rect(6, 7, 20, 18), 2.5, 2.5); Line(6, 12.5, 26, 12.5, thin); Line(14, 12.5, 14, 25, thin); break;
                case "undo":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M11,12 H18 Q24,12 24,18 Q24,24 18,24 H12")); dc.DrawGeometry(white, null, Geometry.Parse("M6.5,12 L12.5,7.5 L12.5,16.5 Z")); break;
                case "cancel":
                    dc.DrawEllipse(null, pen, new Point(16, 16), 9.5, 9.5); Line(9.5, 22.5, 22.5, 9.5); break;
                case "dynamo":
                    dc.DrawGeometry(null, pen, Geometry.Parse("M16,6 L25,11 L25,21 L16,26 L7,21 L7,11 Z")); dc.DrawEllipse(white, null, new Point(16, 16), 2.6, 2.6); break;
                case "report":
                    dc.DrawRoundedRectangle(null, pen, new Rect(8, 6, 16, 20), 2, 2); Line(12, 12, 20, 12, thin); Line(12, 16, 20, 16, thin); Line(12, 20, 17, 20, thin); break;
                default:
                    string txt = kind.StartsWith("text:") ? kind.Substring(5) : kind;
                    var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 17, white, 96);
                    dc.DrawText(ft, new Point((32 - ft.Width) / 2, (32 - ft.Height) / 2)); break;
            }
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        return rtb;
    }

    static Color Lighten(Color c, double f) => Color.FromRgb((byte)(c.R + (255 - c.R) * f), (byte)(c.G + (255 - c.G) * f), (byte)(c.B + (255 - c.B) * f));
    static Color Darken(Color c, double f) => Color.FromRgb((byte)(c.R * (1 - f)), (byte)(c.G * (1 - f)), (byte)(c.B * (1 - f)));
}

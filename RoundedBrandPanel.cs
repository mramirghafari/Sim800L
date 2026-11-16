using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace SIM800L_Flasher;

internal sealed class RoundedBrandPanel : Panel
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal int CornerRadius { get; set; } = 15;

    public RoundedBrandPanel()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = CreateRoundedPath(ClientRectangle, CornerRadius);
        using var fill = new LinearGradientBrush(
            ClientRectangle,
            Color.FromArgb(255, 255, 255),
            Color.FromArgb(237, 248, 255),
            LinearGradientMode.Horizontal);
        e.Graphics.FillPath(fill, path);

        using var glow = new Pen(Color.FromArgb(95, 22, 175, 232), 1.5f);
        e.Graphics.DrawPath(glow, path);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        var oldRegion = Region;
        using var path = CreateRoundedPath(ClientRectangle, CornerRadius);
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        if (bounds.Width <= 1 || bounds.Height <= 1) return path;

        var safeRadius = Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2);
        var diameter = safeRadius * 2;
        var rect = new Rectangle(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(rect, 180, 90);
        rect.X = bounds.Right - diameter - 1;
        path.AddArc(rect, 270, 90);
        rect.Y = bounds.Bottom - diameter - 1;
        path.AddArc(rect, 0, 90);
        rect.X = bounds.Left;
        path.AddArc(rect, 90, 90);
        path.CloseFigure();
        return path;
    }
}

using System.Drawing;
using System.Drawing.Drawing2D;

namespace EmuWorks;

internal sealed class RoundedButton : Button
{
    bool hovered;
    public RoundedButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var rectangle = new Rectangle(0, 0, Width - 1, Height - 1);
        if (rectangle.Width < 16 || rectangle.Height < 16) return;
        using var path = new GraphicsPath(); int diameter = 16;
        path.AddArc(rectangle.X, rectangle.Y, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Y, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.X, rectangle.Bottom - diameter, diameter, diameter, 90, 90); path.CloseFigure();
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        Color background = !Enabled ? Color.FromArgb(231, 234, 241) : hovered ? ControlPaint.Light(BackColor, .12f) : BackColor;
        using var brush = new SolidBrush(background); e.Graphics.FillPath(brush, path);
        if (Focused) { using var pen = new Pen(Color.FromArgb(120, 120, 170), 2); e.Graphics.DrawPath(pen, path); }
        TextRenderer.DrawText(e.Graphics, Text, Font, rectangle, Enabled ? ForeColor : Color.FromArgb(135, 142, 160), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
    }
}

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;

namespace VoxAssist.Desktop.Controls;

public class CompassControl : Control
{
    public static readonly StyledProperty<double> AngleProperty =
        AvaloniaProperty.Register<CompassControl, double>(nameof(Angle), defaultValue: 0);

    public static readonly StyledProperty<bool> IsCcwProperty =
        AvaloniaProperty.Register<CompassControl, bool>(nameof(IsCcw), defaultValue: false);

    public double Angle
    {
        get => GetValue(AngleProperty);
        set => SetValue(AngleProperty, value);
    }

    public bool IsCcw
    {
        get => GetValue(IsCcwProperty);
        set => SetValue(IsCcwProperty, value);
    }

    static CompassControl()
    {
        AffectsRender<CompassControl>(AngleProperty, IsCcwProperty);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double size = 220;
        if (!double.IsInfinity(availableSize.Width) && !double.IsInfinity(availableSize.Height))
        {
            size = Math.Min(availableSize.Width, availableSize.Height);
        }
        return new Size(size, size);
    }

    public override void Render(DrawingContext context)
    {
        var width = Bounds.Width;
        var height = Bounds.Height;
        var side = Math.Min(width, height) - 40;
        
        if (side <= 0) return;

        var center = new Point(width / 2, height / 2);
        var radius = side / 2;

        // 1. Draw Background
        var bgBrush = new SolidColorBrush(Color.Parse("#111111"));
        var borderPen = new Pen(new SolidColorBrush(Color.Parse("#333333")), 2);
        context.DrawEllipse(bgBrush, borderPen, center, radius, radius);

        // 2. Draw Ticks
        var tickPen = new Pen(Brushes.White, 1);
        for (int i = 0; i < 360; i += 30)
        {
            double drawAngle = IsCcw ? -i : i;
            double rad = (drawAngle - 90) * Math.PI / 180.0;
            var start = new Point(center.X + Math.Cos(rad) * radius, center.Y + Math.Sin(rad) * radius);
            var end = new Point(center.X + Math.Cos(rad) * (radius - 8), center.Y + Math.Sin(rad) * (radius - 8));
            context.DrawLine(tickPen, start, end);
            
            if (i % 90 == 0)
            {
                var label = new FormattedText(i.ToString(), 
                    System.Globalization.CultureInfo.CurrentCulture, 
                    FlowDirection.LeftToRight, 
                    Typeface.Default, 
                    12, 
                    Brushes.Gray);
                var labelPos = new Point(center.X + Math.Cos(rad) * (radius + 18) - label.Width / 2, 
                                         center.Y + Math.Sin(rad) * (radius + 18) - label.Height / 2);
                context.DrawText(label, labelPos);
            }
        }

        // 3. Draw the 30° Beam (Pie Slice)
        // Correcting coordinate space: 0 deg is UP (-90 in math space)
        double beamCenterAngle = IsCcw ? -Angle : Angle;
        double startAngle = beamCenterAngle - 15;
        double endAngle = beamCenterAngle + 15;

        double radS = (startAngle - 90) * Math.PI / 180.0;
        double radE = (endAngle - 90) * Math.PI / 180.0;

        var pStart = new Point(center.X + Math.Cos(radS) * radius, center.Y + Math.Sin(radS) * radius);
        var pEnd = new Point(center.X + Math.Cos(radE) * radius, center.Y + Math.Sin(radE) * radius);

        var beamGeometry = new StreamGeometry();
        using (var ctx = beamGeometry.Open())
        {
            ctx.BeginFigure(center, true);
            ctx.LineTo(pStart);
            ctx.ArcTo(pEnd, new Size(radius, radius), 0, false, SweepDirection.Clockwise);
            ctx.EndFigure(true);
        }

        // Smooth Angular Fade: Peak at center of beam
        // ConicGradientBrush: 0 is UP, CW.
        var conicBrush = new ConicGradientBrush
        {
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Angle = beamCenterAngle,
            GradientStops =
            {
                new GradientStop(Color.Parse("#A000FF00"), 0.0),            // Center line (alpha 160)
                new GradientStop(Color.Parse("#0000FF00"), 15.0 / 360.0),  // Transparent at edges
                new GradientStop(Color.Parse("#0000FF00"), 1.0 - (15.0 / 360.0)),
                new GradientStop(Color.Parse("#A000FF00"), 1.0)
            }
        };

        // Combine with Radial Fade to make it fade out towards the circumference
        var radialMask = new RadialGradientBrush
        {
            GradientStops =
            {
                new GradientStop(Colors.White, 0.4),      // Solid core
                new GradientStop(Colors.Transparent, 1.0) // Fade to edge
            }
        };

        using (context.PushGeometryClip(beamGeometry))
        {
            using (context.PushOpacityMask(radialMask, new Rect(center.X - radius, center.Y - radius, side, side)))
            {
                context.DrawRectangle(conicBrush, null, new Rect(center.X - radius, center.Y - radius, side, side));
            }
            
            // Add a sharper center line for precision
            var linePen = new Pen(Brushes.Lime, 2);
            var pLine = new Point(center.X + Math.Cos((beamCenterAngle - 90) * Math.PI / 180.0) * radius, 
                                  center.Y + Math.Sin((beamCenterAngle - 90) * Math.PI / 180.0) * radius);
            context.DrawLine(linePen, center, pLine);
        }

        // 4. Draw current angle text
        var text = new FormattedText($"{Angle:F0}°", 
            System.Globalization.CultureInfo.CurrentCulture, 
            FlowDirection.LeftToRight, 
            Typeface.Default, 
            20, 
            Brushes.White);
        context.DrawText(text, new Point(center.X - text.Width / 2, center.Y - text.Height / 2));
    }
}

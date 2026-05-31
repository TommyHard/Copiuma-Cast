using System.Windows.Controls.Primitives;

namespace Cast.Desktop.Assets.Tooltip;

public static class CalloutToolTipPlacement
{
    public static CustomPopupPlacementCallback CenterBelowCallback => CenterBelow;
    public static CustomPopupPlacementCallback CenterAboveCallback => CenterAbove;

    public static CustomPopupPlacement[] CenterBelow(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        System.Windows.Point offset)
    {
        double x = (targetSize.Width - popupSize.Width) / 2.0 + offset.X;
        double y = targetSize.Height + offset.Y;

        var pt = new System.Windows.Point(
            Math.Round(x, MidpointRounding.AwayFromZero),
            Math.Round(y, MidpointRounding.AwayFromZero));

        return [new CustomPopupPlacement(pt, PopupPrimaryAxis.Horizontal)];
    }

    public static CustomPopupPlacement[] CenterAbove(
        System.Windows.Size popupSize,
        System.Windows.Size targetSize,
        System.Windows.Point offset)
    {
        double x = (targetSize.Width - popupSize.Width) / 2.0 + offset.X;
        double y = -popupSize.Height + offset.Y;

        var pt = new System.Windows.Point(
            Math.Round(x, MidpointRounding.AwayFromZero),
            Math.Round(y, MidpointRounding.AwayFromZero));

        return [new CustomPopupPlacement(pt, PopupPrimaryAxis.Horizontal)];
    }
}
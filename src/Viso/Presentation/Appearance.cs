using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Viso;

internal static class Appearance
{
    public static bool IsDark { get; private set; }
    public static readonly Dictionary<string, (string Light, string Dark)> Palette = new()
    {
        ["BackgroundBrush"] = ("#F5F5F7", "#1E1E20"),
        ["WindowBrush"] = ("#F7F7F9", "#1E1E20"),
        ["PanelBrush"] = ("#FFFFFF", "#2C2C2E"),
        ["SidebarBrush"] = ("#ECECF0", "#252527"),
        ["ChromeBrush"] = ("#FAFAFC", "#242426"),
        ["TextBrush"] = ("#1D1D1F", "#F5F5F7"),
        ["MutedBrush"] = ("#6E6E73", "#A8A8AE"),
        ["BorderBrush"] = ("#DCDCE3", "#454549"),
        ["FrameBorderBrush"] = ("#CFD0D6", "#49494D"),
        ["DividerBrush"] = ("#E3E3E8", "#3A3A3D"),
        ["InputBrush"] = ("#F7F7FA", "#363638"),
        ["PopupBrush"] = ("#FAFAFC", "#303032"),
        ["SecondaryButtonBrush"] = ("#ECECF1", "#444447"),
        ["AccentBrush"] = ("#007AFF", "#0A84FF"),
        ["LinkBrush"] = ("#0066CC", "#64A9FF"),
        ["SelectedBrush"] = ("#E7F0FF", "#263E60"),
        ["HoverBrush"] = ("#F0F3F9", "#3A3A3E"),
        ["NavHoverBrush"] = ("#12000000", "#18FFFFFF"),
        ["SwitchTrackBrush"] = ("#D2D2D7", "#55555B"),
        ["SwitchOnBrush"] = ("#34C759", "#30D158"),
        ["ScrollThumbBrush"] = ("#C4C4CC", "#64646C"),
        ["ScrollHoverBrush"] = ("#A0A0AA", "#888891"),
        ["ChevronBrush"] = ("#767680", "#A0A0A6")
    };
    public static void Apply(string mode)
    {
        IsDark = mode == "dark";
        foreach (var item in Palette)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(IsDark ? item.Value.Dark : item.Value.Light));
            brush.Freeze(); Application.Current.Resources[item.Key] = brush;
        }
    }
}

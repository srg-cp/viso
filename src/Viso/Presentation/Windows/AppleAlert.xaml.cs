using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Viso;

public partial class AppleAlert : Window
{
    public AppleAlert(string title, string message)
    {
        InitializeComponent();
        AlertTitle.Text = title; AlertMessage.Text = message;
        Loaded += (_, _) =>
        {
            if (!SystemParameters.ClientAreaAnimation) return;
            var duration = TimeSpan.FromMilliseconds(160);
            AlertSurface.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, duration));
            var transform = (ScaleTransform)AlertSurface.RenderTransform;
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.96, 1, duration));
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.96, 1, duration));
        };
    }
    public static void Present(string title, string message, Window? owner = null)
    {
        var alert = new AppleAlert(title, message);
        if (owner?.IsVisible == true) { alert.Owner = owner; alert.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
        alert.ShowDialog();
    }
    internal static AppleAlert CreateUninstallConfirmation()
    {
        var alert = new AppleAlert("¿Desinstalar Viso?", "Viso se cerrará y eliminará este ejecutable, los ajustes, las claves guardadas, los datos de la versión anterior y sus accesos directos. Esta acción no se puede deshacer.");
        alert.AcceptButton.Content = "Desinstalar";
        alert.AcceptButton.Background = (Brush)Application.Current.FindResource("DestructiveBrush");
        alert.AcceptButton.IsDefault = false;
        alert.CancelButton.Visibility = Visibility.Visible;
        alert.CancelButton.IsDefault = true;
        alert.Loaded += (_, _) => alert.CancelButton.Focus();
        return alert;
    }
    private void AcceptClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void CancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
    internal void AcceptForSmokeTest() => AcceptButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
    internal bool MatchesAppearance() => ((SolidColorBrush)AlertSurface.Background).Color == (Color)ColorConverter.ConvertFromString(Appearance.IsDark ? "#303032" : "#FAFAFC");
    private void AlertKeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { e.Handled = true; DialogResult = false; } }
}

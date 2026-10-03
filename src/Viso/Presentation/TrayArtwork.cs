using System;
using System.Drawing;
using System.Windows;

namespace Viso;

internal static class TrayArtwork
{
    public static Icon Create()
    {
        var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Viso;component/Resources/Branding/viso.ico"))
            ?? throw new InvalidOperationException("No se encontró el icono de Viso.");
        using var stream = resource.Stream;
        using var icon = new Icon(stream, new System.Drawing.Size(32, 32));
        return (Icon)icon.Clone();
    }
}

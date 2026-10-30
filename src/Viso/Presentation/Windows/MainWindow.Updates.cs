using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Viso;

public partial class MainWindow
{
    private readonly CancellationTokenSource updateCancellation = new();
    private bool checkingUpdate;
    private bool installingUpdate;
    private string? updateJob;
    private string? updateDownload;
    private UpdateRelease? availableUpdate;

    internal void StartUpdates(bool automatic = true)
    {
        UpdateVersionText.Text = "Viso " + UpdateClient.VersionLabel;
        if (!persistSettings) return;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        timer.Tick += (_, _) => _ = CheckUpdatesAsync(true);
        Closing += (_, _) => { timer.Stop(); updateCancellation.Cancel(); };
        timer.Start();
        if (automatic) _ = CheckUpdatesAsync(true);
    }

    private async void CheckUpdateClick(object sender, RoutedEventArgs e) => await CheckUpdatesAsync(false);

    private async Task CheckUpdatesAsync(bool automatic)
    {
        if (!persistSettings || checkingUpdate || availableUpdate != null || exiting || uninstalling || installingUpdate) return;
        checkingUpdate = true;
        CheckUpdateButton.IsEnabled = false;
        try
        {
            if (automatic) await Task.Delay(3000, updateCancellation.Token);
            UpdateStatusText.Text = "Buscando actualizaciones…";
            using var client = new UpdateClient();
            var release = await client.CheckAsync(UpdateClient.CurrentVersion, updateCancellation.Token);
            if (release == null)
            {
                UpdateStatusText.Text = "Estás usando la versión más reciente.";
                return;
            }
            updateJob = Updater.CreateJob();
            UpdateProgress.Visibility = Visibility.Visible;
            var progress = new Progress<int>(percent =>
            {
                UpdateProgress.Value = percent;
                UpdateStatusText.Text = $"Descargando Viso {release.Version.ToString(3)} · {percent}%";
            });
            updateDownload = await client.DownloadAsync(release, updateJob, progress, updateCancellation.Token);
            availableUpdate = release;
            UpdateStatusText.Text = $"Viso {release.Version.ToString(3)} está listo. Reinicia cuando quieras para actualizar.";
            RestartUpdateButton.Visibility = Visibility.Visible;
            CheckUpdateButton.Content = "Actualización lista";
        }
        catch (OperationCanceledException) when (updateCancellation.IsCancellationRequested) { }
        catch (Exception ex)
        {
            UpdateStatusText.Text = ex is IOException ? ex.Message : "No se pudo buscar o descargar la actualización. Comprueba tu conexión e inténtalo más tarde.";
        }
        finally
        {
            UpdateProgress.Visibility = Visibility.Collapsed;
            checkingUpdate = false;
            CheckUpdateButton.IsEnabled = availableUpdate == null;
            if (availableUpdate == null && updateJob != null)
            {
                try { Updater.DeleteJob(updateJob); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                updateJob = null;
            }
        }
    }

    private async void RestartUpdateClick(object sender, RoutedEventArgs e)
    {
        if (availableUpdate == null || updateJob == null || updateDownload == null || installingUpdate || uninstalling || exiting) return;
        if (busy)
        {
            AppleAlert.Present("Hay una consulta en curso", "Espera a que termine o cancélala antes de reiniciar para actualizar.", this);
            return;
        }
        var confirmation = AppleAlert.CreateUpdateConfirmation(availableUpdate.Version.ToString(3));
        confirmation.Owner = this;
        confirmation.WindowStartupLocation = WindowStartupLocation.CenterOwner;
        if (confirmation.ShowDialog() != true) return;
        // A global shortcut may have started a query while the dialog was open.
        if (busy || exiting || uninstalling) return;
        installingUpdate = true;
        ShellContent.IsEnabled = false;
        try
        {
            UpdateStatusText.Text = "Preparando la actualización…";
            await Updater.PrepareAsync(updateJob, updateDownload, availableUpdate.Sha256);
            Close();
        }
        catch (Exception ex)
        {
            installingUpdate = false;
            ShellContent.IsEnabled = true;
            UpdateStatusText.Text = "No se pudo actualizar: " + ex.Message;
            availableUpdate = null;
            updateDownload = null;
            RestartUpdateButton.Visibility = Visibility.Collapsed;
            CheckUpdateButton.Content = "Buscar actualizaciones";
            CheckUpdateButton.IsEnabled = true;
            try { Updater.DeleteJob(updateJob); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            updateJob = null;
        }
    }
}

using System;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Viso.Tests;

public sealed class UpdaterTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public async Task RealHelperCancelsUpdatesOrRestoresWithoutTouchingSettings(bool commit, bool failStartup, bool corruptDownload)
    {
        string root = Path.Combine(Path.GetTempPath(), "Viso-update-test-" + Guid.NewGuid().ToString("N"));
        string target = Path.Combine(root, "app with spaces [1]");
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "UpdateProbe")))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        string executable = Path.Combine(target, "Viso.UpdateProbe.exe");
        byte[] original = File.ReadAllBytes(executable);
        string settings = Path.Combine(target, "settings.json");
        File.WriteAllText(settings, "settings must survive");
        if (failStartup) File.WriteAllText(Path.Combine(target, "fail-start"), "");
        string job = Updater.CreateJob();
        string download = Path.Combine(job, "Viso.download");
        // An appended byte changes the binary while preserving its runnable PE image.
        byte[] replacement = new byte[original.Length + 1];
        original.CopyTo(replacement, 0);
        replacement[^1] = 42;
        File.WriteAllBytes(download, replacement);
        string hash = Convert.ToHexString(SHA256.HashData(replacement));
        var plan = new UpdatePlan(executable, download, corruptDownload ? new string('a', 64) : hash, executable + "." + Guid.NewGuid().ToString("N") + ".update", executable + "." + Guid.NewGuid().ToString("N") + ".backup");
        string script = Path.Combine(job, "update.ps1"), manifest = Path.Combine(job, "plan.json");
        File.WriteAllText(script, Updater.ReadScript(), new UTF8Encoding(true));
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan), new UTF8Encoding(true));
        using var parent = Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true })!;
        using var helper = Process.Start(Updater.StartInfo(script, manifest, job, parent.Id))!;
        try
        {
            if (!corruptDownload)
            {
                var watch = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(job, "ready")))
                {
                    Assert.False(helper.HasExited, "Updater exited before readiness.");
                    Assert.True(watch.Elapsed < TimeSpan.FromSeconds(20), "Updater did not become ready.");
                    await Task.Delay(100, TestContext.Current.CancellationToken);
                }
                File.WriteAllText(Path.Combine(job, commit ? "commit" : "abort"), "");
                Assert.Equal(original, File.ReadAllBytes(executable));
                parent.Kill();
                await parent.WaitForExitAsync(TestContext.Current.CancellationToken);
            }
            await helper.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(35), TestContext.Current.CancellationToken);
            Assert.Equal(commit && (failStartup || corruptDownload) ? 1 : 0, helper.ExitCode);
            Assert.Equal(commit && !failStartup && !corruptDownload ? replacement : original, File.ReadAllBytes(executable));
            Assert.Equal("settings must survive", File.ReadAllText(settings));
            Assert.False(File.Exists(plan.Stage));
            Assert.False(File.Exists(plan.Backup));
            if (!corruptDownload) Assert.False(Directory.Exists(job));
            if (failStartup)
            {
                var watch = Stopwatch.StartNew();
                while (!File.Exists(Path.Combine(target, "recovered")) && watch.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(100, TestContext.Current.CancellationToken);
                Assert.True(File.Exists(Path.Combine(target, "recovered")));
            }
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(TestContext.Current.CancellationToken); }
            Updater.DeleteJob(job);
            UninstallPlan.ValidatePath(root);
            // Windows/Defender can retain the image mapping briefly after WaitForExit.
            // Retry only this test's disposable directory, after validating its path.
            for (int attempt = 0; ; attempt++)
            {
                try { Directory.Delete(root, true); break; }
                catch (Exception ex) when (attempt < 20 && ex is IOException or UnauthorizedAccessException)
                { await Task.Delay(250, TestContext.Current.CancellationToken); }
            }
        }
    }
}

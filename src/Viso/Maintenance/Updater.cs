using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Viso;

internal sealed record UpdatePlan(string Executable, string Download, string Sha256, string Stage, string Backup);

internal static class Updater
{
    internal static string CreateJob()
    {
        string job = Path.Combine(Path.GetTempPath(), "Viso-update-" + Guid.NewGuid().ToString("N"));
        UninstallPlan.ValidatePath(job);
        Directory.CreateDirectory(job);
        return job;
    }

    internal static string ReadScript()
    {
        using var stream = typeof(Updater).Assembly.GetManifestResourceStream("Viso.Maintenance.Update.ps1")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static ProcessStartInfo StartInfo(string script, string plan, string job, int processId)
        => Uninstaller.StartInfo(script, plan, job, processId);

    internal static async Task PrepareAsync(string job, string download, string sha256)
    {
        string executable = Environment.ProcessPath ?? throw new IOException("No se encuentra Viso.exe.");
        // Only the portable build may replace itself. Never overwrite a development apphost.
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "Viso.dll")))
            throw new IOException("Las actualizaciones se aplican desde la versión portable de Viso.");
        UninstallPlan.ValidatePath(executable);
        string suffix = Guid.NewGuid().ToString("N");
        var plan = new UpdatePlan(executable, download, sha256, executable + "." + suffix + ".update", executable + "." + suffix + ".backup");
        string script = Path.Combine(job, "update.ps1"), manifest = Path.Combine(job, "plan.json");
        await File.WriteAllTextAsync(script, ReadScript(), new UTF8Encoding(true));
        await File.WriteAllTextAsync(manifest, JsonSerializer.Serialize(plan), new UTF8Encoding(true));
        using var helper = Process.Start(StartInfo(script, manifest, job, Environment.ProcessId)) ?? throw new IOException("No se pudo iniciar la actualización.");
        bool committed = false;
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(job, "ready")))
            {
                if (helper.HasExited || watch.Elapsed > TimeSpan.FromSeconds(20))
                    throw new IOException("No se pudo preparar la actualización. Comprueba que la carpeta de Viso permite escritura.");
                await Task.Delay(100);
            }
            await File.WriteAllTextAsync(Path.Combine(job, "commit"), "");
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                if (Directory.Exists(job) && !helper.HasExited)
                    await File.WriteAllTextAsync(Path.Combine(job, "abort"), "");
                if (!helper.HasExited)
                {
                    try { await helper.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); }
                    catch (TimeoutException) { helper.Kill(); await helper.WaitForExitAsync(); }
                }
                // Preparation may fail before the helper has sent its ready signal.
                UninstallPlan.ValidatePath(plan.Stage);
                if (File.Exists(plan.Stage)) File.Delete(plan.Stage);
            }
        }
    }

    internal static void Acknowledge(string job)
    {
        // The helper only accepts confirmation from the expected executable and child PID.
        string full = Path.GetFullPath(job);
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^Viso-update-[a-f0-9]{32}$"))
            throw new IOException("La confirmación de actualización no es válida.");
        UninstallPlan.ValidatePath(full);
        var plan = JsonSerializer.Deserialize<UpdatePlan>(File.ReadAllText(Path.Combine(full, "plan.json")))!;
        if (!string.Equals(plan.Executable, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase))
            throw new IOException("La actualización no corresponde a este ejecutable.");
        File.WriteAllText(Path.Combine(full, "started"), Environment.ProcessId.ToString());
    }

    internal static void DeleteJob(string job)
    {
        string full = Path.GetFullPath(job);
        if (!string.Equals(Path.GetDirectoryName(full), Path.TrimEndingDirectorySeparator(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
            || !System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(full), @"^Viso-update-[a-f0-9]{32}$")) return;
        UninstallPlan.ValidatePath(full);
        if (!Directory.Exists(full)) return;
        // Our jobs are flat: never traverse unexpected directories or links.
        foreach (string file in Directory.GetFiles(full)) { UninstallPlan.ValidatePath(file); File.Delete(file); }
        Directory.Delete(full);
    }
}

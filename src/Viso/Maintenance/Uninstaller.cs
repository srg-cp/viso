using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Viso;

internal sealed record UninstallPlan(string Executable, string[] DataFolders, string[] ShortcutFolders, string[] CompanionFiles)
{
    internal static UninstallPlan Create(string executable, string localData, string extractionRoot, params string[] shortcutFolders)
    {
        executable = Path.GetFullPath(executable);
        if (!File.Exists(executable) || !string.Equals(Path.GetExtension(executable), ".exe", StringComparison.OrdinalIgnoreCase))
            throw new IOException("No se encuentra el ejecutable de Viso.");
        var folders = new[] { Path.Combine(Path.GetFullPath(localData), "Viso"), Path.Combine(Path.GetFullPath(localData), "CapturaTutor"), Path.Combine(Path.GetFullPath(extractionRoot), "Viso") };
        foreach (var folder in folders)
        {
            ValidatePath(folder);
            // Recursive data cleanup must never include the running executable.
            if (executable.StartsWith(folder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Mueve Viso.exe fuera de su carpeta de datos antes de desinstalar.");
        }
        ValidatePath(executable);
        foreach (string folder in shortcutFolders) ValidatePath(Path.GetFullPath(folder));
        string directory = Path.GetDirectoryName(executable)!;
        string[] companions = Path.GetFileName(executable).Equals("Viso.exe", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Viso.dll", "Viso.pdb", "Viso.deps.json", "Viso.runtimeconfig.json" }.Select(name => Path.Combine(directory, name)).ToArray()
            : Array.Empty<string>();
        foreach (string file in companions) ValidatePath(file);
        return new(executable, folders.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), shortcutFolders, companions);
    }

    internal static void ValidatePath(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("La ubicación de Viso contiene un enlace. Mueve el ejecutable o sus datos a una carpeta local antes de desinstalar.");
    }
}

internal static class Uninstaller
{
    internal static string ReadScript()
    {
        using var stream = typeof(Uninstaller).Assembly.GetManifestResourceStream("Viso.Maintenance.Uninstall.ps1")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    internal static ProcessStartInfo StartInfo(string script, string plan, string job, int processId)
    {
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (string argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script, "-PlanFile", plan, "-JobFolder", job, "-ParentId", processId.ToString() })
            start.ArgumentList.Add(argument);
        return start;
    }

    internal static async Task PrepareAsync()
    {
        string localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string extractionRoot = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") ?? Path.Combine(Path.GetTempPath(), ".net");
        var plan = UninstallPlan.Create(Environment.ProcessPath ?? "", localData, extractionRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft", "Internet Explorer", "Quick Launch", "User Pinned", "TaskBar"));
        string job = Path.Combine(Path.GetTempPath(), "Viso-uninstall-" + Guid.NewGuid().ToString("N"));
        UninstallPlan.ValidatePath(job);
        Directory.CreateDirectory(job);
        string script = Path.Combine(job, "cleanup.ps1"), planFile = Path.Combine(job, "plan.json");
        Process? helper = null;
        bool committed = false;
        try
        {
            await File.WriteAllTextAsync(script, ReadScript(), new UTF8Encoding(true));
            await File.WriteAllTextAsync(planFile, JsonSerializer.Serialize(plan), new UTF8Encoding(true));
            helper = Process.Start(StartInfo(script, planFile, job, Environment.ProcessId)) ?? throw new IOException("No se pudo iniciar la desinstalación.");
            var timeout = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(job, "ready")))
            {
                if (helper.HasExited || timeout.Elapsed > TimeSpan.FromSeconds(15))
                    throw new IOException("No se pudo preparar la desinstalación. Viso sigue abierto; inténtalo de nuevo.");
                await Task.Delay(100);
            }
            await File.WriteAllTextAsync(Path.Combine(job, "commit"), "");
            committed = true;
        }
        finally
        {
            if (!committed)
            {
                if (helper is { HasExited: false }) { helper.Kill(); await helper.WaitForExitAsync(); }
                UninstallPlan.ValidatePath(job);
                if (Directory.Exists(job)) Directory.Delete(job, true);
            }
            helper?.Dispose();
        }
    }
}

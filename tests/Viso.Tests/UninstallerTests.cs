using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Viso.Tests;

public sealed class UninstallerTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "Viso-uninstall-test-" + Guid.NewGuid().ToString("N"), "prueba á ' $");

    public UninstallerTests() => Directory.CreateDirectory(root);

    private UninstallPlan Plan()
    {
        string executable = Path.Combine(root, "Viso.exe");
        File.WriteAllText(executable, "fixture, never an actual installation");
        return UninstallPlan.Create(executable, Path.Combine(root, "local"), Path.Combine(root, "cache"), Path.Combine(root, "shortcuts"));
    }

    [Fact]
    public void RefusesExecutableInsideDataFolder()
    {
        string local = Path.Combine(root, "local");
        string folder = Path.Combine(local, "Viso");
        Directory.CreateDirectory(folder);
        string executable = Path.Combine(folder, "Viso.exe");
        File.WriteAllText(executable, "fixture");
        Assert.Throws<IOException>(() => UninstallPlan.Create(executable, local, Path.Combine(root, "cache")));
    }

    [Fact]
    public void RefusesJunctionInDataPath()
    {
        var plan = Plan();
        string destination = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(plan.DataFolders[0])!);
        CreateJunction(plan.DataFolders[0], destination);
        try { Assert.Throws<IOException>(() => UninstallPlan.ValidatePath(plan.DataFolders[0])); }
        finally { Directory.Delete(plan.DataFolders[0]); }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CleanupRequiresConfirmationAndExitAndPreservesUnrelatedFiles(bool confirm)
    {
        var plan = Plan();
        foreach (string folder in plan.DataFolders)
        {
            Directory.CreateDirectory(Path.Combine(folder, "nested"));
            File.WriteAllText(Path.Combine(folder, "nested", "settings [1].json"), "fixture");
        }
        foreach (string file in plan.CompanionFiles) File.WriteAllText(file, "fixture");
        string other = Path.Combine(root, "unrelated");
        Directory.CreateDirectory(other);
        string preserved = Path.Combine(other, "keep.txt");
        File.WriteAllText(preserved, "keep");
        string link = Path.Combine(plan.DataFolders[0], "linked");
        CreateJunction(link, other);
        string shortcuts = plan.ShortcutFolders[0];
        Directory.CreateDirectory(shortcuts);
        string ownShortcut = Path.Combine(shortcuts, "Viso.lnk"), otherShortcut = Path.Combine(shortcuts, "Other.lnk");
        CreateShortcut(ownShortcut, plan.Executable);
        CreateShortcut(otherShortcut, Path.Combine(root, "Other.exe"));
        string job = Path.Combine(root, "job");
        Directory.CreateDirectory(job);
        string script = Path.Combine(job, "cleanup.ps1"), manifest = Path.Combine(job, "plan.json");
        File.WriteAllText(script, Uninstaller.ReadScript(), new UTF8Encoding(true));
        File.WriteAllText(manifest, JsonSerializer.Serialize(plan), new UTF8Encoding(true));
        using var parent = Process.Start(new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 60") { UseShellExecute = false, CreateNoWindow = true })!;
        using var helper = Process.Start(Uninstaller.StartInfo(script, manifest, job, parent.Id))!;
        try
        {
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(job, "ready")))
            {
                Assert.False(helper.HasExited, "Helper exited before readiness.");
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(20), "Helper did not become ready.");
                await Task.Delay(100, TestContext.Current.CancellationToken);
            }
            if (confirm) File.WriteAllText(Path.Combine(job, "commit"), "");
            await Task.Delay(300, TestContext.Current.CancellationToken);
            Assert.True(File.Exists(plan.Executable));
            Assert.All(plan.DataFolders, folder => Assert.True(Directory.Exists(folder)));
            parent.Kill();
            await helper.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            Assert.Equal(0, helper.ExitCode);
            Assert.Equal(!confirm, File.Exists(plan.Executable));
            Assert.All(plan.DataFolders, folder => Assert.Equal(!confirm, Directory.Exists(folder)));
            Assert.All(plan.CompanionFiles, file => Assert.Equal(!confirm, File.Exists(file)));
            Assert.Equal(!confirm, File.Exists(ownShortcut));
            Assert.True(File.Exists(otherShortcut));
            Assert.True(File.Exists(preserved));
            Assert.False(Directory.Exists(job));
        }
        finally
        {
            if (!parent.HasExited) { parent.Kill(); await parent.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (!helper.HasExited) { helper.Kill(); await helper.WaitForExitAsync(TestContext.Current.CancellationToken); }
            if (Directory.Exists(link)) Directory.Delete(link);
        }
    }

    private static void CreateJunction(string link, string destination)
    {
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-EncodedCommand");
        string command = "New-Item -ItemType Junction -Path '" + link.Replace("'", "''") + "' -Target '" + destination.Replace("'", "''") + "' | Out-Null";
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(command)));
        using var process = Process.Start(start)!;
        process.WaitForExit();
        Assert.True(process.ExitCode == 0, process.StandardError.ReadToEnd());
    }

    private static void CreateShortcut(string path, string target)
    {
        Type type = Type.GetTypeFromProgID("WScript.Shell")!;
        dynamic shell = Activator.CreateInstance(type)!;
        dynamic shortcut = shell.CreateShortcut(path);
        shortcut.TargetPath = target;
        shortcut.Save();
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);
        System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
    }

    public void Dispose()
    {
        UninstallPlan.ValidatePath(root);
        Directory.Delete(Path.GetDirectoryName(root)!, true);
    }
}

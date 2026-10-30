// A disposable executable for testing the real updater without launching Viso.
if (args.Length == 2 && args[0] == "--updated")
{
    if (File.Exists(Path.Combine(AppContext.BaseDirectory, "fail-start"))) return 1;
    File.WriteAllText(Path.Combine(args[1], "started"), Environment.ProcessId.ToString());
    return 0;
}
if (args.Contains("--update-failed"))
{
    File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "recovered"), "ok");
    return 0;
}
await Task.Delay(TimeSpan.FromSeconds(90));
return 0;

namespace Viso;

internal static class ResponseDismissal
{
    public const string Time = "time";
    public const string Click = "click";
    public const string Scroll = "scroll";
    public static string Normalize(string mode) => mode is Click or Scroll ? mode : Time;
    public static bool MatchesMouse(string mode, ushort buttons) => mode switch
    {
        Click => (buttons & (0x0001 | 0x0004 | 0x0010 | 0x0040 | 0x0100)) != 0,
        Scroll => (buttons & (0x0400 | 0x0800)) != 0,
        _ => false
    };
}

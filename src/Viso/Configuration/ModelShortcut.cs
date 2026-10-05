using System.Collections.Generic;

namespace Viso;

public sealed class ModelShortcut
{
    public string Provider { get; set; } = "OpenAI";
    public string Model { get; set; } = "gpt-5.4-mini";
    public uint Modifiers { get; set; } = 5;
    public int VirtualKey { get; set; } = 0x31;
    public bool ShortAnswer { get; set; }

    public ModelShortcut Copy() => (ModelShortcut)MemberwiseClone();
    public override string ToString() => Provider + " · " + Model;

    internal static IReadOnlyList<ModelShortcut> Choices { get; } = new[]
    {
        new ModelShortcut { Model = "gpt-5.4-mini" },
        new ModelShortcut { Model = "gpt-5.4" },
        new ModelShortcut { Provider = "Gemini", Model = "gemini-3.5-flash-lite" },
        new ModelShortcut { Provider = "Gemini", Model = "gemini-3.1-flash-lite" },
        new ModelShortcut { Provider = "Gemini", Model = "gemini-2.5-flash-lite" },
        new ModelShortcut { Provider = "Gemini", Model = "gemini-3.5-flash" },
        new ModelShortcut { Provider = "Claude", Model = "claude-sonnet-5-5" },
        new ModelShortcut { Provider = "Claude", Model = "claude-opus-5-5" },
        new ModelShortcut { Provider = "Claude", Model = "claude-haiku-4-5" },
        new ModelShortcut { Provider = "Claude", Model = "claude-fable-5-1" }
    };
}

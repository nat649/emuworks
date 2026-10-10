using System.Text.Json;

namespace EmuWorks;

internal sealed class AppSettings
{
    public int Zoom { get; set; }
    public Dictionary<string, string> KeyBindings { get; set; } = new();
    public bool CustomKeyboard { get; set; }
    public bool CheckUpdatesOnLaunch { get; set; }
    public void Validate()
    {
        if (Zoom < 0 || Zoom > 4 || KeyBindings == null) throw new IOException("Invalid application settings.");
        foreach (var binding in KeyBindings)
        {
            if (!Enum.TryParse<Keys>(binding.Key, out var key) || (key & Keys.KeyCode) == Keys.None
                || !CalculatorKeys.Contains(binding.Value)) throw new IOException("Invalid keyboard binding.");
        }
    }
    public static readonly string[] CalculatorKeys =
    {
        "LEFT", "UP", "DOWN", "RIGHT", "OK", "BACK", "HOME", "ONOFF", "SHIFT", "ALPHA", "XNT", "VAR", "TOOLBOX", "BACKSPACE",
        "EXP", "LN", "LOG", "IMAGINARY", "COMMA", "POWER", "SINE", "COSINE", "TANGENT", "PI", "SQRT", "SQUARE",
        "SEVEN", "EIGHT", "NINE", "LEFTPARENTHESIS", "RIGHTPARENTHESIS", "FOUR", "FIVE", "SIX", "MULTIPLICATION", "DIVISION",
        "ONE", "TWO", "THREE", "PLUS", "MINUS", "ZERO", "DOT", "EE", "ANS", "EXE"
    };
    public static AppSettings Load(string root)
    {
        string file = Path.Combine(root, "settings.json");
        var value = File.Exists(file) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? throw new IOException("Invalid settings file.") : new();
        value.Validate(); return value;
    }
    public void Save(string root) { Validate(); ScriptStorage.WriteJson(Path.Combine(root, "settings.json"), this); }
}

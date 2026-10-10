using System.Net;
using System.Net.Sockets;

namespace EmuWorks;

public partial class MainForm
{
    private DeveloperDialog developerWindow;
    private ComparisonForm comparisonWindow;
    private NumericUpDown batteryVoltage;
    private Label batteryState;
    private bool syncingBattery;
    private readonly HashSet<string> virtualModifiers = new();
    private readonly Dictionary<string, Button> virtualButtons = new();

    internal static int ReserveScreenPort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        try { return ((IPEndPoint)probe.LocalEndpoint).Port; }
        finally { probe.Stop(); }
    }

    private string PrepareBootScript(string template)
    {
        string rom = RomDir.Replace('\\', '/');
        string text = File.ReadAllText(Path.Combine(RenodeDir, template))
            .Replace("@../rom/", "@" + rom + "/")
            .Replace("lcd Serve 3555", "lcd Serve " + PortEcran);
        // Apply the selected battery level before the firmware starts reading ADC1.
        text = text.Replace("cpu VectorTableOffset 0x08000000", "cpu VectorTableOffset 0x08000000\nadc SetMillivolts " + settings.BatteryMillivolts);
        string directory = Path.Combine(RomDir, ".sessions"); Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, template);
        File.WriteAllText(path, text);
        return path.Replace('\\', '/');
    }

    private string PrepareRenodeConfig()
    {
        string directory = Path.Combine(RomDir, ".sessions"); Directory.CreateDirectory(directory);
        string config = Path.Combine(directory, "renode.config");
        File.WriteAllText(config, "[general]\nhistory-path = " + Path.Combine(directory, "history").Replace('\\', '/') + "\ncompiler-cache-enabled = False\n[monitor]\nconsume-exceptions-from-command = True\nbreak-script-on-exception = True\n");
        return config;
    }

    private Control BuildCalculatorTools()
    {
        var tabs = new TabControl { Dock = DockStyle.Fill };
        var keyboard = new TabPage("Keyboard") { BackColor = Color.White };
        var keys = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 10, Padding = new(5) };
        for (int i = 0; i < 6; i++) keys.ColumnStyles.Add(new(SizeType.Percent, 100f / 6));
        for (int i = 0; i < 10; i++) keys.RowStyles.Add(new(SizeType.Percent, 10));
        var rows = new (string Label, string Key)[][]
        {
            new[] { ("←", "LEFT"), ("↑", "UP"), ("↓", "DOWN"), ("→", "RIGHT"), ("OK", "OK"), ("Back", "BACK") },
            new[] { ("Home", "HOME"), ("On/Off", "ONOFF") },
            new[] { ("Shift", "SHIFT"), ("Alpha", "ALPHA"), ("x,n,t", "XNT"), ("Var", "VAR"), ("Toolbox", "TOOLBOX"), ("⌫", "BACKSPACE") },
            new[] { ("exp", "EXP"), ("ln", "LN"), ("log", "LOG"), ("i", "IMAGINARY"), (",", "COMMA"), ("xʸ", "POWER") },
            new[] { ("sin", "SINE"), ("cos", "COSINE"), ("tan", "TANGENT"), ("π", "PI"), ("√", "SQRT"), ("x²", "SQUARE") },
            new[] { ("7", "SEVEN"), ("8", "EIGHT"), ("9", "NINE"), ("(", "LEFTPARENTHESIS"), (")", "RIGHTPARENTHESIS") },
            new[] { ("4", "FOUR"), ("5", "FIVE"), ("6", "SIX"), ("×", "MULTIPLICATION"), ("÷", "DIVISION") },
            new[] { ("1", "ONE"), ("2", "TWO"), ("3", "THREE"), ("+", "PLUS"), ("−", "MINUS") },
            new[] { ("0", "ZERO"), (".", "DOT"), ("EE", "EE"), ("Ans", "ANS"), ("EXE", "EXE") }
        };
        for (int row = 0; row < rows.Length; row++)
            for (int col = 0; col < rows[row].Length; col++)
            {
                string key = rows[row][col].Key;
                var button = ActionButton(rows[row][col].Label, (_, _) => VirtualKey(key));
                button.MinimumSize = Size.Empty; button.AutoSize = false; button.Dock = DockStyle.Fill;
                button.Padding = Padding.Empty; button.Margin = new(2); button.TabStop = false;
                keys.Controls.Add(button, col, row);
                virtualButtons[key] = button;
            }
        var hint = new Label { Text = "Code uses navigation and text-compatible keys. Shift and Alpha toggle until pressed again.", Dock = DockStyle.Fill, AutoEllipsis = true, Font = new("Segoe UI", 8) };
        keys.Controls.Add(hint, 0, 9); keys.SetColumnSpan(hint, 6); keyboard.Controls.Add(keys);
        var hardware = new TabPage("Battery") { BackColor = Color.White, Padding = new(14) };
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        controls.Controls.Add(new Label { Text = "Simulated battery voltage (mV)", AutoSize = true });
        batteryVoltage = new() { Minimum = 3000, Maximum = 4300, Increment = 25, Value = 4050, Width = 120 };
        batteryVoltage.ValueChanged += (_, _) => SetBattery((int)batteryVoltage.Value);
        controls.Controls.Add(batteryVoltage);
        var presets = new FlowLayoutPanel { AutoSize = true, WrapContents = true, Width = 290 };
        foreach (var preset in new[] { ("Full", 4050), ("Medium", 3750), ("Low", 3650), ("Empty", 3500) })
            presets.Controls.Add(ActionButton(preset.Item1, (_, _) => batteryVoltage.Value = preset.Item2));
        controls.Controls.Add(presets);
        batteryState = new Label { AutoSize = true }; controls.Controls.Add(batteryState);
        controls.Controls.Add(new Label { Text = "Sets the ADC voltage, not a universal percentage. Firmware decides the battery icon and refresh timing. No charging or automatic drain is simulated.", Width = 300, Height = 95 });
        hardware.Controls.Add(controls);
        var developer = new TabPage("Developer") { BackColor = Color.White, Padding = new(14) };
        var dev = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        dev.Controls.Add(ActionButton("Open developer tools", (_, _) => OpenDeveloper()));
        dev.Controls.Add(new Label { Text = "Renode console, CPU inspection, memory dumps and GDB. Commands apply only to this calculator.", Width = 300, Height = 85 });
        developer.Controls.Add(dev); tabs.TabPages.AddRange(new[] { keyboard, hardware, developer }); return tabs;
    }

    internal void VirtualKey(string key)
    {
        if (!enMarche || editingSettings) return;
        if (codeSession)
        {
            string text = key switch
            {
                "LEFT" => "", "RIGHT" => "", "UP" => "\x11", "DOWN" => "\x12", "HOME" or "BACK" => "\x1b",
                "OK" or "EXE" => "\r", "BACKSPACE" => "\b", "ZERO" => "0", "ONE" => "1", "TWO" => "2",
                "THREE" => "3", "FOUR" => "4", "FIVE" => "5", "SIX" => "6", "SEVEN" => "7", "EIGHT" => "8", "NINE" => "9",
                "PLUS" => "+", "MINUS" => "-", "MULTIPLICATION" => "*", "DIVISION" => "/", "LEFTPARENTHESIS" => "(",
                "RIGHTPARENTHESIS" => ")", "DOT" => ".", "COMMA" => ",", "POWER" => "**", "SHIFT" or "ALPHA" => "",
                _ => ""
            };
            foreach (char c in text) SendCodeChar(c);
            if (text.Length == 0) Log("This virtual key is not supported by Code's text input.");
        }
        else if (key is "SHIFT" or "ALPHA")
        {
            if (virtualModifiers.Remove(key)) Commande("keyboard ReleaseKey \"" + key + "\"");
            else { virtualModifiers.Add(key); Commande("keyboard PressKey \"" + key + "\""); }
        }
        else Commande("keyboard TapKey \"" + key + "\"");
        UpdateVirtualModifiers();
        ecran.Focus();
    }

    private void UpdateVirtualModifiers()
    {
        foreach (string key in new[] { "SHIFT", "ALPHA" })
            if (virtualButtons.TryGetValue(key, out var button))
            {
                button.BackColor = virtualModifiers.Contains(key) ? Accent : Color.FromArgb(235, 239, 248);
                button.ForeColor = virtualModifiers.Contains(key) ? Color.White : Color.FromArgb(40, 49, 70);
                button.Invalidate();
            }
    }

    private void SetBattery(int millivolts)
    {
        if (syncingBattery) return;
        settings.BatteryMillivolts = millivolts;
        UpdateBatteryLabel();
        try { using var lease = sessionLease == null ? AcquireLease() : null; settings.Save(baseDir); }
        catch (Exception ex) { Log("Battery settings: " + ex.Message); }
        ApplyBattery();
    }
    private void UpdateBatteryLabel()
    {
        int millivolts = settings.BatteryMillivolts;
        batteryState.Text = $"{millivolts / 1000.0:F2} V · " + (millivolts < 3600 ? "Empty" : millivolts < 3700 ? "Low" : millivolts < 3800 ? "Medium" : "Full");
    }
    private void ApplyBattery() { if (enMarche) Commande("adc SetMillivolts " + settings.BatteryMillivolts); }

    private void OpenDeveloper()
    {
        foreach (string key in touchesEnfoncees.ToList()) Relacher(key);
        foreach (string modifier in virtualModifiers) Commande("keyboard ReleaseKey \"" + modifier + "\"");
        virtualModifiers.Clear();
        UpdateVirtualModifiers();
        if (developerWindow == null || developerWindow.IsDisposed)
            developerWindow = new DeveloperDialog(this);
        developerWindow.Show(this); developerWindow.BringToFront();
    }
    internal bool SendDeveloperCommand(string command)
    {
        if (!enMarche) { Log("Start this calculator before sending developer commands."); return false; }
        if (string.IsNullOrWhiteSpace(command) || command.Length > 4096 || command.Contains('\n') || command.Contains('\r')) return false;
        Log("> " + command); Commande(command); return true;
    }
    internal Task StopCalculator() => Arreter();
    internal Task StartCalculator() => Demarrer();
    internal bool IsRunning => enMarche;
    internal bool HasProcess => renode != null && !renode.HasExited;
    internal int ScreenPort => PortEcran;
    internal void SelectNewScreenPort() => PortEcran = ReserveScreenPort();
    internal string DataDirectory => baseDir;

    private void OpenComparison()
    {
        if (comparisonWindow != null && !comparisonWindow.IsDisposed) { comparisonWindow.BringToFront(); return; }
        try { comparisonWindow = new ComparisonForm(baseDir, assetRoot); comparisonWindow.Show(this); }
        catch (Exception ex) { Log("Comparison: " + ex.Message); MessageBox.Show(this, ex.Message, "Comparison"); }
    }
}

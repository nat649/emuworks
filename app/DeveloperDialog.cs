using System.Globalization;

namespace EmuWorks;

internal sealed class DeveloperDialog : Form
{
    readonly MainForm calculator;
    readonly TextBox output = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Font = new("Consolas", 9), WordWrap = false };
    readonly TextBox command = new() { Dock = DockStyle.Fill, PlaceholderText = "Renode monitor command" };
    readonly TextBox address = new() { Text = "20000000", Width = 105 };
    readonly NumericUpDown count = new() { Minimum = 1, Maximum = 1048576, Value = 256, Width = 100 };
    readonly NumericUpDown gdbPort = new() { Minimum = 1024, Maximum = 65535, Value = 3333, Width = 90 };

    public DeveloperDialog(MainForm owner)
    {
        calculator = owner;
        Text = "EmuWorks developer tools"; ClientSize = new(800, 580); MinimumSize = new(700, 500); StartPosition = FormStartPosition.CenterParent;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 5, ColumnCount = 1, Padding = new(12) };
        layout.RowStyles.Add(new(SizeType.Absolute, 65)); layout.RowStyles.Add(new(SizeType.Absolute, 45));
        layout.RowStyles.Add(new(SizeType.Absolute, 80)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 48));
        layout.Controls.Add(new Label { Text = "Advanced controls for this calculator only. Commands can modify emulated memory. Pause before a consistent dump; Resume afterwards. A paused CPU may temporarily stop screen updates. GDB requires your own ARM toolchain and matching ELF symbols.", Dock = DockStyle.Fill }, 0, 0);
        var cpu = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        foreach (var action in new[] { ("Pause", "pause"), ("Resume", "start"), ("CPU registers", "cpu GetRegistersValues"), ("LCD stats", "lcd Stats") })
            cpu.Controls.Add(MainForm.ActionButton(action.Item1, (_, _) => calculator.SendDeveloperCommand(action.Item2)));
        layout.Controls.Add(cpu, 0, 1);
        var tools = new FlowLayoutPanel { Dock = DockStyle.Fill };
        tools.Controls.Add(new Label { Text = "Address (hex)", AutoSize = true }); tools.Controls.Add(address);
        tools.Controls.Add(new Label { Text = "Bytes", AutoSize = true }); tools.Controls.Add(count);
        tools.Controls.Add(MainForm.ActionButton("Read word", (_, _) => ReadWord()));
        tools.Controls.Add(MainForm.ActionButton("Dump memory", (_, _) => Dump()));
        tools.Controls.Add(new Label { Text = "GDB port", AutoSize = true }); tools.Controls.Add(gdbPort);
        tools.Controls.Add(MainForm.ActionButton("Start GDB", (_, _) =>
        {
            int port = (int)gdbPort.Value;
            if (System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port))
            { Append("Port already in use. Select another GDB port."); return; }
            calculator.SendDeveloperCommand("machine StartGdbServer " + port);
            Append("In arm-none-eabi-gdb: target remote localhost:" + port + ". Check Renode output for startup errors. Restrict access with your firewall if needed.");
        }));
        layout.Controls.Add(tools, 0, 2); layout.Controls.Add(output, 0, 3);
        var console = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        console.ColumnStyles.Add(new(SizeType.Percent, 100)); console.ColumnStyles.Add(new(SizeType.Absolute, 100));
        console.Controls.Add(command, 0, 0); console.Controls.Add(MainForm.ActionButton("Send", (_, _) => Send()), 1, 0);
        command.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { Send(); e.SuppressKeyPress = true; } };
        layout.Controls.Add(console, 0, 4); Controls.Add(layout);
    }
    uint Address()
    {
        string text = address.Text.Trim(); if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) text = text[2..];
        if (!uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint value)) throw new IOException("Enter a 32-bit hexadecimal address.");
        return value;
    }
    void ReadWord() { try { calculator.SendDeveloperCommand("sysbus ReadDoubleWord 0x" + Address().ToString("X8")); } catch (Exception ex) { Append(ex.Message); } }
    void Dump()
    {
        try
        {
            uint start = Address(); int length = (int)count.Value;
            if ((ulong)start + (uint)length > 0x100000000UL) throw new IOException("Address range exceeds 32-bit memory.");
            if (!calculator.IsRunning) { Append("Start the calculator first."); return; }
            using var file = new SaveFileDialog { Filter = "Memory dump (*.bin)|*.bin", FileName = "memory.bin" };
            if (file.ShowDialog(this) != DialogResult.OK) return;
            calculator.SendDeveloperCommand($"mem Save \"{file.FileName.Replace('\\', '/')}\" 0x{start:X8} {length}");
        }
        catch (Exception ex) { Append(ex.Message); }
    }
    void Send() { if (calculator.SendDeveloperCommand(command.Text)) command.Clear(); }
    internal void Append(string line)
    {
        if (IsDisposed) return;
        if (output.Lines.Length > 1000) output.Clear();
        output.AppendText(line + Environment.NewLine);
    }
}

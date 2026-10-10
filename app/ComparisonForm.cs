namespace EmuWorks;

internal sealed class ComparisonForm : Form
{
    readonly MainForm left, right;
    readonly ComboBox leftFirmware, rightFirmware;
    readonly string library;
    readonly Button start, stop;
    bool stopping, allowClose, starting;
    Task closeTask;
    internal bool HasProcesses => left.HasProcess || right.HasProcess;

    public ComparisonForm(string root, string assets)
    {
        library = Path.Combine(root, "firmwares");
        var entries = Directory.GetDirectories(library).Where(p => !Path.GetFileName(p).StartsWith('.')).Where(p => File.Exists(Path.Combine(p, "internal.bin")) && File.Exists(Path.Combine(p, "external.bin"))).ToArray();
        if (entries.Length == 0) throw new IOException("Import a firmware into the library first.");
        string workspace = Path.Combine(root, "comparisons", Guid.NewGuid().ToString("N"));
        left = new MainForm(Path.Combine(workspace, "left"), assets, true);
        right = new MainForm(Path.Combine(workspace, "right"), assets, true);
        while (right.ScreenPort == left.ScreenPort) right.SelectNewScreenPort();
        // These are independent calculators, with separate ROMs and ports.
        Text = "EmuWorks · firmware comparison"; ClientSize = new(1400, 920); MinimumSize = new(1100, 820);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 2, Padding = new(10) };
        layout.ColumnStyles.Add(new(SizeType.Percent, 50)); layout.ColumnStyles.Add(new(SizeType.Percent, 50));
        layout.RowStyles.Add(new(SizeType.Absolute, 52)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 65));
        leftFirmware = Selector(entries); rightFirmware = Selector(entries);
        rightFirmware.SelectedIndex = Math.Min(1, entries.Length - 1);
        layout.Controls.Add(leftFirmware, 0, 0); layout.Controls.Add(rightFirmware, 1, 0);
        Embed(left, layout, 0); Embed(right, layout, 1);
        var controls = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        start = MainForm.ActionButton("Start both", async (_, _) => await StartBoth(), true);
        stop = MainForm.ActionButton("Stop both", async (_, _) => await StopBoth());
        controls.Controls.Add(start); controls.Controls.Add(stop);
        controls.Controls.Add(MainForm.ActionButton("Open data folder", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(workspace) { UseShellExecute = true })?.Dispose()));
        controls.Controls.Add(new Label { Text = "Independent clocks and scripts. Use each keyboard separately. Data is kept in comparisons/.", Width = 420, Height = 50 });
        layout.Controls.Add(controls, 0, 2); layout.SetColumnSpan(controls, 2); Controls.Add(layout);
    }
    static ComboBox Selector(string[] entries)
    {
        var box = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList, FormattingEnabled = true };
        box.Items.AddRange(entries);
        box.Format += (_, e) => { try { var info = FirmwareInfo.Read((string)e.ListItem); e.Value = info.Name + " · " + info.Version; } catch { e.Value = Path.GetFileName((string)e.ListItem); } };
        box.SelectedIndex = 0; return box;
    }
    static void Embed(MainForm child, TableLayoutPanel layout, int column)
    {
        child.TopLevel = false; child.FormBorderStyle = FormBorderStyle.None; child.Dock = DockStyle.Fill;
        child.MinimumSize = Size.Empty; layout.Controls.Add(child, column, 1); child.Show();
    }
    async Task StartBoth()
    {
        if (starting || stopping) return;
        starting = true; start.Enabled = false;
        leftFirmware.Enabled = rightFirmware.Enabled = false;
        try
        {
            await StopBoth();
            if (stopping || IsDisposed) return;
            Install(left, (string)leftFirmware.SelectedItem); Install(right, (string)rightFirmware.SelectedItem);
            leftFirmware.Enabled = rightFirmware.Enabled = false;
            await Task.WhenAll(left.StartCalculator(), right.StartCalculator());
            if (!stopping && !IsDisposed && (!left.IsRunning || !right.IsRunning)) MessageBox.Show(this, "One or both calculators could not start. Check their Journal tabs.", "Comparison");
        }
        catch (Exception ex) { if (!stopping && !IsDisposed) MessageBox.Show(this, ex.Message, "Comparison"); }
        finally { starting = false; if (!IsDisposed && !stopping) { start.Enabled = true; if (!left.IsRunning && !right.IsRunning) leftFirmware.Enabled = rightFirmware.Enabled = true; } }
    }
    static void Install(MainForm form, string entry)
    {
        string rom = Path.Combine(form.DataDirectory, "rom");
        FirmwareStore.Install(Path.Combine(entry, "internal.bin"), Path.Combine(entry, "external.bin"), rom);
        // No scripts or storage are copied from the main calculator.
    }
    async Task StopBoth()
    {
        await Task.WhenAll(left.StopCalculator(), right.StopCalculator());
        if (!IsDisposed && !starting && !stopping) leftFirmware.Enabled = rightFirmware.Enabled = true;
    }
    internal Task StopAndClose()
    {
        if (closeTask == null || closeTask.IsCompleted && !allowClose) closeTask = StopAndCloseCore();
        return closeTask;
    }
    private async Task StopAndCloseCore()
    {
        stopping = true; start.Enabled = false; stop.Enabled = false;
        await StopBoth();
        if (left.HasProcess || right.HasProcess) { stopping = false; start.Enabled = stop.Enabled = true; return; }
        allowClose = true; Close();
    }
    protected override async void OnFormClosing(FormClosingEventArgs e)
    {
        if (allowClose) { base.OnFormClosing(e); return; }
        e.Cancel = true; await StopAndClose();
    }
}

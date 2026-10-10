using System.Text.Json;
using System.Drawing;

namespace EmuWorks;

internal sealed class FirmwareMetadataDialog : Form
{
    readonly TextBox name = new() { Width = 320, MaxLength = 80 };
    readonly TextBox version = new() { Width = 320, MaxLength = 40 };
    public string FirmwareName => name.Text.Trim();
    public string FirmwareVersion => string.IsNullOrWhiteSpace(version.Text) ? "Unknown" : version.Text.Trim();
    public FirmwareMetadataDialog(string label, string revision)
    {
        Text = "Firmware information"; ClientSize = new(370, 220); StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; MinimizeBox = false;
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), FlowDirection = FlowDirection.TopDown, WrapContents = false };
        name.Text = label; version.Text = revision;
        flow.Controls.AddRange(new Control[] { new Label { Text = "Display name", AutoSize = true }, name, new Label { Text = "Version (as supplied by the author)", AutoSize = true }, version,
            MainForm.ActionButton("Save", (_, _) => { if (FirmwareName.Length > 0) DialogResult = DialogResult.OK; }) }); Controls.Add(flow);
    }
}

internal sealed class FirmwareImportDialog : Form
{
    readonly TextBox inside = new() { Dock = DockStyle.Fill, ReadOnly = true };
    readonly TextBox outside = new() { Dock = DockStyle.Fill, ReadOnly = true };
    readonly TextBox name = new() { Dock = DockStyle.Fill, MaxLength = 80 };
    readonly TextBox version = new() { Dock = DockStyle.Fill, MaxLength = 40, PlaceholderText = "Unknown" };
    readonly Label validation = new() { Dock = DockStyle.Fill, AutoSize = false, Text = "Select both images to validate the firmware." };
    readonly Button install;
    public string InternalPath => inside.Text;
    public string ExternalPath => outside.Text;
    public FirmwareInfo Information => new() { Name = name.Text.Trim(), Version = string.IsNullOrWhiteSpace(version.Text) ? "Unknown" : version.Text.Trim(), ImportedAt = DateTime.UtcNow };
    public FirmwareImportDialog()
    {
        Text = "Import N0110 firmware"; ClientSize = new(600, 460); MinimumSize = new(550, 480); Font = new("Segoe UI", 10); BackColor = Color.White;
        StartPosition = FormStartPosition.CenterParent; MaximizeBox = false; MinimizeBox = false;
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(20), ColumnCount = 2, RowCount = 9 };
        grid.ColumnStyles.Add(new(SizeType.Percent, 100)); grid.ColumnStyles.Add(new(SizeType.Absolute, 110));
        grid.RowStyles.Add(new(SizeType.Absolute, 62));
        for (int i = 1; i <= 6; i++) grid.RowStyles.Add(new(SizeType.Absolute, i % 2 == 1 ? 25 : 42));
        grid.RowStyles.Add(new(SizeType.Percent, 100)); grid.RowStyles.Add(new(SizeType.Absolute, 48));
        var explanation = new Label { Text = "Choose internal and external binary images from the same N0110 build. They will be checked, saved in your library and installed together.", Dock = DockStyle.Fill };
        grid.Controls.Add(explanation, 0, 0); grid.SetColumnSpan(explanation, 2);
        grid.Controls.Add(new Label { Text = "1 · Internal flash image", AutoSize = true }, 0, 1);
        grid.Controls.Add(inside, 0, 2); grid.Controls.Add(MainForm.ActionButton("Browse", (_, _) => Browse(true)), 1, 2);
        grid.Controls.Add(new Label { Text = "2 · External flash image", AutoSize = true }, 0, 3);
        grid.Controls.Add(outside, 0, 4); grid.Controls.Add(MainForm.ActionButton("Browse", (_, _) => Browse(false)), 1, 4);
        grid.Controls.Add(new Label { Text = "3 · Name and version", AutoSize = true }, 0, 5);
        grid.Controls.Add(name, 0, 6); grid.Controls.Add(version, 1, 6);
        grid.Controls.Add(validation, 0, 7); grid.SetColumnSpan(validation, 2);
        install = MainForm.ActionButton("Import and install", (_, _) => DialogResult = DialogResult.OK, true); install.Enabled = false;
        grid.Controls.Add(install, 0, 8); grid.Controls.Add(MainForm.ActionButton("Cancel", (_, _) => DialogResult = DialogResult.Cancel), 1, 8);
        name.TextChanged += (_, _) => ValidatePair(); inside.TextChanged += (_, _) => ValidatePair(); outside.TextChanged += (_, _) => ValidatePair(); Controls.Add(grid);
    }
    void Browse(bool internalImage)
    {
        using var choose = new OpenFileDialog { Filter = "Binary firmware (*.bin)|*.bin", Title = internalImage ? "Choose the internal image" : "Choose the matching external image" };
        if (File.Exists(inside.Text)) choose.InitialDirectory = Path.GetDirectoryName(inside.Text);
        if (choose.ShowDialog(this) != DialogResult.OK) return;
        if (internalImage)
        {
            inside.Text = choose.FileName;
            string candidate = Path.Combine(Path.GetDirectoryName(choose.FileName)!, Path.GetFileName(choose.FileName).Replace("internal", "external", StringComparison.OrdinalIgnoreCase));
            if (candidate != choose.FileName && File.Exists(candidate)) outside.Text = candidate;
            if (name.Text.Length == 0) name.Text = Path.GetFileName(Path.GetDirectoryName(choose.FileName));
        }
        else outside.Text = choose.FileName;
        ValidatePair();
    }
    void ValidatePair()
    {
        if (install == null) return;
        install.Enabled = false;
        try
        {
            if (!File.Exists(inside.Text) || !File.Exists(outside.Text)) { validation.Text = "Select both images to validate the firmware."; return; }
            if (string.Equals(inside.Text, outside.Text, StringComparison.OrdinalIgnoreCase)) throw new IOException("Select two different images.");
            FirmwareStore.Validate(inside.Text, outside.Text);
            validation.ForeColor = Color.FromArgb(38, 120, 84);
            validation.Text = $"Valid N0110 sizes and ARM startup vector.\nInternal: {new FileInfo(inside.Text).Length:N0} bytes · External: {new FileInfo(outside.Text).Length:N0} bytes\nMatching source builds must be confirmed by the firmware supplier.";
            install.Enabled = name.Text.Trim().Length > 0;
        }
        catch (Exception ex) { validation.ForeColor = Color.Firebrick; validation.Text = ex.Message; }
    }
}

internal sealed class SettingsDialog : Form
{
    readonly ComboBox zoom = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
    readonly CheckBox updates = new() { Text = "Check for updates on launch", AutoSize = true };
    readonly DataGridView bindings = new() { Dock = DockStyle.Fill, AllowUserToAddRows = true, AllowUserToDeleteRows = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, RowHeadersVisible = false };
    readonly Dictionary<Keys, string> defaults;
    public AppSettings Value { get; private set; } = new();
    public SettingsDialog(AppSettings current, Dictionary<Keys, string> defaultBindings)
    {
        defaults = defaultBindings;
        Text = "Keyboard and display settings"; ClientSize = new(660, 590); MinimumSize = new(580, 500); StartPosition = FormStartPosition.CenterParent; Font = new("Segoe UI", 10);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(18), ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new(SizeType.Absolute, 78)); layout.RowStyles.Add(new(SizeType.Absolute, 55)); layout.RowStyles.Add(new(SizeType.Percent, 100)); layout.RowStyles.Add(new(SizeType.Absolute, 54));
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        zoom.Items.AddRange(new object[] { "Auto fit", "1×", "2×", "3×", "4×" }); zoom.SelectedIndex = current.Zoom;
        var zoomRow = new FlowLayoutPanel { AutoSize = true }; zoomRow.Controls.Add(new Label { Text = "Screen zoom", AutoSize = true, Margin = new(0, 7, 12, 0) }); zoomRow.Controls.Add(zoom);
        updates.Checked = current.CheckUpdatesOnLaunch; top.Controls.Add(zoomRow); top.Controls.Add(updates); layout.Controls.Add(top, 0, 0);
        layout.Controls.Add(new Label { Text = "Edit PC key names (for example F2, A, NumPad1). Code keeps its text input and Ctrl+C. Integer zoom fits within the available screen area.", Dock = DockStyle.Fill }, 0, 1);
        bindings.Columns.Add(new DataGridViewTextBoxColumn { Name = "PcKey", HeaderText = "PC key" });
        bindings.BackgroundColor = Color.White; bindings.BorderStyle = BorderStyle.None;
        bindings.GridColor = Color.FromArgb(231, 235, 244); bindings.RowTemplate.Height = 28;
        bindings.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(246, 248, 252);
        bindings.DefaultCellStyle.SelectionBackColor = Color.FromArgb(76, 105, 225);
        bindings.DefaultCellStyle.SelectionForeColor = Color.White;
        bindings.Columns.Add(new DataGridViewComboBoxColumn { Name = "CalculatorKey", HeaderText = "Calculator key", DataSource = AppSettings.CalculatorKeys });
        var merged = defaults.ToDictionary(p => p.Key.ToString(), p => p.Value);
        foreach (var item in current.KeyBindings) merged[item.Key] = item.Value;
        foreach (var item in merged.OrderBy(p => p.Key)) bindings.Rows.Add(item.Key, item.Value);
        layout.Controls.Add(bindings, 0, 2);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.Add(MainForm.ActionButton("Save settings", (_, _) => Save(), true));
        actions.Controls.Add(MainForm.ActionButton("Reset keyboard", (_, _) => { bindings.Rows.Clear(); foreach (var pair in defaults) bindings.Rows.Add(pair.Key.ToString(), pair.Value); }));
        actions.Controls.Add(MainForm.ActionButton("Cancel", (_, _) => DialogResult = DialogResult.Cancel)); layout.Controls.Add(actions, 0, 3); Controls.Add(layout);
    }
    void Save()
    {
        try
        {
            bindings.EndEdit(); var changes = new Dictionary<string, string>(); var seen = new HashSet<Keys>();
            foreach (DataGridViewRow row in bindings.Rows)
            {
                if (row.IsNewRow) continue;
                string text = row.Cells[0].Value?.ToString()?.Trim() ?? "", target = row.Cells[1].Value?.ToString() ?? "";
                if (!Enum.TryParse<Keys>(text, true, out var key) || (key & Keys.Modifiers) != 0 || key == Keys.None || !AppSettings.CalculatorKeys.Contains(target) || !seen.Add(key))
                    throw new IOException("Use a valid, unique PC key and select its calculator key.");
                // Keep the entire mapping, including defaults; omitted keys are disabled.
                changes[key.ToString()] = target;
            }
            Value = new() { Zoom = zoom.SelectedIndex, CheckUpdatesOnLaunch = updates.Checked, KeyBindings = changes, CustomKeyboard = true };
            Value.Validate(); DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Invalid settings"); }
    }
}

internal sealed class BackupsDialog : Form
{
    readonly ListView list = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, MultiSelect = false };
    public string SelectedBackup { get; private set; }
    public BackupsDialog(string rom)
    {
        Text = "Backup history"; ClientSize = new(660, 430); StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new(16), ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new(SizeType.Absolute, 46)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 50));
        root.Controls.Add(new Label { Text = "Restore scripts and saved settings. A backup of the current files is created before restoration. Integrity is checked before any replacement.", Dock = DockStyle.Fill }, 0, 0);
        list.Columns.Add("Created (UTC)", 285); list.Columns.Add("Firmware storage", 145); list.Columns.Add("Scripts", 90);
        string folder = Path.Combine(rom, "sauvegardes"); Directory.CreateDirectory(folder);
        foreach (string directory in Directory.GetDirectories(folder).OrderByDescending(d => d))
        {
            if (Path.GetFileName(directory).StartsWith(".")) continue;
            try
            {
                var manifest = JsonSerializer.Deserialize<ScriptStorage.Manifest>(File.ReadAllText(Path.Combine(directory, "manifest.json")));
                if (manifest == null || manifest.files == null) continue;
                var item = new ListViewItem(new[] { Path.GetFileName(directory), manifest.kind == "code" ? "EmuWorks Code" : "Ion / external", manifest.files.Count.ToString() }) { Tag = directory };
                list.Items.Add(item);
            }
            catch (Exception ex) when (ex is IOException or JsonException) { list.Items.Add(new ListViewItem(new[] { Path.GetFileName(directory), "Unreadable", "—" })); }
        }
        root.Controls.Add(list, 0, 1);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var restore = MainForm.ActionButton("Restore selected", (_, _) => { if (list.SelectedItems.Count > 0 && list.SelectedItems[0].Tag is string path) { SelectedBackup = path; DialogResult = DialogResult.OK; } }, true);
        restore.Enabled = false; list.SelectedIndexChanged += (_, _) => restore.Enabled = list.SelectedItems.Count == 1 && list.SelectedItems[0].Tag is string;
        actions.Controls.Add(restore);
        actions.Controls.Add(MainForm.ActionButton("Open folder", (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder) { UseShellExecute = true })?.Dispose()));
        actions.Controls.Add(MainForm.ActionButton("Import backup", (_, _) => { using var file = new OpenFileDialog { Filter = "Backup manifest (manifest.json)|manifest.json" }; if (file.ShowDialog(this) == DialogResult.OK) { SelectedBackup = Path.GetDirectoryName(file.FileName); DialogResult = DialogResult.OK; } }));
        root.Controls.Add(actions, 0, 2); Controls.Add(root);
    }
}

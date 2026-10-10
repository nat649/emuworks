using System.Drawing;
using System.Diagnostics;
using System.Windows.Forms;

namespace EmuWorks;

public partial class MainForm
{
    static readonly Color Accent = Color.FromArgb(76, 105, 225);
    internal static Button ActionButton(string text, EventHandler action, bool primary = false)
    {
        var button = new RoundedButton { Text = text, AutoSize = true, MinimumSize = new Size(94, 36), Padding = new Padding(12, 4, 12, 4),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.FromArgb(235, 239, 248),
            ForeColor = primary ? Color.White : Color.FromArgb(40, 49, 70), Cursor = Cursors.Hand };
        button.FlatAppearance.BorderSize = 0;
        button.Click += action;
        return button;
    }
    static Label Caption(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 14, 0, 6), ForeColor = Color.FromArgb(70, 80, 102) };
    private void BuildDashboard()
    {
        Text = "EmuWorks"; ClientSize = new Size(1140, 790); MinimumSize = new Size(1100, 780);
        Font = new Font("Segoe UI", 10); BackColor = Color.FromArgb(245, 247, 252);
        StartPosition = FormStartPosition.CenterScreen; KeyPreview = true;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(16) };
        root.RowStyles.Add(new(SizeType.Absolute, 72)); root.RowStyles.Add(new(SizeType.Percent, 100)); root.RowStyles.Add(new(SizeType.Absolute, 62));
        var header = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        header.ColumnStyles.Add(new(SizeType.Percent, 100)); header.ColumnStyles.Add(new(SizeType.AutoSize));
        var brand = new Label { Text = "EmuWorks", Font = new Font("Segoe UI", 24, FontStyle.Bold), AutoSize = true, ForeColor = Color.FromArgb(32, 43, 68) };
        header.Controls.Add(brand, 0, 0);
        var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false };
        importButton = ActionButton("Import firmware", OnImportFirmware, true);
        settingsButton = ActionButton("Settings", (_, _) => EditSettings());
        updatesButton = ActionButton("Updates", async (_, _) => await CheckUpdates(true));
        actions.Controls.AddRange(new Control[] { importButton, settingsButton, updatesButton }); header.Controls.Add(actions, 1, 0);
        var body = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        body.ColumnStyles.Add(new(SizeType.Absolute, 338)); body.ColumnStyles.Add(new(SizeType.Percent, 100));
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 9), Margin = new Padding(0, 0, 16, 0) };
        var library = new TabPage("Library") { BackColor = Color.White, Padding = new Padding(16) };
        var libraryContent = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        libraryContent.Controls.Add(Caption("FIRMWARE"));
        firmwareBox = new ComboBox { Width = 276, DropDownStyle = ComboBoxStyle.DropDownList, Margin = new Padding(0, 0, 0, 12) };
        firmwareBox.FormattingEnabled = true;
        firmwareBox.Format += (_, args) =>
        {
            try { var info = FirmwareInfo.Read(Path.Combine(FirmwaresDir, args.ListItem.ToString())); args.Value = info.Name + (info.Version == "Unknown" ? "" : " · " + info.Version); }
            catch (Exception) { args.Value = args.ListItem; }
        };
        firmwareBox.SelectedIndexChanged += (_, _) => RefreshFirmwareDetails();
        libraryContent.Controls.Add(firmwareBox);
        firmwareDetails = new Label { Width = 276, Height = 160, ForeColor = Color.FromArgb(76, 85, 108), Padding = new Padding(0, 8, 0, 0) };
        libraryContent.Controls.Add(firmwareDetails);
        installButton = ActionButton("Use selected firmware", OnInstallFirmware, true); libraryContent.Controls.Add(installButton);
        libraryContent.Controls.Add(ActionButton("Edit name / version", (_, _) => EditFirmwareInfo()));
        libraryContent.Controls.Add(ActionButton("Open library folder", (_, _) => OpenInShell(FirmwaresDir)));
        libraryContent.Controls.Add(Caption("SERIAL NUMBER"));
        serieBox = new TextBox { Width = 276, MaxLength = 64, PlaceholderText = "EmuWorks" };
        serieBox.Leave += (_, _) => EnregistrerSerie(); libraryContent.Controls.Add(serieBox);
        libraryContent.Controls.Add(new Label { Text = "Custom serial for compatible external firmware.", Width = 276, Height = 55, ForeColor = Color.Gray, Margin = new Padding(0, 10, 0, 0) });
        library.Controls.Add(libraryContent);
        var scripts = new TabPage("Scripts") { BackColor = Color.White, Padding = new Padding(12) };
        scriptsGroup = new GroupBox { Text = "Python scripts", Dock = DockStyle.Fill, Padding = new Padding(10) };
        var scriptLayout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1 };
        scriptLayout.RowStyles.Add(new(SizeType.Percent, 100)); scriptLayout.RowStyles.Add(new(SizeType.Absolute, 46)); scriptLayout.RowStyles.Add(new(SizeType.Absolute, 92));
        scriptList = new ListBox { Dock = DockStyle.Fill, IntegralHeight = false, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(247, 249, 253) };
        scriptList.DoubleClick += OnOpenScript; scriptLayout.Controls.Add(scriptList, 0, 0);
        var scriptActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        addButton = ActionButton("Add", OnAddScript); removeButton = ActionButton("Remove", OnRemoveScript);
        folderButton = ActionButton("Folder", (_, _) => OpenInShell(ScriptsDir));
        scriptActions.Controls.AddRange(new Control[] { addButton, removeButton, folderButton }); scriptLayout.Controls.Add(scriptActions, 0, 1);
        var backupActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        backupsButton = ActionButton("Create backup", async (_, _) => await CreateBackup());
        restoreButton = ActionButton("Backup history / restore", OnRestoreScripts);
        backupActions.Controls.AddRange(new Control[] { backupsButton, restoreButton }); scriptLayout.Controls.Add(backupActions, 0, 2);
        scriptsGroup.Controls.Add(scriptLayout); scripts.Controls.Add(scriptsGroup);
        var journal = new TabPage("Journal") { BackColor = Color.White, Padding = new Padding(10) };
        logBox = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            Font = new Font("Consolas", 9), BackColor = Color.FromArgb(247, 249, 253), TabStop = false };
        journal.Controls.Add(logBox); tabs.TabPages.AddRange(new[] { library, scripts, journal });
        body.Controls.Add(tabs, 0, 0);
        ecran = new EcranPanel { Dock = DockStyle.Fill, Margin = new Padding(0) }; body.Controls.Add(ecran, 1, 0);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Padding = new Padding(0, 10, 0, 0) };
        footer.ColumnStyles.Add(new(SizeType.Absolute, 250)); footer.ColumnStyles.Add(new(SizeType.Percent, 100)); footer.ColumnStyles.Add(new(SizeType.AutoSize));
        startButton = ActionButton("Start calculator", OnStartStop, true); startButton.Width = 230; footer.Controls.Add(startButton, 0, 0);
        statusLabel = new Label { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Color.FromArgb(95, 105, 125), AutoEllipsis = true }; footer.Controls.Add(statusLabel, 1, 0);
        footer.Controls.Add(new Label { Text = "EmuWorks " + UpdateService.Current.ToString(3), AutoSize = true, ForeColor = Color.Gray, Margin = new Padding(8, 12, 0, 0) }, 2, 0);
        root.Controls.Add(header, 0, 0); root.Controls.Add(body, 0, 1); root.Controls.Add(footer, 0, 2); Controls.Add(root);
    }
    private void RefreshFirmwareDetails()
    {
        if (firmwareBox.SelectedItem is not string folder) { firmwareDetails.Text = "Import a matching N0110 image pair to get started."; return; }
        selectedFirmwareDirectory = Path.Combine(FirmwaresDir, folder);
        try
        {
            var info = FirmwareInfo.Read(selectedFirmwareDirectory);
            string inside = Path.Combine(selectedFirmwareDirectory, "internal.bin"), outside = Path.Combine(selectedFirmwareDirectory, "external.bin");
            FirmwareStore.Validate(inside, outside);
            bool installed = FirmwareStore.Same(inside, Path.Combine(RomDir, "internal.bin")) && FirmwareStore.Same(outside, Path.Combine(RomDir, "external.bin"));
            firmwareDetails.Text = $"{info.Name}\nVersion: {info.Version}\n{info.Source}\n\n{(installed ? "Installed" : "Ready to install")} · N0110\nInternal: {new FileInfo(inside).Length:N0} bytes\nExternal: {new FileInfo(outside).Length:N0} bytes";
        }
        catch (Exception ex) { firmwareDetails.Text = "Unavailable: " + ex.Message; }
    }
    private void EditFirmwareInfo()
    {
        if (session != null || firmwareBox.SelectedItem is not string folder) return;
        try
        {
            string directory = Path.Combine(FirmwaresDir, folder); var info = FirmwareInfo.Read(directory);
            using var dialog = new FirmwareMetadataDialog(info.Name, info.Version);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            using var lease = AcquireLease(); info.Name = dialog.FirmwareName; info.Version = dialog.FirmwareVersion; info.Save(directory); RefreshFirmwareDetails();
        }
        catch (Exception ex) { Log("Firmware information: " + ex.Message); }
    }
    private void ApplySettings() { ecran.Zoom = settings.Zoom; ecran.Invalidate(); }
    private bool editingSettings;
    private void EditSettings()
    {
        if (closing || editingSettings) return;
        editingSettings = true;
        try
        {
            // Release all held keys before giving the dialog keyboard focus.
            foreach (var key in touchesEnfoncees.ToList()) Relacher(key);
            using var dialog = new SettingsDialog(settings, Touches);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            // A running session already owns the exclusive ROM lease.
            using var lease = sessionLease == null ? AcquireLease() : null;
            dialog.Value.Save(baseDir);
            settings = dialog.Value;
            ApplySettings();
        }
        catch (Exception ex)
        {
            Log("Settings: " + ex.Message);
            if (!IsDisposed && !Disposing) MessageBox.Show(this, ex.Message, "Settings", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { editingSettings = false; }
    }
    private async Task CreateBackup()
    {
        await lifecycle.WaitAsync();
        try
        {
            if (session != null || closing) return;
            using var lease = AcquireLease(); EnregistrerSerie(); settings.Save(baseDir);
            string backup = ScriptStorage.Backup(RomDir); Log("Backup created: " + backup);
        }
        catch (Exception ex) { Log("Backup failed: " + ex.Message); }
        finally { lifecycle.Release(); }
    }
    private async Task CheckUpdates(bool manual)
    {
        updatesButton.Enabled = false;
        try
        {
            var latest = await UpdateService.Latest();
            if (IsDisposed || Disposing) return;
            if (latest.Version > UpdateService.Current)
            {
                if (MessageBox.Show(this, $"EmuWorks {latest.Version} is available. Open its download page?\n\nPreserve your rom and firmwares folders when updating.", "Update available", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
                    OpenInShell(latest.Page);
            }
            else if (manual) MessageBox.Show(this, "No newer release is available.\nInstalled: " + UpdateService.Current.ToString(3) + "\nLatest release: " + latest.Version, "EmuWorks updates", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) { Log("Update check: " + ex.Message); if (manual && !IsDisposed) MessageBox.Show(this, "Could not check for updates. Check your internet connection and try again.", "EmuWorks updates"); }
        finally { if (!IsDisposed && !Disposing) updatesButton.Enabled = true; }
    }
    private bool TryBinding(Keys key, out string name)
    {
        if (settings.KeyBindings.TryGetValue(key.ToString(), out name)) return true;
        if (settings.CustomKeyboard) { name = null; return false; }
        return Touches.TryGetValue(key, out name);
    }
}

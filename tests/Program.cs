using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using EmuWorks;

internal static partial class Tests
{
    static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    static string root;
    static int failures;
    static object Field(MainForm form, string name) => typeof(MainForm).GetField(name, Hidden).GetValue(form);
    static Task Call(MainForm form, string name) => (Task)typeof(MainForm).GetMethod(name, Hidden).Invoke(form, null);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Contains("--updates-only"))
        {
            var latest = UpdateService.Latest().GetAwaiter().GetResult();
            Console.WriteLine("PASS update lookup: " + latest.Version + " " + latest.Page); return;
        }
        if (args.Contains("--ui-preview"))
        {
            ApplicationConfiguration.Initialize();
            string output = args[^1]; Directory.CreateDirectory(output);
            using var import = new FirmwareImportDialog();
            using var preferences = new SettingsDialog(new(), new() { [Keys.Enter] = "EXE", [Keys.F2] = "HOME" });
            foreach (var preview in new[] { (Form: (Form)import, Name: "import.png"), (Form: (Form)preferences, Name: "settings.png") })
            {
                preview.Form.ShowInTaskbar = false; preview.Form.Opacity = 0; preview.Form.Show(); Application.DoEvents();
                using var bitmap = new Bitmap(preview.Form.Width, preview.Form.Height);
                preview.Form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, preview.Form.Size)); bitmap.Save(Path.Combine(output, preview.Name)); preview.Form.Hide();
            }
            return;
        }
        if (args.Length > 0 && args[0] == "--storage")
        {
            try { ScriptStorage.Execute(args.Skip(1).ToArray()); }
            catch (Exception ex) { Console.Error.WriteLine(ex.Message); Environment.ExitCode = 1; }
            return;
        }
        if (args.Contains("--console")) { FakeRenode().GetAwaiter().GetResult(); return; }
        root = Path.Combine(Path.GetTempPath(), "EmuWorksTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            FirmwareTests();
            if (args.Contains("--firmware-only")) return;
            string rom = Path.Combine(root, "rom"), tools = Path.Combine(root, "renode", "tools");
            Directory.CreateDirectory(Path.Combine(rom, "scripts")); Directory.CreateDirectory(tools);
            File.WriteAllText(Path.Combine(rom, "scripts", "keep.py"), "print('keep')\n");
            File.WriteAllText(Path.Combine(rom, "sram.bin"), "stale dump must never be imported");
            MakeFirmware(rom, 0x08000009);
            string repo = Path.GetFullPath(args[0]);
            foreach (string name in new[] { "rom.js", "storage.js" }) File.Copy(Path.Combine(repo, "renode", "tools", name), Path.Combine(tools, name));
            Environment.SetEnvironmentVariable("EMUWORKS_BASE", root);
            Environment.SetEnvironmentVariable("RENODE_EXE", Environment.ProcessPath);
            ApplicationConfiguration.Initialize();
            using var form = new MainForm { ShowInTaskbar = false, Opacity = 0 };
            // Isolate the fake screen server from a calculator the user may have running.
            var portProbe = new TcpListener(IPAddress.Loopback, 0);
            portProbe.Start();
            int testPort = ((IPEndPoint)portProbe.LocalEndpoint).Port;
            portProbe.Stop();
            typeof(MainForm).GetField("PortEcran", Hidden).SetValue(form, testPort);
            Environment.SetEnvironmentVariable("EMUWORKS_TEST_PORT", testPort.ToString());
            using var host = new Form { ShowInTaskbar = false, Opacity = 0 };
            host.Shown += (_, _) => form.Show();
            form.Shown += async (_, _) =>
            {
                try
                {
                    await Case("deuxieme instance refusee pour la meme ROM", async () =>
                    {
                        using var lease = new FileStream(Path.Combine(rom, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                        await Call(form, "Demarrer");
                        Check(Field(form, "renode") == null && Field(form, "session") == null, "Verrou ignore");
                    });
                    await Case("annulation pendant le demarrage", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "silent");
                        var start = Call(form, "Demarrer");
                        await Task.Delay(400);
                        await Call(form, "Arreter").WaitAsync(TimeSpan.FromSeconds(12));
                        await start;
                        Check(Field(form, "renode") == null && Field(form, "session") == null, "Session restante");
                        Check(File.ReadAllText(Path.Combine(rom, "scripts/keep.py")).Contains("keep"), "Scripts ecrases");
                    });
                    await Case("external firmware works without Node.js and preserves scripts", async () =>
                    {
                        string previousPath = Environment.GetEnvironmentVariable("PATH");
                        Environment.SetEnvironmentVariable("PATH", "");
                        try
                        {
                            Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                            await Call(form, "Demarrer");
                            Check((bool)Field(form, "enMarche"), "Native script backup failed without Node.js");
                            await Call(form, "Arreter");
                            Check(File.ReadAllText(Path.Combine(rom, "scripts/keep.py")).Contains("keep"), "Native backup changed user scripts");
                        }
                        finally { Environment.SetEnvironmentVariable("PATH", previousPath); }
                    });
                    await Case("deux cycles marche/arret et processus libere", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                        for (int i = 0; i < 2; i++)
                        {
                            await Call(form, "Demarrer").WaitAsync(TimeSpan.FromSeconds(15));
                            Check((bool)Field(form, "enMarche"), "Demarrage rate");
                            int pid = ((Process)Field(form, "renode")).Id;
                            await Task.Delay(150);
                            await Call(form, "Arreter").WaitAsync(TimeSpan.FromSeconds(12));
                            Check(Field(form, "renode") == null, "Processus non dispose");
                            Check(!Process.GetProcesses().Any(p => { using(p) return p.Id == pid; }), "Processus encore vivant");
                        }
                    });
                    await Case("settings open and save while the calculator is running", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                        await Call(form, "Demarrer");
                        try
                        {
                            Check(((Button)Field(form, "settingsButton")).Enabled, "Settings button disabled during emulation");
                            bool opened = false;
                            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
                            timer.Tick += (_, _) =>
                            {
                                var dialog = Application.OpenForms.OfType<SettingsDialog>().FirstOrDefault();
                                if (dialog == null) return;
                                timer.Stop(); opened = true;
                                Check((bool)Field(form, "editingSettings"), "Calculator input was not suspended");
                                var zoom = (ComboBox)typeof(SettingsDialog).GetField("zoom", Hidden).GetValue(dialog);
                                zoom.SelectedIndex = 2;
                                typeof(SettingsDialog).GetMethod("Save", Hidden).Invoke(dialog, null);
                            };
                            timer.Start();
                            ((Button)Field(form, "settingsButton")).PerformClick();
                            Check(opened, "Settings dialog did not open");
                            Check(AppSettings.Load(root).Zoom == 2, "Live settings were not saved");
                            Check(((EcranPanel)Field(form, "ecran")).Zoom == 2, "Live zoom was not applied");
                            Check((bool)Field(form, "enMarche") && !(bool)Field(form, "editingSettings"), "Settings interrupted the session or retained input focus");
                        }
                        finally { await Call(form, "Arreter"); }
                    });
                    await Case("custom keyboard and zoom persist and control the calculator", async () =>
                    {
                        string commands = Path.Combine(root, "custom-keyboard.log");
                        var custom = new AppSettings { Zoom = 1, CustomKeyboard = true, KeyBindings = new() { ["F2"] = "EXE" } };
                        custom.Save(root); Check(AppSettings.Load(root).KeyBindings["F2"] == "EXE", "Keyboard settings were not persisted");
                        typeof(MainForm).GetField("settings", Hidden).SetValue(form, custom);
                        typeof(MainForm).GetMethod("ApplySettings", Hidden).Invoke(form, null);
                        Check(((EcranPanel)Field(form, "ecran")).Zoom == 1, "Screen zoom was not applied");
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_COMMANDS", commands);
                        try
                        {
                            Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                            await Call(form, "Demarrer");
                            Check((bool)typeof(MainForm).GetMethod("Enfoncer", Hidden).Invoke(form, new object[] { Keys.F2 }), "Custom key was ignored");
                            Check(!(bool)typeof(MainForm).GetMethod("Enfoncer", Hidden).Invoke(form, new object[] { Keys.A }), "Removed binding remained active");
                            typeof(MainForm).GetMethod("Relacher", Hidden).Invoke(form, new object[] { "EXE" });
                            await Call(form, "Arreter");
                            string sent = File.ReadAllText(commands);
                            Check(sent.Contains("keyboard PressKey \"EXE\"") && sent.Contains("keyboard ReleaseKey \"EXE\""), "Custom key commands were not sent");
                        }
                        finally { Environment.SetEnvironmentVariable("EMUWORKS_TEST_COMMANDS", null); typeof(MainForm).GetField("settings", Hidden).SetValue(form, new AppSettings()); }
                    });
                    await Case("fermeture socket detectee et retour a l'arret", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "drop");
                        await Call(form, "Demarrer").WaitAsync(TimeSpan.FromSeconds(15));
                        var deadline = DateTime.UtcNow.AddSeconds(12);
                        while (Field(form, "session") != null && DateTime.UtcNow < deadline) await Task.Delay(50);
                        Check(Field(form, "session") == null, "La deconnexion n'a pas arrete la session");
                    });
                    await Case("arret force du seul processus possede", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "ignore-quit");
                        await Call(form, "Demarrer");
                        Check((bool)Field(form, "enMarche"), "Demarrage rate");
                        await Call(form, "Arreter").WaitAsync(TimeSpan.FromSeconds(18));
                        Check(Field(form, "renode") == null, "Processus bloque non libere");
                    });
                    await Case("Core integre demarre sans Node et conserve les scripts", async () =>
                    {
                        string library = Path.Combine(root, "firmwares", CoreFirmware.LibraryName);
                        FirmwareStore.Install(Path.Combine(library, "internal.bin"), Path.Combine(library, "external.bin"), rom);
                        string oldPath = Environment.GetEnvironmentVariable("PATH");
                        try
                        {
                            Environment.SetEnvironmentVariable("PATH", "");
                            Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                            await Call(form, "Demarrer").WaitAsync(TimeSpan.FromSeconds(15));
                            Check((bool)Field(form, "enMarche") && (bool)Field(form, "coreSession"), "Core non reconnu");
                            await Call(form, "Arreter");
                            Check(File.ReadAllText(Path.Combine(rom, "scripts/keep.py")).Contains("keep"), "Scripts touches par Core");
                        }
                        finally { Environment.SetEnvironmentVariable("PATH", oldPath); MakeFirmware(rom, 0x08000009); }
                    });
                    await Case("Code starts without Node, routes text and preserves independent scripts", async () =>
                    {
                        string library = Path.Combine(root, "firmwares", CodeFirmware.LibraryName);
                        FirmwareStore.Install(Path.Combine(library, "internal.bin"), Path.Combine(library, "external.bin"), rom);
                        string codePath = Path.Combine(rom, "code-scripts", "hello.py");
                        string original = File.ReadAllText(codePath);
                        string commandLog = Path.Combine(root, "code-commands.txt");
                        string oldPath = Environment.GetEnvironmentVariable("PATH");
                        try
                        {
                            Environment.SetEnvironmentVariable("PATH", "");
                            Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "normal");
                            Environment.SetEnvironmentVariable("EMUWORKS_TEST_COMMANDS", commandLog);
                            await Call(form, "Demarrer").WaitAsync(TimeSpan.FromSeconds(15));
                            Check((bool)Field(form, "enMarche") && (bool)Field(form, "codeSession"), "Code was not recognized");
                            Check(((Process)Field(form, "renode")).StartInfo.Arguments.Contains("emuworks-code.resc"), "Wrong startup script");
                            typeof(MainForm).GetMethod("OnKeyPress", Hidden).Invoke(form, new object[] { new KeyPressEventArgs('p') });
                            object[] args = { new Message(), Keys.Control | Keys.C };
                            Check((bool)typeof(MainForm).GetMethod("ProcessCmdKey", Hidden).Invoke(form, args), "Ctrl+C was not handled");
                            await Call(form, "Arreter");
                            string log = File.ReadAllText(commandLog);
                            Check(log.Contains("keyboard TypeHex \"70\"") && log.Contains("keyboard TypeHex \"03\""), "Text or interrupt packet missing");
                            Check(!log.Contains("SaveSram"), "Code used Ion storage synchronization");
                            Check(File.ReadAllText(codePath) == original && File.ReadAllText(Path.Combine(rom, "scripts/keep.py")).Contains("keep"), "Scripts were changed");
                            Check(File.Exists(Path.Combine(library, "licenses", "MicroPython.txt")), "Embedded dependency notices missing");
                        }
                        finally { Environment.SetEnvironmentVariable("PATH", oldPath); Environment.SetEnvironmentVariable("EMUWORKS_TEST_COMMANDS", null); MakeFirmware(rom, 0x08000009); }
                    });
                    await Case("fermeture de fenetre pendant connexion", async () =>
                    {
                        Environment.SetEnvironmentVariable("EMUWORKS_TEST_MODE", "silent");
                        var start = Call(form, "Demarrer");
                        await Task.Delay(400);
                        form.Close();
                        await start;
                        await Task.Delay(100);
                        Check(Field(form, "renode") == null, "Processus orphelin apres fermeture");
                    });
                }
                finally { if (!form.IsDisposed) form.Close(); host.Close(); }
            };
            Application.Run(host);
        }
        catch (Exception e) { failures++; Console.WriteLine(e); }
        finally
        {
            // Uniquement le dossier unique cree par ce test.
            if (Path.GetFileName(root).StartsWith("EmuWorksTests-") && Path.GetDirectoryName(root).Equals(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                Directory.Delete(root, true);
        }
        Console.WriteLine("Echecs : " + failures);
        Environment.ExitCode = failures == 0 ? 0 : 1;
    }

    static async Task Case(string name, Func<Task> test)
    {
        try { await test(); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { failures++; Console.WriteLine("FAIL " + name + ": " + ex); }
    }

    static void MakeFirmware(string dir, uint reset)
    {
        Directory.CreateDirectory(dir);
        byte[] b = new byte[64];
        BitConverter.GetBytes(0x20040000u).CopyTo(b, 0); BitConverter.GetBytes(reset).CopyTo(b, 4);
        File.WriteAllBytes(Path.Combine(dir, "internal.bin"), b);
        File.WriteAllBytes(Path.Combine(dir, "external.bin"), new byte[] {1,2,3,4});
    }
    static void FirmwareTests()
    {
        NativeStorageTests();
        string fresh = Path.Combine(root, "fresh");
        CoreFirmware.PrepareLibrary(fresh);
        string freshRom = Path.Combine(fresh, "rom");
        Check(CoreFirmware.IsCore(Path.Combine(freshRom, "internal.bin")), "Firmware integre absent sur installation neuve");
        FirmwareStore.Validate(Path.Combine(freshRom, "internal.bin"), Path.Combine(freshRom, "external.bin"));
        MakeFirmware(freshRom, 0x08000009);
        CoreFirmware.PrepareLibrary(fresh);
        Check(!CoreFirmware.IsCore(Path.Combine(freshRom, "internal.bin")), "Firmware utilisateur remplace par Core");
        Console.WriteLine("PASS installation Core neuve et preservation firmware existant");
        CodeFirmware.PrepareLibrary(fresh);
        string codeLibrary = Path.Combine(fresh, "firmwares", CodeFirmware.LibraryName);
        Check(CodeFirmware.IsCode(Path.Combine(codeLibrary, "internal.bin")), "Code identity missing");
        Check(!CodeFirmware.IsCode(Path.Combine(freshRom, "internal.bin")), "Code replaced active firmware");
        FirmwareStore.Validate(Path.Combine(codeLibrary, "internal.bin"), Path.Combine(codeLibrary, "external.bin"));
        string codeScripts = Path.Combine(freshRom, "code-scripts");
        File.WriteAllText(Path.Combine(codeScripts, "hello.py"), "print('preserve me')\n");
        CodeFirmware.PrepareLibrary(fresh);
        Check(File.ReadAllText(Path.Combine(codeScripts, "hello.py")).Contains("preserve me"), "User script overwritten");
        CodeFirmware.PackScripts(freshRom);
        byte[] pack = File.ReadAllBytes(Path.Combine(freshRom, "code-scripts.bin"));
        Check(BitConverter.ToUInt32(pack, 0) == 0x45574353 && BitConverter.ToInt32(pack, 4) == 1, "Invalid script pack header");
        int sourceOffset = (int)BitConverter.ToUInt32(pack, 40);
        int sourceLength = BitConverter.ToInt32(pack, 44);
        Check(System.Text.Encoding.UTF8.GetString(pack, sourceOffset, sourceLength) == "print('preserve me')\n", "Script pack payload changed");
        File.WriteAllText(Path.Combine(codeScripts, "bad-name.py"), "pass");
        try { CodeFirmware.PackScripts(freshRom); throw new Exception("Invalid module name accepted"); }
        catch (IOException) { }
        Check(pack.SequenceEqual(File.ReadAllBytes(Path.Combine(freshRom, "code-scripts.bin"))), "Rejected pack replaced the valid one");
        File.Delete(Path.Combine(codeScripts, "bad-name.py"));
        File.WriteAllText(Path.Combine(codeScripts, "large.py"), new string('x', 16385));
        try { CodeFirmware.PackScripts(freshRom); throw new Exception("Oversized script accepted"); }
        catch (IOException) { }
        Console.WriteLine("PASS Code installation, firmware preservation, script packing and validation");
        string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
        MakeFirmware(a, 0x08000009); MakeFirmware(b, 0x00200009);
        string library = Path.Combine(root, "import-library");
        string imported = FirmwareStore.ImportToLibrary(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), library);
        Check(FirmwareStore.Same(Path.Combine(imported, "external.bin"), Path.Combine(a, "external.bin")), "Library import lost an image");
        Check(FirmwareStore.ImportToLibrary(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), library) == imported, "Duplicate import created a new entry");
        string named = Path.Combine(library, "custom-firmware");
        Directory.Move(imported, named);
        Check(FirmwareStore.ImportToLibrary(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), library) == named, "Existing named firmware was duplicated");
        File.Delete(Path.Combine(a, "internal.bin"));
        FirmwareStore.Validate(Path.Combine(named, "internal.bin"), Path.Combine(named, "external.bin"));
        MakeFirmware(a, 0x08000009);
        Console.WriteLine("PASS persistent firmware library import, source independence and deduplication");
        FirmwareStore.Install(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), b);
        Check(FirmwareStore.Same(Path.Combine(a,"internal.bin"), Path.Combine(b,"internal.bin")), "Import invalide");
        byte[] old = File.ReadAllBytes(Path.Combine(b,"internal.bin"));
        File.WriteAllBytes(Path.Combine(a,"external.bin"), Array.Empty<byte>());
        try { FirmwareStore.ImportToLibrary(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), library); throw new Exception("Invalid library image accepted"); }
        catch (IOException) { }
        Check(Directory.GetDirectories(library).Length == 1, "Rejected import left a library entry");
        try { FirmwareStore.Install(Path.Combine(a,"internal.bin"), Path.Combine(a,"external.bin"), b); throw new Exception("Image vide acceptee"); }
        catch (IOException) { }
        Check(old.SequenceEqual(File.ReadAllBytes(Path.Combine(b,"internal.bin"))), "Image precedente modifiee");
        // Simule une interruption apres remplacement de la premiere image.
        string pending = Path.Combine(b, ".firmware-pending");
        File.Copy(Path.Combine(b,"internal.bin"), Path.Combine(pending,"old-internal.bin"), true);
        File.Copy(Path.Combine(b,"external.bin"), Path.Combine(pending,"old-external.bin"), true);
        File.WriteAllText(Path.Combine(pending,"pending.json"), "[true,true]");
        File.WriteAllBytes(Path.Combine(b,"internal.bin"), new byte[5]);
        FirmwareStore.Recover(b);
        Check(old.SequenceEqual(File.ReadAllBytes(Path.Combine(b,"internal.bin"))), "Recuperation ratee");
        Console.WriteLine("PASS import firmware, rejet image vide, reprise apres interruption");
    }

    static async Task FakeRenode()
    {
        string mode = Environment.GetEnvironmentVariable("EMUWORKS_TEST_MODE");
        using var cancel = new CancellationTokenSource();
        Task input = Task.Run(async () =>
        {
            while (await Console.In.ReadLineAsync() is string line)
            {
                string commandLog = Environment.GetEnvironmentVariable("EMUWORKS_TEST_COMMANDS");
                if (commandLog != null) File.AppendAllText(commandLog, line + "\n");
                if (line == "quit" && mode != "ignore-quit") { cancel.Cancel(); return; }
            }
        });
        if (mode == "silent") { await input; return; }
        var listener = new TcpListener(IPAddress.Loopback, int.Parse(Environment.GetEnvironmentVariable("EMUWORKS_TEST_PORT")));
        listener.Start();
        try
        {
            using var client = await listener.AcceptTcpClientAsync(cancel.Token);
            var stream = client.GetStream();
            byte[] request = new byte[1], frame = new byte[320*240*2];
            int count = 0;
            while (await stream.ReadAsync(request, cancel.Token) > 0)
            {
                if (mode == "drop" && count++ == 2) break;
                await stream.WriteAsync(frame, cancel.Token);
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally { listener.Stop(); }
        await input;
    }
}

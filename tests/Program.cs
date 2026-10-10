using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using EmuWorks;

internal static class Tests
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
        if (args.Contains("--console")) { FakeRenode().GetAwaiter().GetResult(); return; }
        root = Path.Combine(Path.GetTempPath(), "EmuWorksTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            FirmwareTests();
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
        string fresh = Path.Combine(root, "fresh");
        CoreFirmware.PrepareLibrary(fresh);
        string freshRom = Path.Combine(fresh, "rom");
        Check(CoreFirmware.IsCore(Path.Combine(freshRom, "internal.bin")), "Firmware integre absent sur installation neuve");
        FirmwareStore.Validate(Path.Combine(freshRom, "internal.bin"), Path.Combine(freshRom, "external.bin"));
        MakeFirmware(freshRom, 0x08000009);
        CoreFirmware.PrepareLibrary(fresh);
        Check(!CoreFirmware.IsCore(Path.Combine(freshRom, "internal.bin")), "Firmware utilisateur remplace par Core");
        Console.WriteLine("PASS installation Core neuve et preservation firmware existant");
        string a = Path.Combine(root, "a"), b = Path.Combine(root, "b");
        MakeFirmware(a, 0x08000009); MakeFirmware(b, 0x00200009);
        FirmwareStore.Install(Path.Combine(a, "internal.bin"), Path.Combine(a, "external.bin"), b);
        Check(FirmwareStore.Same(Path.Combine(a,"internal.bin"), Path.Combine(b,"internal.bin")), "Import invalide");
        byte[] old = File.ReadAllBytes(Path.Combine(b,"internal.bin"));
        File.WriteAllBytes(Path.Combine(a,"external.bin"), Array.Empty<byte>());
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
                if (line == "quit" && mode != "ignore-quit") { cancel.Cancel(); return; }
        });
        if (mode == "silent") { await input; return; }
        var listener = new TcpListener(IPAddress.Loopback, 3555);
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

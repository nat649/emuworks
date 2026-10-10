using EmuWorks;

internal static partial class Tests
{
    static void RealToolsTests(string assets, string renode, string firmware)
    {
        // User-provided firmware is only copied into an isolated temporary calculator.
        Environment.SetEnvironmentVariable("RENODE_EXE", renode);
        ApplicationConfiguration.Initialize();
        string directory = Path.Combine(Path.GetTempPath(), "EmuWorksRealTools-" + Guid.NewGuid().ToString("N"));
        using var host = new Form { ShowInTaskbar = false, Opacity = 0 };
        host.Shown += async (_, _) =>
        {
            using var core = new MainForm(Path.Combine(directory, "core"), assets, true) { ShowInTaskbar = false, Opacity = 0 };
            using var epsilon = new MainForm(Path.Combine(directory, "epsilon"), assets, true) { ShowInTaskbar = false, Opacity = 0 };
            core.Show(); epsilon.Show();
            try
            {
                string rom = Path.Combine(epsilon.DataDirectory, "rom");
                FirmwareStore.Install(Path.Combine(firmware, "internal.bin"), Path.Combine(firmware, "external.bin"), rom);
                await Task.WhenAll(core.StartCalculator(), epsilon.StartCalculator());
                Check(core.IsRunning && epsilon.IsRunning, "Real parallel boot failed: " + ((TextBox)Field(core, "logBox")).Text + "\n" + ((TextBox)Field(epsilon, "logBox")).Text);
                Console.WriteLine("PASS real Core/Epsilon parallel boot with independent ROMs and screen ports");
                epsilon.SendDeveloperCommand("adc SetMillivolts 3650");
                epsilon.SendDeveloperCommand("adc Millivolts");
                epsilon.SendDeveloperCommand("cpu GetRegistersValues");
                epsilon.SendDeveloperCommand("sysbus ReadDoubleWord 0x20000000");
                epsilon.SendDeveloperCommand("lcd Stats");
                string dump = Path.Combine(directory, "memory.bin");
                epsilon.SendDeveloperCommand("mem Save \"" + dump.Replace('\\', '/') + "\" 0x20000000 256");
                foreach (string key in new[] { "SEVEN", "PLUS", "TWO", "EXE" }) { core.VirtualKey(key); await Task.Delay(200); }
                await Task.Delay(1200);
                string journal = ((TextBox)Field(epsilon, "logBox")).Text;
                Check(File.Exists(dump) && new FileInfo(dump).Length == 256, "Real memory dump failed: " + journal);
                Check(journal.Contains("0x00000E42") && journal.Contains("PC / R15") && journal.Contains("SP / R13") && journal.Contains("RAMWR=") && !journal.Contains("There was an error executing command") && !journal.Contains("Could not find"), "Developer command failed: " + journal);
                Console.WriteLine("PASS real ADC battery commands, register inspection, memory dump and virtual key dispatch");
                string raw = Path.Combine(directory, "core-screen.raw");
                core.SendDeveloperCommand("lcd Dump \"" + raw.Replace('\\', '/') + "\"");
                var deadline = DateTime.UtcNow.AddSeconds(10);
                while (!File.Exists(raw) && DateTime.UtcNow < deadline) await Task.Delay(100);
                Check(File.Exists(raw) && new FileInfo(raw).Length == 320 * 240 * 3, "Core screen capture missing: " + ((TextBox)Field(core, "logBox")).Text);
                int port = MainForm.ReserveScreenPort();
                Check(epsilon.SendDeveloperCommand("machine StartGdbServer " + port), "GDB startup command rejected");
                deadline = DateTime.UtcNow.AddSeconds(10);
                while (!System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port) && DateTime.UtcNow < deadline) await Task.Delay(100);
                Check(System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port), "GDB server did not listen");
                epsilon.SendDeveloperCommand("pause"); epsilon.SendDeveloperCommand("start");
                await epsilon.StopCalculator();
                Check(!System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == port), "GDB server leaked after shutdown");
                Console.WriteLine("PASS real GDB server startup and port cleanup");
                Console.WriteLine("Artifacts: " + directory);
            }
            catch (Exception ex) { Console.WriteLine(ex); Environment.ExitCode = 1; }
            finally
            {
                File.WriteAllText(Path.Combine(directory, "core.log"), ((TextBox)Field(core, "logBox")).Text);
                File.WriteAllText(Path.Combine(directory, "epsilon.log"), ((TextBox)Field(epsilon, "logBox")).Text);
                await Task.WhenAll(core.StopCalculator(), epsilon.StopCalculator()); host.Close();
            }
        };
        Application.Run(host);
    }
}

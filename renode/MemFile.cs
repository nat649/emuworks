// ============================================================================
//  MemFile  -  vidage / restauration de n'importe quelle zone memoire vers un
//              fichier, depuis le moniteur Renode
// ============================================================================
//  Remplace l'USB pour tout ce qui compte : au lieu d'emuler un controleur DFU
//  pour faire transiter des octets, on expose la memoire directement.
//
//  Generique :
//     mem Save "C:/EmuWorks/sram.bin" 0x20000000 0x40000
//     mem Load "C:/EmuWorks/sram.bin" 0x20000000
//
//  Raccourcis (zones du N0110) :
//     mem SaveSram "..."      / mem LoadSram "..."      SRAM 256 Ko  @0x20000000
//     mem SaveFlash "..."     / mem LoadFlash "..."     QSPI 8 Mo    @0x90000000
//     mem SaveInternal "..."  / mem LoadInternal "..."  flash 64 Ko  @0x08000000
//
//  Sauvegarde automatique (pour que fermer la fenetre ne perde rien) :
//     mem AutoSave "C:/EmuWorks/rom/sram.bin" 5     toutes les 5 s
//     mem AutoSave "" 0                             desactive
//
//  Appel d'un outil externe (le moniteur Renode n'a pas d'echappement shell) :
//     mem Shell "node" "C:/EmuWorks/renode/tools/rom.js push C:/EmuWorks/rom"
//
//  Les chemins relatifs sont resolus depuis EMUWORKS_BASE (la racine du
//  projet), pose par l'appelant : Renode ne conserve pas son repertoire de
//  lancement.
//
//  Les scripts Python d'Epsilon vivent dans staticStorageArea (32 Ko) en SRAM :
//  c'est donc SaveSram / LoadSram qui les capture, pas la flash externe.
// ============================================================================

using System;
using System.IO;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    public class MemFile : IDoubleWordPeripheral, IKnownSize, IDisposable
    {
        public MemFile(IMachine machine)
        {
            this.machine = machine;
        }

        public long Size
        {
            get { return 0x100; }
        }

        public void Reset()
        {
        }

        public uint ReadDoubleWord(long offset)
        {
            return 0;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
        }


        // ---- resolution des chemins --------------------------------------
        //  Renode ne garde PAS le repertoire depuis lequel on l'a lance : un
        //  chemin relatif donne ici atterrirait n'importe ou. L'appelant
        //  (EmuWorks.exe ou les .bat) pose donc EMUWORKS_BASE, et tout ce qui
        //  est relatif se resout par rapport a la racine du projet. C'est ce
        //  qui permet d'installer le dossier ou l'on veut.
        private static string Resoudre(string chemin)
        {
            if(string.IsNullOrEmpty(chemin) || Path.IsPathRooted(chemin))
            {
                return chemin;
            }
            string racine = Environment.GetEnvironmentVariable("EMUWORKS_BASE");
            if(string.IsNullOrEmpty(racine))
            {
                return chemin;
            }
            return Path.GetFullPath(Path.Combine(racine, chemin));
        }

        // ---- generique --------------------------------------------------
        public void Save(string path, long address, long count)
        {
            if(SaveQuiet(path, address, count))
            {
                this.Log(LogLevel.Info, "0x{0:X8}..0x{1:X8} ({2} octets) -> {3}",
                    address, address + count - 1, count, path);
            }
        }

        public void Load(string path, long address)
        {
            try
            {
                path = Resoudre(path);
                if(!File.Exists(path))
                {
                    this.Log(LogLevel.Warning, "Fichier introuvable : {0}", path);
                    return;
                }
                byte[] data = File.ReadAllBytes(path);
                machine.GetSystemBus(this).WriteBytes(data, (ulong)address, false, null);
                this.Log(LogLevel.Info, "{0} ({1} octets) -> 0x{2:X8}", path, data.Length, address);
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec du chargement : {0}", e.Message);
            }
        }

        // ---- raccourcis N0110 -------------------------------------------
        public void SaveSram(string path)     { Save(path, SramBase, SramSize); }
        public void LoadSram(string path)     { Load(path, SramBase); }

        public void SaveFlash(string path)    { Save(path, QspiBase, QspiSize); }
        public void LoadFlash(string path)    { Load(path, QspiBase); }

        public void SaveInternal(string path) { Save(path, IntBase, IntSize); }
        public void LoadInternal(string path) { Load(path, IntBase); }

        // ---- appel d'un outil externe ------------------------------------
        //  Le moniteur Renode ne sait pas lancer de processus : sans ca, chaque
        //  synchronisation obligerait a sortir de l'emulateur pour taper une
        //  commande, ce qui interdit un lanceur en un seul geste.
        public void Shell(string program, string arguments)
        {
            try
            {
                var info = new System.Diagnostics.ProcessStartInfo(program, arguments);
                info.UseShellExecute = false;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.CreateNoWindow = true;
                string racine = Environment.GetEnvironmentVariable("EMUWORKS_BASE");
                if(!string.IsNullOrEmpty(racine))
                {
                    info.WorkingDirectory = racine;
                }
                var process = System.Diagnostics.Process.Start(info);
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                LogLines(output, LogLevel.Info);
                LogLines(errors, LogLevel.Warning);
                if(process.ExitCode != 0)
                {
                    this.Log(LogLevel.Error, "{0} a rendu le code {1}", program, process.ExitCode);
                }
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec de l'appel a {0} : {1}", program, e.Message);
            }
        }

        // ---- sauvegarde automatique --------------------------------------
        //  Renode n'offre pas de crochet « avant fermeture » utilisable depuis un
        //  script : on vide donc la SRAM periodiquement, et le lanceur convertit
        //  le dernier vidage en fichiers .py une fois Renode termine.
        public void AutoSave(string path, int seconds)
        {
            StopTimer();
            autoPath = path;
            if(seconds <= 0 || string.IsNullOrEmpty(path))
            {
                this.Log(LogLevel.Info, "Sauvegarde automatique desactivee.");
                return;
            }
            autoTimer = new System.Timers.Timer(seconds * 1000.0);
            autoTimer.AutoReset = true;
            autoTimer.Elapsed += OnAutoTick;
            autoTimer.Start();
            this.Log(LogLevel.Info, "Sauvegarde automatique toutes les {0} s -> {1}", seconds, path);
        }

        public void Dispose()
        {
            StopTimer();
            if(!string.IsNullOrEmpty(autoPath))
            {
                SaveQuiet(autoPath, SramBase, SramSize);
            }
        }

        private void OnAutoTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            SaveQuiet(autoPath, SramBase, SramSize);
        }

        private bool SaveQuiet(string path, long address, long count)
        {
            try
            {
                path = Resoudre(path);
                byte[] data = machine.GetSystemBus(this).ReadBytes((ulong)address, (int)count, false, null);
                File.WriteAllBytes(path, data);
                return true;
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec du vidage : {0}", e.Message);
                return false;
            }
        }

        private void StopTimer()
        {
            if(autoTimer == null)
            {
                return;
            }
            autoTimer.Stop();
            autoTimer.Elapsed -= OnAutoTick;
            autoTimer.Dispose();
            autoTimer = null;
        }

        private void LogLines(string text, LogLevel level)
        {
            if(string.IsNullOrEmpty(text))
            {
                return;
            }
            string[] lines = text.Replace("\r", "").Split('\n');
            for(int i = 0; i < lines.Length; i++)
            {
                if(lines[i].Length > 0)
                {
                    this.Log(level, "{0}", lines[i]);
                }
            }
        }

        private readonly IMachine machine;
        private System.Timers.Timer autoTimer;
        private string autoPath;

        private const long SramBase = 0x20000000;
        private const long SramSize = 0x40000;
        private const long QspiBase = 0x90000000;
        private const long QspiSize = 0x800000;
        private const long IntBase  = 0x08000000;
        private const long IntSize  = 0x10000;
    }
}

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
//  Numero de serie (base64 des 96 bits d'identifiant unique) :
//     mem SetSerial "EmuWorks"
//     mem SerialFromFile "rom/serie.txt"
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
            else { throw new IOException("Le vidage memoire a echoue : " + path); }
        }

        public void Load(string path, long address)
        {
            try
            {
                path = Resoudre(path);
                if(!File.Exists(path))
                {
                    this.Log(LogLevel.Warning, "Fichier introuvable : {0}", path);
                    throw new FileNotFoundException("Image memoire introuvable", path);
                }
                byte[] data = File.ReadAllBytes(path);
                machine.GetSystemBus(this).WriteBytes(data, (ulong)address, false, null);
                this.Log(LogLevel.Info, "{0} ({1} octets) -> 0x{2:X8}", path, data.Length, address);
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec du chargement : {0}", e.Message);
                throw;
            }
        }

        // ---- raccourcis N0110 -------------------------------------------
        public void SaveSram(string path)     { Save(path, SramBase, SramSize); }
        public void LoadSram(string path)     { Load(path, SramBase); }

        public void SaveFlash(string path)    { Save(path, QspiBase, QspiSize); }
        public void LoadFlash(string path)    { Load(path, QspiBase); }

        public void SaveInternal(string path) { Save(path, IntBase, IntSize); }
        public void LoadInternal(string path) { Load(path, IntBase); }


        // ---- numero de serie ----------------------------------------------
        //  Epsilon n'a pas de numero de serie stocke : il encode en base64 les
        //  96 bits d'identifiant unique du STM32, lus a 0x1FF07A10
        //  (ion/src/device/shared/drivers/serial_number.cpp). 12 octets font
        //  exactement 16 caracteres, sans remplissage -- donc n'importe quel
        //  texte de 16 caracteres de l'alphabet base64 est un identifiant
        //  valide, et le numero affiche se choisit librement.
        public void SetSerial(string texte)
        {
            string propre = Alphabet(texte);
            if(propre.Length == 0)
            {
                this.Log(LogLevel.Warning, "Numero de serie : aucun caractere utilisable dans \"{0}\".", texte);
                this.Log(LogLevel.Warning, "Caracteres admis : A-Z a-z 0-9 + / (alphabet base64).");
                return;
            }
            //  Trop court : on repete le motif plutot que de bourrer de zeros,
            //  "EmuWorks" donne "EmuWorksEmuWorks" et reste lisible.
            while(propre.Length < SerialLength)
            {
                propre = propre + propre;
            }
            propre = propre.Substring(0, SerialLength);
            try
            {
                byte[] identifiant = Convert.FromBase64String(propre);
                machine.GetSystemBus(this).WriteBytes(identifiant, (ulong)UidBase, false, null);
                this.Log(LogLevel.Info, "Numero de serie : {0}", propre);
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Numero de serie refuse : {0}", e.Message);
            }
        }

        //  Fichier optionnel : absent, la calculatrice garde l'identifiant nul.
        public void SerialFromFile(string path)
        {
            string chemin = Resoudre(path);
            if(!File.Exists(chemin))
            {
                return;
            }
            SetSerial(File.ReadAllText(chemin));
        }

        private static string Alphabet(string texte)
        {
            const string admis = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
            var retenu = new System.Text.StringBuilder();
            for(int i = 0; i < texte.Length; i++)
            {
                if(admis.IndexOf(texte[i]) >= 0)
                {
                    retenu.Append(texte[i]);
                }
            }
            return retenu.ToString();
        }

        // ---- appel d'un outil externe ------------------------------------
        //  Le moniteur Renode ne sait pas lancer de processus : sans ca, chaque
        //  synchronisation obligerait a sortir de l'emulateur pour taper une
        //  commande, ce qui interdit un lanceur en un seul geste.
        public void SyncScripts()
        {
            string executable = Environment.GetEnvironmentVariable("EMUWORKS_STORAGE_EXE");
            if(string.IsNullOrEmpty(executable) || !File.Exists(executable))
            {
                throw new IOException("Native storage helper is missing. Start this script from EmuWorks.");
            }
            Shell(executable, "--storage sync rom/sram.bin rom");
        }

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
                using(var process = System.Diagnostics.Process.Start(info))
                {
                    var output = process.StandardOutput.ReadToEndAsync();
                    var errors = process.StandardError.ReadToEndAsync();
                    if(!process.WaitForExit(30000))
                    {
                        process.Kill();
                        throw new IOException("Outil scripts bloque pendant 30 secondes.");
                    }
                    LogLines(output.Result, LogLevel.Info);
                    LogLines(errors.Result, LogLevel.Warning);
                    if(process.ExitCode != 0) throw new IOException(program + " : code " + process.ExitCode);
                }
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec de l'appel a {0} : {1}", program, e.Message);
                throw;
            }
        }

        // ---- sauvegarde automatique --------------------------------------
        //  Renode n'offre pas de crochet « avant fermeture » utilisable depuis un
        //  script : on vide donc la SRAM periodiquement, et le lanceur convertit
        //  le dernier vidage en fichiers .py une fois Renode termine.
        public void AutoSave(string path, int seconds)
        {
            lock(saveLock)
            {
            StopTimer();
            string sessionPath = Environment.GetEnvironmentVariable("EMUWORKS_SESSION_SRAM");
            if(seconds > 0 && !string.IsNullOrEmpty(sessionPath)) path = sessionPath;
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
            if(!SaveQuiet(path, SramBase, SramSize)) throw new IOException("Premiere sauvegarde impossible.");
            this.Log(LogLevel.Info, "Sauvegarde automatique toutes les {0} s -> {1}", seconds, path);
            }
        }

        public void Dispose()
        {
            lock(saveLock)
            {
            StopTimer();
            disposed = true;
            if(!string.IsNullOrEmpty(autoPath))
            {
                SaveQuiet(autoPath, SramBase, SramSize);
            }
            }
        }

        private void OnAutoTick(object sender, System.Timers.ElapsedEventArgs e)
        {
            lock(saveLock)
            {
                if(!disposed && autoTimer != null) SaveQuiet(autoPath, SramBase, SramSize);
            }
        }

        private bool SaveQuiet(string path, long address, long count)
        {
            lock(saveLock)
            {
            string temporary = null;
            try
            {
                path = Resoudre(path);
                byte[] data = machine.GetSystemBus(this).ReadBytes((ulong)address, (int)count, false, null);
                temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                using(var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(data, 0, data.Length);
                    stream.Flush(true);
                }
                if(File.Exists(path)) File.Replace(temporary, path, null);
                else File.Move(temporary, path);
                return true;
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Echec du vidage : {0}", e.Message);
                return false;
            }
            finally
            {
                if(temporary != null && File.Exists(temporary))
                {
                    try { File.Delete(temporary); }
                    catch(Exception e) { this.Log(LogLevel.Warning, "Nettoyage temporaire : {0}", e.Message); }
                }
            }
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
        private readonly object saveLock = new object();
        private bool disposed;

        private const long SramBase = 0x20000000;
        private const long SramSize = 0x40000;
        private const long QspiBase = 0x90000000;
        private const long QspiSize = 0x800000;
        //  identifiant unique du STM32F730, dans la zone OTP
        private const long UidBase = 0x1FF07A10;
        private const int SerialLength = 16;
        private const long IntBase  = 0x08000000;
        private const long IntSize  = 0x10000;
    }
}

// ============================================================================
//  NumWorks.exe - une seule application pour tout l'emulateur
// ============================================================================
//  Renode ouvre normalement ses propres fenetres (moniteur + analyseur d'ecran).
//  Ici il est lance SANS interface (--disable-xwt) et pilote par son entree
//  standard : l'ecran arrive par une socket locale servie par NumWorksDisplay,
//  et le clavier repart par des commandes du moniteur. L'utilisateur ne voit
//  donc qu'une seule fenetre : celle-ci.
//
//  Le dossier rom\ EST la calculatrice. L'application ne fait que l'orchestrer.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NumWorksLauncher
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Application.Run(new MainForm());
        }
    }

    // --- l'ecran de la calculatrice -----------------------------------------
    public class EcranPanel : Panel
    {
        public const int LargeurEcran = 320;
        public const int HauteurEcran = 240;

        private readonly Bitmap image;

        public EcranPanel()
        {
            image = new Bitmap(LargeurEcran, HauteurEcran, PixelFormat.Format16bppRgb565);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Selectable, true);
            BackColor = Color.FromArgb(24, 24, 28);
            TabStop = true;
        }

        public bool Allume { get; set; }

        //  Appelee depuis le fil interface uniquement.
        public void Afficher(byte[] rgb565)
        {
            var zone = new Rectangle(0, 0, LargeurEcran, HauteurEcran);
            BitmapData data = image.LockBits(zone, ImageLockMode.WriteOnly, PixelFormat.Format16bppRgb565);
            try
            {
                for (int y = 0; y < HauteurEcran; y++)
                {
                    Marshal.Copy(rgb565, y * LargeurEcran * 2,
                        IntPtr.Add(data.Scan0, y * data.Stride), LargeurEcran * 2);
                }
            }
            finally
            {
                image.UnlockBits(data);
            }
            Allume = true;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (!Allume)
            {
                using var police = new Font("Segoe UI", 11F);
                using var pinceau = new SolidBrush(Color.FromArgb(150, 150, 160));
                var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                e.Graphics.DrawString("Calculatrice arretee", police, pinceau, ClientRectangle, format);
                return;
            }
            // pixels carres : la dalle fait 320x240, on l'agrandit sans lisser
            e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            e.Graphics.DrawImage(image, ClientRectangle);
        }
    }

    public class MainForm : Form
    {
        private const string RenodeExe = @"C:\Program Files\Renode\bin\Renode.exe";
        private const int PortEcran = 3555;

        private readonly string baseDir;
        private string RomDir => Path.Combine(baseDir, "rom");
        private string ScriptsDir => Path.Combine(RomDir, "scripts");
        private string FirmwaresDir => Path.Combine(baseDir, "firmwares");
        private string RenodeDir => Path.Combine(baseDir, "renode");
        private string RomTool => Path.Combine(RenodeDir, "tools", "rom.js");

        private ComboBox firmwareBox;
        private Button installButton;
        private ListBox scriptList;
        private Button addButton, removeButton, folderButton;
        private Button startButton;
        private TextBox logBox;
        private Label statusLabel;
        private EcranPanel ecran;

        private Process renode;
        private TcpClient socket;
        private Thread fluxImages;
        private volatile bool enMarche;
        private readonly HashSet<string> touchesEnfoncees = new HashSet<string>();

        public MainForm()
        {
            baseDir = ResolveBaseDir();
            BuildUi();
            RefreshFirmwares();
            RefreshScripts();
            CheckEnvironment();
        }

        // --- localisation du dossier de travail -----------------------------
        //  Renode 1.16 ne sait pas lire un chemin contenant des espaces, donc
        //  tout vit sous C:\NumWorks. On accepte quand meme un exe pose a cote
        //  du dossier rom\, pour ne pas casser une copie deplacee.
        private static string ResolveBaseDir()
        {
            string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(Path.Combine(here, "rom"))) return here;
            string parent = Path.GetDirectoryName(here);
            if (parent != null && Directory.Exists(Path.Combine(parent, "rom"))) return parent;
            return @"C:\NumWorks";
        }

        private void BuildUi()
        {
            Text = "Emulateur NumWorks";
            ClientSize = new Size(1012, 600);
            MinimumSize = new Size(1028, 639);
            Font = new Font("Segoe UI", 9F);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;

            var firmwareLabel = new Label { Text = "Firmware :", AutoSize = true, Location = new Point(14, 17) };
            firmwareBox = new ComboBox
            {
                Location = new Point(88, 13), Width = 172,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            installButton = new Button { Text = "Installer", Location = new Point(266, 12), Width = 78 };
            installButton.Click += OnInstallFirmware;

            var scriptsGroup = new GroupBox
            {
                Text = "Scripts Python", Location = new Point(14, 48), Size = new Size(330, 330)
            };
            scriptList = new ListBox
            {
                Location = new Point(12, 24), Size = new Size(306, 254), IntegralHeight = false
            };
            scriptList.DoubleClick += OnOpenScript;
            addButton = new Button { Text = "Ajouter...", Location = new Point(12, 290), Width = 96 };
            addButton.Click += OnAddScript;
            removeButton = new Button { Text = "Supprimer", Location = new Point(116, 290), Width = 96 };
            removeButton.Click += OnRemoveScript;
            folderButton = new Button { Text = "Dossier", Location = new Point(220, 290), Width = 96 };
            folderButton.Click += (s, e) => OpenInShell(ScriptsDir);
            scriptsGroup.Controls.AddRange(new Control[] { scriptList, addButton, removeButton, folderButton });

            var logLabel = new Label { Text = "Journal :", AutoSize = true, Location = new Point(14, 388) };
            logBox = new TextBox
            {
                Location = new Point(14, 408), Size = new Size(330, 172),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5F), BackColor = Color.White, TabStop = false
            };

            ecran = new EcranPanel { Location = new Point(358, 48), Size = new Size(640, 480) };

            startButton = new Button
            {
                Text = "Demarrer la calculatrice",
                Location = new Point(358, 540), Size = new Size(240, 44),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold)
            };
            startButton.Click += OnStartStop;

            statusLabel = new Label
            {
                Location = new Point(612, 554), AutoSize = true, ForeColor = Color.DimGray
            };

            Controls.AddRange(new Control[]
            {
                firmwareLabel, firmwareBox, installButton,
                scriptsGroup, logLabel, logBox, ecran, startButton, statusLabel
            });
        }

        private void CheckEnvironment()
        {
            statusLabel.Text = baseDir;
            if (!File.Exists(RenodeExe))
            {
                Log("Renode introuvable : " + RenodeExe);
                Log("Installe-le avec :  winget install Renode.Renode");
                startButton.Enabled = false;
            }
            if (!Directory.Exists(RomDir))
            {
                Log("Dossier rom\\ introuvable sous " + baseDir);
                startButton.Enabled = false;
            }
            else if (!File.Exists(Path.Combine(RomDir, "internal.bin")))
            {
                Log("Aucun firmware dans rom\\.");
                Log("Aucun firmware n'est distribue avec ce depot : Epsilon est sous");
                Log("licence CC BY-NC-SA, on ne peut pas en redistribuer les binaires.");
                Log("Compile le tien avec renode\\build-firmware-n0110.yml, puis pose");
                Log("les deux images dans firmwares\\<nom>\\ et clique Installer.");
            }
            if (baseDir.Contains(' '))
            {
                Log("ATTENTION : le chemin contient un espace. Renode 1.16 ne sait pas");
                Log("les lire (\"Could not tokenize\"). Remets tout sous C:\\NumWorks.");
            }
        }

        // --- firmwares -------------------------------------------------------
        private void RefreshFirmwares()
        {
            firmwareBox.Items.Clear();
            if (!Directory.Exists(FirmwaresDir)) return;

            long actif = FileLength(Path.Combine(RomDir, "internal.bin"));
            long actifExt = FileLength(Path.Combine(RomDir, "external.bin"));
            string selection = null;

            foreach (var dir in Directory.GetDirectories(FirmwaresDir).OrderBy(d => d))
            {
                string nom = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, "internal.bin"))) continue;
                bool memeTaille = FileLength(Path.Combine(dir, "internal.bin")) == actif
                               && FileLength(Path.Combine(dir, "external.bin")) == actifExt;
                firmwareBox.Items.Add(nom);
                if (memeTaille && selection == null) selection = nom;
            }
            if (selection != null) firmwareBox.SelectedItem = selection;
            else if (firmwareBox.Items.Count > 0) firmwareBox.SelectedIndex = 0;
        }

        private static long FileLength(string path)
        {
            return File.Exists(path) ? new FileInfo(path).Length : -1;
        }

        private void OnInstallFirmware(object sender, EventArgs e)
        {
            if (firmwareBox.SelectedItem == null) return;
            string nom = firmwareBox.SelectedItem.ToString();
            string src = Path.Combine(FirmwaresDir, nom);
            try
            {
                File.Copy(Path.Combine(src, "internal.bin"), Path.Combine(RomDir, "internal.bin"), true);
                File.Copy(Path.Combine(src, "external.bin"), Path.Combine(RomDir, "external.bin"), true);
                // L'adresse du stockage change d'un firmware a l'autre : on jette
                // l'image de reference, elle sera reconstruite au demarrage.
                foreach (var f in new[] { "sram.bin", ".storage.bin", ".storage.json", "load.resc" })
                {
                    string p = Path.Combine(RomDir, f);
                    if (File.Exists(p)) File.Delete(p);
                }
                Log(nom + " installe.");
            }
            catch (Exception ex)
            {
                Log("Echec de l'installation : " + ex.Message);
            }
        }

        // --- scripts ---------------------------------------------------------
        private void RefreshScripts()
        {
            string garde = scriptList.SelectedItem as string;
            scriptList.Items.Clear();
            if (!Directory.Exists(ScriptsDir)) return;
            foreach (var f in Directory.GetFiles(ScriptsDir).OrderBy(f => f))
            {
                scriptList.Items.Add(Path.GetFileName(f));
            }
            if (garde != null && scriptList.Items.Contains(garde)) scriptList.SelectedItem = garde;
        }

        private void OnAddScript(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Ajouter un script Python",
                Filter = "Scripts Python (*.py)|*.py|Tous les fichiers (*.*)|*.*",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            Directory.CreateDirectory(ScriptsDir);
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    File.Copy(f, Path.Combine(ScriptsDir, Path.GetFileName(f)), true);
                    Log("Ajoute : " + Path.GetFileName(f));
                }
                catch (Exception ex) { Log("Echec : " + ex.Message); }
            }
            RefreshScripts();
        }

        private void OnRemoveScript(object sender, EventArgs e)
        {
            if (scriptList.SelectedItem == null) return;
            string nom = scriptList.SelectedItem.ToString();
            if (MessageBox.Show(this, "Supprimer " + nom + " ?", "Confirmer",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                File.Delete(Path.Combine(ScriptsDir, nom));
                Log("Supprime : " + nom);
            }
            catch (Exception ex) { Log("Echec : " + ex.Message); }
            RefreshScripts();
        }

        private void OnOpenScript(object sender, EventArgs e)
        {
            if (scriptList.SelectedItem == null) return;
            OpenInShell(Path.Combine(ScriptsDir, scriptList.SelectedItem.ToString()));
        }

        private static void OpenInShell(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch { /* pas d'application associee : on ignore */ }
        }

        // --- marche / arret ---------------------------------------------------
        private async void OnStartStop(object sender, EventArgs e)
        {
            if (enMarche) { await Arreter(); return; }

            if (!File.Exists(Path.Combine(RomDir, "internal.bin")))
            {
                Log("Aucun firmware dans rom\\. Choisis-en un et clique Installer.");
                return;
            }

            SauvegarderScripts();
            startButton.Enabled = false;
            startButton.Text = "Demarrage...";
            Log("Demarrage de la calculatrice...");

            try
            {
                var info = new ProcessStartInfo(RenodeExe,
                    "--console --disable-xwt --hide-log -e \"i @numworks-embarque.resc\"")
                {
                    WorkingDirectory = RenodeDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                renode = Process.Start(info);
            }
            catch (Exception ex)
            {
                Log("Renode n'a pas demarre : " + ex.Message);
                startButton.Enabled = true;
                startButton.Text = "Demarrer la calculatrice";
                return;
            }

            renode.OutputDataReceived += (s, a) => { if (a.Data != null) Log(a.Data); };
            renode.ErrorDataReceived += (s, a) => { if (a.Data != null) Log(a.Data); };
            renode.BeginOutputReadLine();
            renode.BeginErrorReadLine();

            //  Le demarrage prend une dizaine de secondes : amorcage du firmware,
            //  puis les deux appels a node qui injectent les scripts.
            TcpClient client = await Task.Run(() => Connecter(60));
            if (client == null)
            {
                Log("L'ecran ne repond pas. Regarde le journal ci-dessus.");
                await Arreter();
                return;
            }

            socket = client;
            enMarche = true;
            fluxImages = new Thread(BoucleImages) { IsBackground = true };
            fluxImages.Start();

            startButton.Enabled = true;
            startButton.Text = "Arreter";
            SetControlsEnabled(false);
            ecran.Focus();
            Log("Calculatrice demarree. Tape au clavier, l'ecran a le focus.");
        }

        private TcpClient Connecter(int secondes)
        {
            var fin = DateTime.UtcNow.AddSeconds(secondes);
            while (DateTime.UtcNow < fin)
            {
                if (renode != null && renode.HasExited) return null;
                try
                {
                    var c = new TcpClient();
                    c.Connect("127.0.0.1", PortEcran);
                    c.NoDelay = true;
                    return c;
                }
                catch (SocketException)
                {
                    Thread.Sleep(400);
                }
            }
            return null;
        }

        //  Un octet de requete, une trame RGB565 en reponse. On plafonne a ~30
        //  images par seconde : au-dela on ne gagne rien, la dalle emulee n'est
        //  de toute facon rafraichie que quand le firmware ecrit dedans.
        private void BoucleImages()
        {
            int taille = EcranPanel.LargeurEcran * EcranPanel.HauteurEcran * 2;
            byte[] trame = new byte[taille];
            var demande = new byte[] { 1 };
            try
            {
                var flux = socket.GetStream();
                while (enMarche)
                {
                    flux.Write(demande, 0, 1);
                    int lu = 0;
                    while (lu < taille)
                    {
                        int n = flux.Read(trame, lu, taille - lu);
                        if (n <= 0) return;
                        lu += n;
                    }
                    byte[] copie = (byte[])trame.Clone();
                    if (!IsHandleCreated) return;
                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ecran.Afficher(copie);
                        }
                        catch (Exception ex)
                        {
                            // Une exception sur le fil interface tuerait
                            // l'application : mieux vaut un ecran fige et un
                            // message que la fenetre qui disparait.
                            enMarche = false;
                            Log("Affichage impossible : " + ex.Message);
                        }
                    }));
                    Thread.Sleep(33);
                }
            }
            catch (Exception)
            {
                // socket fermee a l'arret : rien a signaler
            }
        }

        private async Task Arreter()
        {
            startButton.Enabled = false;
            startButton.Text = "Arret...";
            enMarche = false;

            try { socket?.Close(); } catch { }
            socket = null;

            if (renode != null && !renode.HasExited)
            {
                try
                {
                    renode.StandardInput.WriteLine("quit");
                    renode.StandardInput.Flush();
                }
                catch { }
                await Task.Run(() =>
                {
                    if (!renode.WaitForExit(8000))
                    {
                        try { renode.Kill(true); } catch { }
                    }
                });
            }
            renode = null;
            touchesEnfoncees.Clear();

            ecran.Allume = false;
            ecran.Invalidate();

            string vidage = Path.Combine(RomDir, "sram.bin");
            if (File.Exists(vidage))
            {
                Log("Enregistrement du dossier...");
                await Task.Run(() => Lancer("node",
                    Quote(RomTool) + " pull " + Quote(vidage) + " " + Quote(RomDir), baseDir));
            }
            else
            {
                // La calculatrice n'a jamais atteint la premiere sauvegarde
                // automatique : rien a enregistrer, et surtout rien qui
                // justifie de toucher a scripts\.
                Log("Aucun vidage memoire : le dossier est laisse intact.");
            }

            RefreshScripts();
            RefreshFirmwares();
            SetControlsEnabled(true);
            startButton.Enabled = true;
            startButton.Text = "Demarrer la calculatrice";
        }

        //  Filet de securite : si la calculatrice plante avant d'initialiser son
        //  stockage, on veut pouvoir revenir en arriere.
        private void SauvegarderScripts()
        {
            try
            {
                if (!Directory.Exists(ScriptsDir)) return;
                string sauvegarde = Path.Combine(RomDir, ".scripts-precedents");
                if (Directory.Exists(sauvegarde)) Directory.Delete(sauvegarde, true);
                Directory.CreateDirectory(sauvegarde);
                foreach (var f in Directory.GetFiles(ScriptsDir))
                {
                    File.Copy(f, Path.Combine(sauvegarde, Path.GetFileName(f)), true);
                }
            }
            catch (Exception ex) { Log("Sauvegarde impossible : " + ex.Message); }
        }

        // --- clavier ----------------------------------------------------------
        //  Le moniteur Renode reste accessible pendant que la machine tourne :
        //  chaque touche devient une commande sur son entree standard.
        private static readonly Dictionary<Keys, string> Touches = new Dictionary<Keys, string>
        {
            { Keys.Left, "LEFT" }, { Keys.Up, "UP" }, { Keys.Down, "DOWN" }, { Keys.Right, "RIGHT" },
            { Keys.Enter, "EXE" }, { Keys.Back, "BACKSPACE" }, { Keys.Delete, "BACKSPACE" },
            { Keys.Escape, "BACK" }, { Keys.Home, "HOME" },
            { Keys.ShiftKey, "SHIFT" }, { Keys.Tab, "SHIFT" }, { Keys.Capital, "ALPHA" },
            { Keys.D0, "ZERO" }, { Keys.D1, "ONE" }, { Keys.D2, "TWO" }, { Keys.D3, "THREE" },
            { Keys.D4, "FOUR" }, { Keys.D5, "FIVE" }, { Keys.D6, "SIX" }, { Keys.D7, "SEVEN" },
            { Keys.D8, "EIGHT" }, { Keys.D9, "NINE" },
            { Keys.NumPad0, "ZERO" }, { Keys.NumPad1, "ONE" }, { Keys.NumPad2, "TWO" },
            { Keys.NumPad3, "THREE" }, { Keys.NumPad4, "FOUR" }, { Keys.NumPad5, "FIVE" },
            { Keys.NumPad6, "SIX" }, { Keys.NumPad7, "SEVEN" }, { Keys.NumPad8, "EIGHT" },
            { Keys.NumPad9, "NINE" },
            { Keys.Add, "PLUS" }, { Keys.Subtract, "MINUS" },
            { Keys.Multiply, "MULTIPLICATION" }, { Keys.Divide, "DIVISION" },
            { Keys.Decimal, "DOT" },
            { Keys.X, "XNT" }, { Keys.P, "PI" }, { Keys.S, "SINE" }, { Keys.C, "COSINE" },
            { Keys.T, "TANGENT" }, { Keys.E, "EXP" }, { Keys.L, "LN" }, { Keys.R, "SQRT" },
            { Keys.V, "VAR" }, { Keys.A, "ANS" }, { Keys.O, "TOOLBOX" }, { Keys.I, "IMAGINARY" },
            { Keys.F1, "ONOFF" }
        };

        //  Les caracteres passent par KeyPress : la disposition du clavier est
        //  deja appliquee, donc « ( » marche aussi bien sur AZERTY que QWERTY.
        private static readonly Dictionary<char, string> Caracteres = new Dictionary<char, string>
        {
            { '+', "PLUS" }, { '-', "MINUS" }, { '*', "MULTIPLICATION" }, { '/', "DIVISION" },
            { '(', "LEFTPARENTHESIS" }, { ')', "RIGHTPARENTHESIS" },
            { '.', "DOT" }, { ',', "COMMA" }, { '^', "POWER" }
        };

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            //  Sans ca, les fleches et Tab deplacent le focus entre les boutons
            //  au lieu d'atteindre la calculatrice.
            if (enMarche)
            {
                Keys nue = keyData & Keys.KeyCode;
                if (nue == Keys.Left || nue == Keys.Up || nue == Keys.Down
                    || nue == Keys.Right || nue == Keys.Tab)
                {
                    const int WM_KEYDOWN = 0x0100;
                    const int WM_SYSKEYDOWN = 0x0104;
                    if (msg.Msg == WM_KEYDOWN || msg.Msg == WM_SYSKEYDOWN) Enfoncer(nue);
                    return true;
                }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (enMarche && Enfoncer(e.KeyCode))
            {
                e.Handled = true;
                e.SuppressKeyPress = Touches.ContainsKey(e.KeyCode);
            }
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            if (enMarche)
            {
                string nom;
                if (Touches.TryGetValue(e.KeyCode, out nom)) Relacher(nom);
                RelacherTout();
                e.Handled = true;
            }
            base.OnKeyUp(e);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (enMarche)
            {
                string nom;
                if (Caracteres.TryGetValue(e.KeyChar, out nom))
                {
                    //  TapKey maintient la touche assez longtemps pour que le
                    //  balayage ligne par ligne du firmware la voie passer.
                    Commande("keyboard TapKey \"" + nom + "\"");
                    e.Handled = true;
                }
            }
            base.OnKeyPress(e);
        }

        private bool Enfoncer(Keys code)
        {
            string nom;
            if (!Touches.TryGetValue(code, out nom)) return false;
            if (touchesEnfoncees.Contains(nom)) return true;   // repetition automatique
            touchesEnfoncees.Add(nom);
            Commande("keyboard PressKey \"" + nom + "\"");
            return true;
        }

        private void Relacher(string nom)
        {
            if (!touchesEnfoncees.Remove(nom)) return;
            Commande("keyboard ReleaseKey \"" + nom + "\"");
        }

        //  Les touches envoyees par ProcessCmdKey n'ont pas de KeyUp fiable :
        //  on relache tout ce qui traine des que le clavier se libere.
        private void RelacherTout()
        {
            if (touchesEnfoncees.Count == 0) return;
            if ((ModifierKeys & Keys.Shift) != 0) return;
            foreach (var nom in touchesEnfoncees.ToList()) Relacher(nom);
        }

        private void Commande(string ligne)
        {
            try
            {
                if (renode == null || renode.HasExited) return;
                renode.StandardInput.WriteLine(ligne);
                renode.StandardInput.Flush();
            }
            catch (Exception ex)
            {
                Log("Commande refusee : " + ex.Message);
            }
        }

        // --- divers -----------------------------------------------------------
        private void Lancer(string programme, string arguments, string dossier)
        {
            try
            {
                var info = new ProcessStartInfo(programme, arguments)
                {
                    WorkingDirectory = dossier,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using var process = Process.Start(info);
                string sortie = process.StandardOutput.ReadToEnd();
                string erreurs = process.StandardError.ReadToEnd();
                process.WaitForExit();
                foreach (var l in Lignes(sortie)) Log(l);
                foreach (var l in Lignes(erreurs)) Log(l);
            }
            catch (Exception ex)
            {
                Log("Echec de " + programme + " : " + ex.Message);
            }
        }

        private static IEnumerable<string> Lignes(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) yield break;
            foreach (var l in texte.Replace("\r", "").Split('\n'))
            {
                if (l.Length > 0) yield return l;
            }
        }

        private static string Quote(string s) { return "\"" + s + "\""; }

        private void SetControlsEnabled(bool valeur)
        {
            installButton.Enabled = valeur;
            firmwareBox.Enabled = valeur;
            addButton.Enabled = valeur;
            removeButton.Enabled = valeur;
        }

        private void Log(string message)
        {
            if (logBox.InvokeRequired)
            {
                try { logBox.BeginInvoke(new Action<string>(Log), message); } catch { }
                return;
            }
            if (logBox.Lines.Length > 500) logBox.Clear();
            logBox.AppendText(message + Environment.NewLine);
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (enMarche)
            {
                //  On ne ferme pas sans enregistrer : l'arret ecrit les scripts
                //  de la calculatrice dans le dossier.
                e.Cancel = true;
                await Arreter();
                Close();
                return;
            }
            base.OnFormClosing(e);
        }
    }
}

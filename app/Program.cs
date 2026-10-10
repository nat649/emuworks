// ============================================================================
//  EmuWorks - une seule application pour tout l'emulateur
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
using System.Buffers;
using System.Net.NetworkInformation;
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

namespace EmuWorks
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            ApplicationConfiguration.Initialize();
            //  Mode apercu : rend le panneau ecran dans un PNG, sans ouvrir de
            //  fenetre. Sert a verifier l'apparence depuis un terminal.
            //    EmuWorks.exe --apercu <trame.raw RGB888> <sortie.png> [largeur hauteur]
            if (args.Length >= 3 && args[0] == "--apercu")
            {
                Apercu(args);
                return;
            }
            Application.Run(new MainForm());
        }

        private static void Apercu(string[] args)
        {
            //  Sans ce filet, une exception ici ouvrirait une boite de dialogue
            //  et l'application resterait bloquee : inutilisable depuis un
            //  terminal, qui est pourtant tout l'interet de ce mode.
            try
            {
                RendreApercu(args);
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(args[2] + ".erreur.txt", ex.ToString()); } catch { }
                Environment.Exit(1);
            }
        }

        private static void RendreApercu(string[] args)
        {
            int largeur = args.Length >= 5 ? int.Parse(args[3]) : 640;
            int hauteur = args.Length >= 5 ? int.Parse(args[4]) : 480;

            using var panneau = new EcranPanel();
            byte[] rgb888 = File.ReadAllBytes(args[1]);
            int pixels = EcranPanel.LargeurEcran * EcranPanel.HauteurEcran;
            if (rgb888.Length >= pixels * 3)
            {
                byte[] rgb565 = new byte[pixels * 2];
                for (int i = 0; i < pixels; i++)
                {
                    int c = ((rgb888[i * 3] >> 3) << 11)
                          | ((rgb888[i * 3 + 1] >> 2) << 5)
                          | (rgb888[i * 3 + 2] >> 3);
                    rgb565[i * 2] = (byte)(c & 0xFF);
                    rgb565[i * 2 + 1] = (byte)(c >> 8);
                }
                panneau.Afficher(rgb565);
            }

            using var rendu = new Bitmap(largeur, hauteur);
            using (var g = Graphics.FromImage(rendu))
            {
                panneau.Dessiner(g, new Rectangle(0, 0, largeur, hauteur));
            }
            rendu.Save(args[2], ImageFormat.Png);
            Console.WriteLine("apercu " + largeur + "x" + hauteur + " -> " + args[2]);
        }
    }

    // --- l'ecran de la calculatrice -----------------------------------------
    public class EcranPanel : Panel
    {
        public const int LargeurEcran = 320;
        public const int HauteurEcran = 240;

        private static readonly Color Fond = Color.FromArgb(27, 29, 33);
        private static readonly Color Coque = Color.FromArgb(42, 44, 49);
        private static readonly Color Lisere = Color.FromArgb(62, 65, 72);

        private readonly Bitmap image;

        public EcranPanel()
        {
            image = new Bitmap(LargeurEcran, HauteurEcran, PixelFormat.Format16bppRgb565);
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint | ControlStyles.Selectable
                     | ControlStyles.ResizeRedraw, true);
            BackColor = Fond;
            TabStop = true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) image.Dispose();
            base.Dispose(disposing);
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

        //  La dalle fait 320x240. On ne l'etire pas a la taille du panneau : on
        //  cherche le plus grand agrandissement ENTIER qui rentre, et on centre.
        //  Un facteur non entier (2,3x par exemple) dedouble une ligne de pixels
        //  sur trois -- le texte de la calculatrice en devient bancal.
        private static Rectangle ZoneDalle(Rectangle panneau)
        {
            const int marge = 26;
            int largeurDispo = Math.Max(1, panneau.Width - 2 * marge);
            int hauteurDispo = Math.Max(1, panneau.Height - 2 * marge);
            int facteur = Math.Min(largeurDispo / LargeurEcran, hauteurDispo / HauteurEcran);
            if (facteur < 1) facteur = 1;
            int l = LargeurEcran * facteur, h = HauteurEcran * facteur;
            return new Rectangle(panneau.X + (panneau.Width - l) / 2,
                                 panneau.Y + (panneau.Height - h) / 2, l, h);
        }

        private static GraphicsPath Arrondi(Rectangle r, int rayon)
        {
            var chemin = new GraphicsPath();
            int d = rayon * 2;
            chemin.AddArc(r.X, r.Y, d, d, 180, 90);
            chemin.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            chemin.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            chemin.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            chemin.CloseFigure();
            return chemin;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Dessiner(e.Graphics, ClientRectangle);
        }

        //  Separe de OnPaint pour pouvoir rendre le panneau hors fenetre (voir
        //  le mode --apercu) : c'est la seule facon de verifier l'apparence
        //  sans ouvrir l'application.
        public void Dessiner(Graphics g, Rectangle panneau)
        {
            using (var fond = new SolidBrush(BackColor))
            {
                g.FillRectangle(fond, panneau);
            }

            Rectangle dalle = ZoneDalle(panneau);
            Rectangle coque = Rectangle.Inflate(dalle, 14, 14);

            g.SmoothingMode = SmoothingMode.AntiAlias;

            // ombre portee : quelques passes de plus en plus larges et pales
            for (int i = 6; i >= 1; i--)
            {
                var halo = Rectangle.Inflate(coque, i * 2, i * 2);
                halo.Offset(0, i);
                using var chemin = Arrondi(halo, 12 + i * 2);
                using var pinceau = new SolidBrush(Color.FromArgb(10, 0, 0, 0));
                g.FillPath(pinceau, chemin);
            }

            using (var chemin = Arrondi(coque, 12))
            using (var pinceau = new SolidBrush(Coque))
            using (var crayon = new Pen(Lisere))
            {
                g.FillPath(pinceau, chemin);
                g.DrawPath(crayon, chemin);
            }

            if (!Allume)
            {
                using var eteint = new SolidBrush(Color.FromArgb(18, 19, 22));
                g.FillRectangle(eteint, dalle);
                using var police = new Font("Segoe UI", 10.5F);
                using var texte = new SolidBrush(Color.FromArgb(120, 122, 132));
                using var format = new StringFormat
                {
                    Alignment = StringAlignment.Center,
                    LineAlignment = StringAlignment.Center
                };
                g.DrawString("Calculatrice arretee", police, texte, dalle, format);
                return;
            }

            // pixels carres : on agrandit sans lisser
            g.SmoothingMode = SmoothingMode.None;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, dalle);
        }
    }

    public class MainForm : Form
    {
        private const int PortEcran = 3555;
        private const int PeriodeImageMs = 33;      // ~30 images par seconde
        private const int DelaiArretMs = 8000;      // avant de terminer Renode de force

        //  Emplacement de Renode : la variable RENODE_EXE l'emporte, puis le
        //  PATH, puis les installations habituelles. Le depot est public, tout
        //  le monde ne l'a pas au meme endroit.
        private static readonly string RenodeExe = TrouverRenode();

        private static string TrouverRenode()
        {
            string impose = Environment.GetEnvironmentVariable("RENODE_EXE");
            if (!string.IsNullOrEmpty(impose) && File.Exists(impose)) return impose;

            foreach (var dossier in (Environment.GetEnvironmentVariable("PATH") ?? "")
                                        .Split(Path.PathSeparator))
            {
                if (dossier.Length == 0) continue;
                try
                {
                    string essai = Path.Combine(dossier, "Renode.exe");
                    if (File.Exists(essai)) return essai;
                }
                catch (ArgumentException)
                {
                    // entree de PATH mal formee : on passe a la suivante
                }
            }

            string[] racines =
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            };
            foreach (var racine in racines)
            {
                if (string.IsNullOrEmpty(racine)) continue;
                string essai = Path.Combine(racine, "Renode", "bin", "Renode.exe");
                if (File.Exists(essai)) return essai;
            }

            //  Introuvable : on rend le chemin attendu, pour que le message
            //  d'erreur dise ou l'application a cherche.
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                                "Renode", "bin", "Renode.exe");
        }

        private readonly string baseDir;
        private string RomDir => Path.Combine(baseDir, "rom");
        private string ScriptsDir => Path.Combine(RomDir, "scripts");
        private string FirmwaresDir => Path.Combine(baseDir, "firmwares");
        private string RenodeDir => Path.Combine(baseDir, "renode");
        private string RomTool => Path.Combine(RenodeDir, "tools", "rom.js");

        private ComboBox firmwareBox;
        private GroupBox scriptsGroup;
        private Button installButton, importButton, restoreButton;
        private ListBox scriptList;
        private Button addButton, removeButton, folderButton;
        private Button startButton;
        private TextBox logBox;
        private TextBox serieBox;
        private Label statusLabel;
        private EcranPanel ecran;

        private Process renode;
        private TcpClient socket;
        private Task fluxImages;
        private bool enMarche;
        private CancellationTokenSource session;
        private readonly SemaphoreSlim lifecycle = new SemaphoreSlim(1, 1);
        private readonly object frameLock = new object();
        private byte[] latestFrame;
        private readonly System.Windows.Forms.Timer imageTimer = new System.Windows.Forms.Timer { Interval = PeriodeImageMs };
        private string sessionDump;
        private bool sessionReady, closing, closeAllowed;
        private bool coreSession;
        private FileStream sessionLease;
        private const int StartupTimeoutSeconds = 60;
        private const int FrameTimeoutSeconds = 10;
        private readonly HashSet<string> touchesEnfoncees = new HashSet<string>();

        public MainForm()
        {
            baseDir = ResolveBaseDir();
            BuildUi();
            try
            {
                using var lease = AcquireLease();
                FirmwareStore.Recover(RomDir);
                CoreFirmware.PrepareLibrary(baseDir);
            }
            catch (Exception ex) { Log("Preparation du firmware : " + ex.Message); }
            RefreshFirmwares();
            RefreshScripts();
            ChargerSerie();
            imageTimer.Tick += (s, e) => RenderLatestFrame();
            SetControlsEnabled(true);
            CheckEnvironment();
        }

        // --- numero de serie --------------------------------------------------
        private string FichierSerie => Path.Combine(RomDir, "serie.txt");

        private void ChargerSerie()
        {
            try
            {
                if (File.Exists(FichierSerie)) serieBox.Text = File.ReadAllText(FichierSerie).Trim();
            }
            catch (Exception ex) { Log("Lecture de serie.txt : " + ex.Message); }
        }

        private void EnregistrerSerie()
        {
            try
            {
                string valeur = serieBox.Text.Trim();
                if (valeur.Length == 0)
                {
                    if (File.Exists(FichierSerie)) File.Delete(FichierSerie);
                    return;
                }
                if (!File.Exists(FichierSerie) || File.ReadAllText(FichierSerie).Trim() != valeur)
                {
                    Directory.CreateDirectory(RomDir);
                    File.WriteAllText(FichierSerie, valeur + Environment.NewLine);
                }
            }
            catch (Exception ex) { Log("Ecriture de serie.txt : " + ex.Message); }
        }

        // --- localisation du dossier de travail -----------------------------
        //  L'emplacement du projet est libre (mais sans espaces : Renode 1.16
        //  ne sait pas les lire). On part de l'exe, puis du dossier parent,
        //  puis des emplacements habituels.
        private static string ResolveBaseDir()
        {
            string configured = Environment.GetEnvironmentVariable("EMUWORKS_BASE");
            if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
            string here = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            if (Directory.Exists(Path.Combine(here, "renode"))) return here;
            if (Directory.Exists(Path.Combine(here, "rom"))) return here;
            string parent = Path.GetDirectoryName(here);
            if (parent != null && Directory.Exists(Path.Combine(parent, "rom"))) return parent;
            foreach (var repli in new[] { @"C:\EmuWorks", @"C:\NumWorks" })
            {
                if (Directory.Exists(Path.Combine(repli, "rom"))) return repli;
            }
            return @"C:\EmuWorks";
        }

        private void BuildUi()
        {
            Text = "EmuWorks";
            //  Le panneau ecran doit loger 2x la dalle (640x480) PLUS la marge
            //  et le cadre, sinon l'agrandissement entier retombe a 1x.
            ClientSize = new Size(1102, 688);
            MinimumSize = new Size(1118, 727);
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
            importButton = new Button { Text = "Importer un firmware...", Location = new Point(688, 12), Width = 184 };
            importButton.Click += OnImportFirmware;

            //  Epsilon ne stocke pas de numero de serie : il encode l'identifiant
            //  unique du processeur. On le choisit donc librement.
            var serieLabel = new Label
            {
                Text = "Numero de serie :", AutoSize = true, Location = new Point(358, 17)
            };
            serieBox = new TextBox
            {
                Location = new Point(468, 13), Width = 200, MaxLength = 64,
                PlaceholderText = "EmuWorks"
            };
            serieBox.Leave += (s, e) => EnregistrerSerie();

            scriptsGroup = new GroupBox
            {
                Text = "Scripts Python", Location = new Point(14, 48), Size = new Size(330, 330)
            };
            scriptList = new ListBox
            {
                Location = new Point(12, 24), Size = new Size(306, 216), IntegralHeight = false
            };
            scriptList.DoubleClick += OnOpenScript;
            addButton = new Button { Text = "Ajouter...", Location = new Point(12, 290), Width = 96 };
            addButton.Click += OnAddScript;
            removeButton = new Button { Text = "Supprimer", Location = new Point(116, 290), Width = 96 };
            removeButton.Click += OnRemoveScript;
            folderButton = new Button { Text = "Dossier", Location = new Point(220, 290), Width = 96 };
            folderButton.Click += (s, e) => OpenInShell(ScriptsDir);
            restoreButton = new Button { Text = "Restaurer une sauvegarde...", Location = new Point(12, 248), Width = 304 };
            restoreButton.Click += OnRestoreScripts;
            scriptsGroup.Controls.AddRange(new Control[] { scriptList, addButton, removeButton, folderButton, restoreButton });

            var logLabel = new Label { Text = "Journal :", AutoSize = true, Location = new Point(14, 388) };
            logBox = new TextBox
            {
                Location = new Point(14, 408), Size = new Size(330, 260),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 8.5F), BackColor = Color.White, TabStop = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Bottom
            };

            //  L'ecran suit la taille de la fenetre : agrandir la fenetre agrandit
            //  la calculatrice, par bonds entiers (2x, 3x...) pour rester net.
            ecran = new EcranPanel
            {
                Location = new Point(358, 48), Size = new Size(730, 570),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };

            startButton = new Button
            {
                Text = "Demarrer la calculatrice",
                Location = new Point(358, 630), Size = new Size(240, 44),
                Font = new Font("Segoe UI", 10F, FontStyle.Bold),
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };
            startButton.Click += OnStartStop;

            statusLabel = new Label
            {
                Location = new Point(612, 644), AutoSize = true, ForeColor = Color.DimGray,
                Anchor = AnchorStyles.Left | AnchorStyles.Bottom
            };

            Controls.AddRange(new Control[]
            {
                firmwareLabel, firmwareBox, installButton, importButton, serieLabel, serieBox,
                scriptsGroup, logLabel, logBox, ecran, startButton, statusLabel
            });
        }

        private void CheckEnvironment()
        {
            statusLabel.Text = baseDir;
            if (!File.Exists(RenodeExe))
            {
                Log("Renode introuvable. Cherche dans RENODE_EXE, le PATH, puis :");
                Log("  " + RenodeExe);
                Log("Installe-le avec :  winget install Renode.Renode");
                Log("Ou pose son chemin dans la variable RENODE_EXE.");
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
                Log("Choisis emuworks-core-0.1 puis Installer pour utiliser le firmware libre integre.");
                Log("Tu peux aussi importer tes propres images avec Importer un firmware.");
                Log("Compile le tien avec renode\\build-firmware-n0110.yml, puis pose");
                Log("les deux images dans firmwares\\<nom>\\ et clique Installer.");
            }
            if (baseDir.Contains(' '))
            {
                //  Renode 1.16 echoue sur un chemin a espaces (« Could not
                //  tokenize ») sans rien expliquer. Laisser demarrer ne
                //  produirait qu'une panne incomprehensible : on bloque.
                Log("Le chemin du projet contient un espace :");
                Log("  " + baseDir);
                Log("Renode 1.16 ne sait pas les lire (\"Could not tokenize\").");
                Log("Deplace le dossier vers un chemin sans espace, par exemple C:\\EmuWorks.");
                startButton.Enabled = false;
            }
        }

        // --- firmwares -------------------------------------------------------
        private void RefreshFirmwares()
        {
            firmwareBox.Items.Clear();
            if (!Directory.Exists(FirmwaresDir)) return;

            string selection = null;

            foreach (var dir in Directory.GetDirectories(FirmwaresDir).OrderBy(d => d))
            {
                string nom = Path.GetFileName(dir);
                if (!File.Exists(Path.Combine(dir, "internal.bin")) || !File.Exists(Path.Combine(dir, "external.bin"))) continue;
                bool memeTaille = FirmwareStore.Same(Path.Combine(dir, "internal.bin"), Path.Combine(RomDir, "internal.bin"))
                               && FirmwareStore.Same(Path.Combine(dir, "external.bin"), Path.Combine(RomDir, "external.bin"));
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
            if (session != null || firmwareBox.SelectedItem == null) return;
            string src = Path.Combine(FirmwaresDir, firmwareBox.SelectedItem.ToString());
            InstallFirmware(Path.Combine(src, "internal.bin"), Path.Combine(src, "external.bin"));
        }

        private void OnImportFirmware(object sender, EventArgs e)
        {
            if (session != null) return;
            using var inside = new OpenFileDialog { Title = "Choisir l'image interne (internal.bin ou epsilon.internal.bin)", Filter = "Image binaire (*.bin)|*.bin" };
            if (inside.ShowDialog(this) != DialogResult.OK) return;
            using var outside = new OpenFileDialog { Title = "Choisir l'image externe correspondante (external.bin)", Filter = "Image binaire (*.bin)|*.bin", InitialDirectory = Path.GetDirectoryName(inside.FileName) };
            if (outside.ShowDialog(this) != DialogResult.OK) return;
            InstallFirmware(inside.FileName, outside.FileName);
        }

        private void InstallFirmware(string inside, string outside)
        {
            try
            {
                using var lease = AcquireLease();
                FirmwareStore.Install(inside, outside, RomDir);
                Log("Firmware installe. Les tailles et la table de demarrage ont ete verifiees.");
                RefreshFirmwares();
                SetControlsEnabled(true);
                if (CoreFirmware.IsCore(Path.Combine(RomDir, "internal.bin")))
                    Log("EmuWorks Core : calculatrice de base, sans Python. Historique conserve pendant la session.");
                startButton.Enabled = true;
                CheckEnvironment();
            }
            catch (Exception ex) { Log("Installation refusee : " + ex.Message); }
        }

        private async void OnRestoreScripts(object sender, EventArgs e)
        {
            if (session != null) return;
            using var dialog = new OpenFileDialog
            {
                Title = "Choisir le manifest.json de la sauvegarde a restaurer",
                Filter = "Sauvegarde (manifest.json)|manifest.json",
                InitialDirectory = Path.Combine(RomDir, "sauvegardes")
            };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            await lifecycle.WaitAsync();
            if (closing) { lifecycle.Release(); return; }
            SetControlsEnabled(false); startButton.Enabled = false;
            try
            {
                using var lease = AcquireLease();
                if (await RunTool("restore", RomDir, Path.GetDirectoryName(dialog.FileName))) RefreshScripts();
            }
            catch (Exception ex) { Log("Restauration refusee : " + ex.Message); }
            finally
            {
                SetControlsEnabled(true); startButton.Enabled = true; CheckEnvironment();
                lifecycle.Release();
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

        private async void OnAddScript(object sender, EventArgs e)
        {
            using var dlg = new OpenFileDialog
            {
                Title = "Ajouter un script Python",
                Filter = "Scripts Python (*.py)|*.py",
                Multiselect = true
            };
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            await EditScripts(() => {
            Directory.CreateDirectory(ScriptsDir);
            foreach (var f in dlg.FileNames)
            {
                try
                {
                    string name = Path.GetFileName(f);
                    if (!System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-zA-Z0-9_][a-zA-Z0-9_.-]*\.py$"))
                        throw new IOException("Nom Python invalide : " + name);
                    string temporary = Path.Combine(RomDir, Guid.NewGuid().ToString("N") + ".tmp");
                    try
                    {
                        File.Copy(f, temporary);
                        File.Move(temporary, Path.Combine(ScriptsDir, name), true);
                    }
                    finally { if (File.Exists(temporary)) File.Delete(temporary); }
                    Log("Ajoute : " + Path.GetFileName(f));
                }
                catch (Exception ex) { Log("Echec : " + ex.Message); }
            }
            });
        }

        private async void OnRemoveScript(object sender, EventArgs e)
        {
            if (scriptList.SelectedItem == null) return;
            string nom = scriptList.SelectedItem.ToString();
            if (MessageBox.Show(this, "Supprimer " + nom + " ?", "Confirmer",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            await EditScripts(() =>
            {
                File.Delete(Path.Combine(ScriptsDir, nom));
                Log("Supprime : " + nom);
            });
        }

        private FileStream AcquireLease()
        {
            Directory.CreateDirectory(RomDir);
            try { return new FileStream(Path.Combine(RomDir, ".session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException ex) { throw new IOException("Cette ROM est deja utilisee par une autre instance, ou inaccessible.", ex); }
        }

        private async Task EditScripts(Action edit)
        {
            await lifecycle.WaitAsync();
            if (closing || session != null) { lifecycle.Release(); return; }
            SetControlsEnabled(false); startButton.Enabled = false;
            try
            {
                using var lease = AcquireLease();
                if (await RunTool("backup", RomDir)) edit();
            }
            catch (Exception ex) { Log("Modification refusee : " + ex.Message); }
            finally
            {
                RefreshScripts(); SetControlsEnabled(true); startButton.Enabled = true;
                lifecycle.Release(); CheckEnvironment();
            }
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
            if (session != null) await Arreter();
            else await Demarrer();
        }

        private async Task Demarrer()
        {
            await lifecycle.WaitAsync();
            bool started = false;
            try
            {
                if (closing || session != null) return;
                if (baseDir.Contains(' ')) throw new IOException("Deplace EmuWorks vers un chemin sans espace.");
                if (!File.Exists(RenodeExe)) throw new IOException("Renode introuvable. Installe Renode ou renseigne RENODE_EXE.");
                session = new CancellationTokenSource();
                sessionLease = AcquireLease();
                FirmwareStore.Recover(RomDir);
                FirmwareStore.Validate(Path.Combine(RomDir, "internal.bin"), Path.Combine(RomDir, "external.bin"));
                coreSession = CoreFirmware.IsCore(Path.Combine(RomDir, "internal.bin"));
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(p => p.Port == PortEcran))
                    throw new IOException("Le port " + PortEcran + " est deja utilise. Ferme l'autre emulateur.");

                var token = session.Token;
                SetControlsEnabled(false);
                startButton.Text = "Annuler le demarrage";
                if (!coreSession && !await RunTool("backup", RomDir)) throw new IOException("La sauvegarde des scripts a echoue. Demarrage annule.");
                token.ThrowIfCancellationRequested();
                EnregistrerSerie();
                string sessions = Path.Combine(RomDir, ".sessions");
                Directory.CreateDirectory(sessions);
                sessionDump = Path.Combine(sessions, Guid.NewGuid().ToString("N") + ".bin");
                sessionReady = false;

                string bootScript = coreSession ? "emuworks-core.resc" : "numworks-embarque.resc";
                var info = new ProcessStartInfo(RenodeExe,
                    "--console --disable-xwt --hide-log -e \"i @" + bootScript + "\"")
                {
                    WorkingDirectory = RenodeDir, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
                };
                info.Environment["EMUWORKS_BASE"] = baseDir;
                info.Environment["EMUWORKS_SESSION_SRAM"] = sessionDump;
                var process = new Process { StartInfo = info };
                process.OutputDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                process.ErrorDataReceived += (s, e) => { if (e.Data != null) Log(e.Data); };
                try { if (!process.Start()) throw new IOException("Renode n'a pas demarre."); }
                catch { process.Dispose(); throw; }
                renode = process;
                renode.BeginOutputReadLine(); renode.BeginErrorReadLine();
                Log("Amorcage du firmware...");
                socket = await Connecter(renode, token);
                // La connexion seule ne prouve pas que le serveur peut fournir une image.
                byte[] first = await ReadFrame(socket.GetStream(), token);
                try { ecran.Afficher(first); }
                finally { ArrayPool<byte>.Shared.Return(first); }
                sessionReady = enMarche = started = true;
                startButton.Text = "Arreter";
                imageTimer.Start();
                fluxImages = BoucleImages(socket, token);
                ecran.Focus();
                Log("Calculatrice demarree.");
            }
            catch (OperationCanceledException)
            {
                Log(session?.IsCancellationRequested == true ? "Demarrage annule." : "Delai de demarrage depasse : aucun ecran disponible.");
            }
            catch (Exception ex) { Log("Demarrage impossible : " + ex.Message); }
            finally { lifecycle.Release(); }
            if (!started) await Arreter();
        }

        private async Task<TcpClient> Connecter(Process process, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(StartupTimeoutSeconds));
            while (true)
            {
                timeout.Token.ThrowIfCancellationRequested();
                if (process.HasExited) throw new IOException("Renode s'est ferme (code " + process.ExitCode + "). Consulte le journal.");
                var client = new TcpClient { NoDelay = true };
                try
                {
                    await client.ConnectAsync("127.0.0.1", PortEcran, timeout.Token);
                    return client;
                }
                catch (SocketException) { client.Dispose(); }
                catch { client.Dispose(); throw; }
                await Task.Delay(250, timeout.Token);
            }
        }

        private static async Task<byte[]> ReadFrame(NetworkStream stream, CancellationToken token)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(FrameTimeoutSeconds));
            int size = EcranPanel.LargeurEcran * EcranPanel.HauteurEcran * 2;
            byte[] frame = ArrayPool<byte>.Shared.Rent(size);
            try
            {
                await stream.WriteAsync(new byte[] { 1 }, timeout.Token);
                await stream.ReadExactlyAsync(frame.AsMemory(0, size), timeout.Token);
                return frame;
            }
            catch { ArrayPool<byte>.Shared.Return(frame); throw; }
        }

        private async Task BoucleImages(TcpClient client, CancellationToken token)
        {
            try
            {
                while (true)
                {
                    byte[] frame = await ReadFrame(client.GetStream(), token).ConfigureAwait(false);
                    lock (frameLock)
                    {
                        if (latestFrame != null) ArrayPool<byte>.Shared.Return(latestFrame);
                        latestFrame = frame;
                    }
                    await Task.Delay(PeriodeImageMs, token).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    Log("Connexion a l'ecran perdue : " + ex.Message);
                    Post(async () => { if (socket == client) await Arreter(); });
                }
            }
        }

        private void RenderLatestFrame()
        {
            byte[] frame;
            lock (frameLock) { frame = latestFrame; latestFrame = null; }
            if (frame == null) return;
            try { if (enMarche) ecran.Afficher(frame); }
            catch (Exception ex) { Log("Affichage : " + ex.Message); Post(async () => await Arreter()); }
            finally { ArrayPool<byte>.Shared.Return(frame); }
        }

        private async Task Arreter()
        {
            // Annule aussi une connexion en cours avant d'attendre le verrou de cycle de vie.
            session?.Cancel();
            await lifecycle.WaitAsync();
            if (session == null) { lifecycle.Release(); return; }
            try
            {
                startButton.Enabled = false; startButton.Text = "Arret et sauvegarde...";
                enMarche = false; imageTimer.Stop();
                socket?.Dispose();
                if (fluxImages != null) await fluxImages;
                fluxImages = null; socket = null;
                lock (frameLock)
                {
                    if (latestFrame != null) ArrayPool<byte>.Shared.Return(latestFrame);
                    latestFrame = null;
                }
                if (renode != null)
                {
                    if (!renode.HasExited)
                    {
                        // Pause avant le dernier vidage : le firmware ne modifie plus les records.
                        if (sessionReady && !coreSession)
                        {
                            Commande("pause");
                            Commande("mem SaveSram \"" + sessionDump.Replace('\\', '/') + "\"");
                        }
                        Commande("quit");
                        using var timeout = new CancellationTokenSource(DelaiArretMs);
                        try { await renode.WaitForExitAsync(timeout.Token); }
                        catch (OperationCanceledException)
                        {
                            Log("Renode ne repond plus : arret du processus lance par EmuWorks.");
                            renode.Kill(true);
                            using var killTimeout = new CancellationTokenSource(DelaiArretMs);
                            await renode.WaitForExitAsync(killTimeout.Token);
                        }
                    }
                    renode.Dispose(); renode = null;
                }
                if (coreSession) Log("EmuWorks Core arrete. Son historique sera remis a zero au prochain demarrage.");
                else if (sessionReady && File.Exists(sessionDump))
                {
                    if (!await RunTool("pull", sessionDump, RomDir))
                        Log("Import refuse : les scripts precedents sont conserves. Vidage : " + sessionDump);
                }
                else Log("Aucune sauvegarde valide de cette session a importer. Scripts conserves.");
            }
            catch (Exception ex) { Log("Arret incomplet : " + ex.Message); }
            finally
            {
                // Si un processus refuse meme Kill, garder sa reference pour pouvoir reessayer.
                bool remaining = renode != null && !renode.HasExited;
                if (!remaining)
                {
                    renode?.Dispose(); renode = null;
                    session?.Dispose(); session = null;
                    sessionLease?.Dispose(); sessionLease = null;
                    sessionReady = false;
                }
                touchesEnfoncees.Clear();
                ecran.Allume = false; ecran.Invalidate();
                RefreshScripts(); RefreshFirmwares();
                SetControlsEnabled(!remaining);
                startButton.Enabled = true;
                startButton.Text = remaining ? "Reessayer l'arret" : "Demarrer la calculatrice";
                lifecycle.Release();
                if (!remaining) CheckEnvironment();
            }
        }

        private async Task<bool> RunTool(params string[] arguments)
        {
            try
            {
                var info = new ProcessStartInfo("node")
                {
                    WorkingDirectory = baseDir, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                info.ArgumentList.Add(RomTool);
                foreach (var arg in arguments) info.ArgumentList.Add(arg);
                using var process = Process.Start(info);
                Task<string> output = process.StandardOutput.ReadToEndAsync();
                Task<string> errors = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    process.Kill(true);
                    using var killTimeout = new CancellationTokenSource(DelaiArretMs);
                    await process.WaitForExitAsync(killTimeout.Token);
                    throw new IOException("Synchronisation trop longue, interrompue.");
                }
                foreach (var line in Lignes(await output)) Log(line);
                foreach (var line in Lignes(await errors)) Log(line);
                return process.ExitCode == 0;
            }
            catch (Exception ex) { Log("Outil scripts : " + ex.Message); return false; }
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
            bool python = !CoreFirmware.IsCore(Path.Combine(RomDir, "internal.bin"));
            scriptsGroup.Text = python ? "Scripts Python" : "Python indisponible dans Core 0.1";
            installButton.Enabled = valeur;
            importButton.Enabled = valeur;
            restoreButton.Enabled = valeur && python;
            scriptList.Enabled = valeur && python;
            folderButton.Enabled = valeur && python;
            firmwareBox.Enabled = valeur;
            serieBox.Enabled = valeur && python;
            addButton.Enabled = valeur && python;
            removeButton.Enabled = valeur && python;
        }

        private void Post(Action action)
        {
            if (IsDisposed || Disposing || !IsHandleCreated) return;
            try { BeginInvoke(new Action(() => { if (!IsDisposed && !Disposing) action(); })); }
            catch (InvalidOperationException) { /* fermeture en cours */ }
        }

        private void Log(string message)
        {
            if (IsDisposed || Disposing) return;
            if (InvokeRequired) { Post(() => Log(message)); return; }
            if (logBox.Lines.Length > 500) logBox.Clear();
            logBox.AppendText(message + Environment.NewLine);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            imageTimer.Dispose();
            base.OnFormClosed(e);
        }


        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (closeAllowed) { base.OnFormClosing(e); return; }
            e.Cancel = true;
            if (closing) return;
            closing = true;
            await Arreter();
            if (renode != null) { closing = false; return; }
            closeAllowed = true;
            Close();
        }
    }
}

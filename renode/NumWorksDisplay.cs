// ============================================================================
//  NumWorksDisplay  -  ecran ST7789V de la NumWorks N0110 vu du bus FMC
// ============================================================================
//  CommandAddress = 0x60000000   ;   DataAddress = 0x60000000 | (1 << 17)
//  Ecran physique (verre) : 320 x 240 paysage
//
//  Epsilon 15.5.0 pilote la dalle dans DEUX orientations :
//   - MADCTL 0x00 (portrait) : CASET = axe court (0..239), RASET = axe long (0..319)
//                              -> efface / remplit le plein ecran
//   - MADCTL 0xA0 (paysage, MV+MY) : CASET = X ecran (0..319), RASET = Y ecran (0..239)
//                              -> texte, bitmaps, UI
//  L'adresse CASET s'incremente en premier dans les deux cas.
//  RGB565. L'inversion 0x21 (particularite dalle) est ignoree.
// ============================================================================

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;
using Antmicro.Renode.Backends.Display;

namespace Antmicro.Renode.Peripherals.Video
{
    public class NumWorksDisplay : AutoRepaintingVideo, IKnownSize,
        IBytePeripheral, IWordPeripheral, IDoubleWordPeripheral
    {
        public NumWorksDisplay(IMachine machine) : base(machine)
        {
            fb = new ushort[ScreenWidth * ScreenHeight];
            Reconfigure(ScreenWidth, ScreenHeight, PixelFormat.RGB565);
            ResetState();
        }

        public long Size => 0x40000;

        public override void Reset()
        {
            ResetState();
        }

        private void ResetState()
        {
            Array.Clear(fb, 0, fb.Length);
            command = 0;
            paramCount = 0;
            madctl = 0x00;
            caStart = 0; caEnd = 0;
            raStart = 0; raEnd = 0;
            caPtr = 0; raPtr = 0;
        }

        // ---- acces bus : commande si A16=0, donnee si A16=1 -----------------
        // par securite : si Renode passe par un autre gabarit d'acces, on sert
        // quand meme le flux de relecture au lieu de renvoyer 0 (ce qui effacerait).
        public byte ReadByte(long offset)
        {
            if((offset & (1 << 17)) == 0 || command != CMD_RAMRD)
            {
                return 0;
            }
            if(readDummyPending)
            {
                readDummyPending = false;
                return 0;
            }
            return (byte)NextReadByte();
        }

        public uint ReadDoubleWord(long offset)
        {
            uint hi = ReadWord(offset);
            uint lo = ReadWord(offset);
            return (hi << 16) | lo;
        }

        // Epsilon relit l'ecran (pullPixels) : PixelFormatSet 0x06, MemoryRead,
        // un mot bidon, puis 3 mots de 16 bits pour 2 pixels (RGB666 empaquete).
        public ushort ReadWord(long offset)
        {
            if((offset & (1 << 17)) == 0)
            {
                return 0;
            }
            if(command != CMD_RAMRD)
            {
                return 0;
            }
            if(readDummyPending)
            {
                readDummyPending = false;
                return 0;
            }
            int hi = NextReadByte();
            int lo = NextReadByte();
            return (ushort)((hi << 8) | lo);
        }

        private int NextReadByte()
        {
            if(rdPos >= 3)
            {
                ushort c = ReadPixelAtReadPen();
                pulledPixels = pulledPixels + 1;
                if(c != 0xFFFF)
                {
                    pulledInk = pulledInk + 1;
                }
                // RGB565 -> RGB666 : 6 bits utiles en haut de chaque octet
                rdBuf[0] = (byte)(((c >> 11) & 0x1F) << 3);   // R
                rdBuf[1] = (byte)(((c >> 5) & 0x3F) << 2);    // G
                rdBuf[2] = (byte)((c & 0x1F) << 3);           // B
                rdPos = 0;
                AdvanceReadPen();
            }
            int v = rdBuf[rdPos];
            rdPos = rdPos + 1;
            return v;
        }

        private ushort ReadPixelAtReadPen()
        {
            int gx, gy;
            if((madctl & 0x20) != 0)
            {
                gx = rdCa;
                gy = rdRa;
            }
            else
            {
                gx = (ScreenWidth - 1) - rdRa;   // meme miroir qu'en ecriture
                gy = rdCa;
            }
            if((uint)gx < ScreenWidth && (uint)gy < ScreenHeight)
            {
                return fb[gy * ScreenWidth + gx];
            }
            return 0;
        }

        private void AdvanceReadPen()
        {
            rdCa = rdCa + 1;
            if(rdCa > caEnd)
            {
                rdCa = caStart;
                rdRa = rdRa + 1;
                if(rdRa > raEnd)
                {
                    rdRa = raStart;
                }
            }
        }

        public void WriteByte(long offset, byte value) => Access(offset, value);
        public void WriteWord(long offset, ushort value) => Access(offset, value);

        public void WriteDoubleWord(long offset, uint value)
        {
            Access(offset, (ushort)(value & 0xFFFF));
            Access(offset, (ushort)(value >> 16));
        }

        private void Access(long offset, ushort value)
        {
            if((offset & (1 << 17)) != 0)
            {
                HandleData(value);
            }
            else
            {
                HandleCommand((byte)value);
            }
        }

        private void HandleCommand(byte c)
        {
            command = c;
            paramCount = 0;
            cmdSeen[c] = cmdSeen[c] + 1;
            if(c == CMD_RAMWR)
            {
                CloseRamwr();
                caPtr = caStart;
                raPtr = raStart;
                ramwrCount = ramwrCount + 1;
                curCa0 = caStart; curCa1 = caEnd;
                curRa0 = raStart; curRa1 = raEnd;
                curMad = madctl;
                pxThisRamwr = 0;
                inRamwr = true;
            }
            if(c == CMD_RAMRD)
            {
                rdCa = caStart;
                rdRa = raStart;
                rdPos = 3;
                pullCount = pullCount + 1;
                readDummyPending = true;   // premier mot bidon, cf. datasheet
            }
        }

        // capture l ecran en RGB888 brut :  lcd Dump "C:/EmuWorks/ecran.raw"
        public void Dump(string path)
        {
            byte[] rgb = new byte[ScreenWidth * ScreenHeight * 3];
            for(int i = 0; i < fb.Length; i = i + 1)
            {
                ushort c = fb[i];
                rgb[i * 3]     = (byte)((((c >> 11) & 0x1F) * 255) / 31);
                rgb[i * 3 + 1] = (byte)((((c >> 5) & 0x3F) * 255) / 63);
                rgb[i * 3 + 2] = (byte)(((c & 0x1F) * 255) / 31);
            }
            File.WriteAllBytes(path, rgb);
            this.Log(LogLevel.Info, "ecran {0}x{1} -> {2}", ScreenWidth, ScreenHeight, path);
        }

        // ---- serveur d images pour l application hote --------------------
        //  Renode ouvre toujours sa propre fenetre en mode graphique. Pour que
        //  l application soit la seule fenetre visible, on la lance sans
        //  interface et on lui fait servir le framebuffer sur une socket
        //  locale : un octet de requete, une trame RGB565 en reponse.
        public void Serve(int port)
        {
            StopServing();
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                serving = true;
                serverThread = new Thread(ServeLoop);
                serverThread.IsBackground = true;
                serverThread.Start();
                this.Log(LogLevel.Info, "Serveur d images sur 127.0.0.1:{0}", port);
            }
            catch(Exception e)
            {
                this.Log(LogLevel.Error, "Serveur d images impossible : {0}", e.Message);
            }
        }

        public void StopServing()
        {
            serving = false;
            if(listener != null)
            {
                try { listener.Stop(); } catch(Exception) { }
                listener = null;
            }
        }

        private void ServeLoop()
        {
            byte[] frame = new byte[ScreenWidth * ScreenHeight * 2];
            while(serving)
            {
                TcpClient client = null;
                try
                {
                    client = listener.AcceptTcpClient();
                    client.NoDelay = true;
                    NetworkStream stream = client.GetStream();
                    while(serving)
                    {
                        int demande = stream.ReadByte();
                        if(demande < 0)
                        {
                            break;
                        }
                        for(int i = 0; i < fb.Length; i = i + 1)
                        {
                            ushort c = fb[i];
                            frame[2 * i] = (byte)(c & 0xFF);
                            frame[2 * i + 1] = (byte)(c >> 8);
                        }
                        stream.Write(frame, 0, frame.Length);
                    }
                }
                catch(Exception)
                {
                    // client parti ou serveur arrete : on repart en attente
                }
                finally
                {
                    if(client != null)
                    {
                        try { client.Close(); } catch(Exception) { }
                    }
                }
            }
        }

        // compte les pixels non blancs d'une zone :  lcd Ink 31 107 79 120
        public void Ink(int x0, int y0, int x1, int y1)
        {
            int n = 0;
            ushort sample = 0;
            for(int y = y0; y <= y1; y = y + 1)
            {
                for(int x = x0; x <= x1; x = x + 1)
                {
                    if((uint)x >= ScreenWidth || (uint)y >= ScreenHeight)
                    {
                        continue;
                    }
                    ushort c = fb[y * ScreenWidth + x];
                    if(c != 0xFFFF)
                    {
                        n = n + 1;
                        sample = c;
                    }
                }
            }
            this.Log(LogLevel.Info, "zone {0},{1}..{2},{3} : {4} pixels non blancs (exemple 0x{5:X4})",
                x0, y0, x1, y1, n, sample);
        }

        // journal circulaire des derniers rectangles ecrits :  lcd Rects
        public void Rects()
        {
            CloseRamwr();
            this.Log(LogLevel.Info, "--- {0} derniers rectangles (du plus recent au plus ancien) ---", RingSize);
            for(int k = 0; k < RingSize; k = k + 1)
            {
                int i = ((ringPos - 1 - k) % RingSize + RingSize) % RingSize;
                if(rExp[i] == 0)
                {
                    continue;
                }
                this.Log(LogLevel.Info,
                    "  X {0}..{1}  Y {2}..{3}  madctl=0x{4:X2}  px={5}  encre-poussee={6}",
                    rCa0[i], rCa1[i], rRa0[i], rRa1[i], rMad[i], rExp[i], rInk[i]);
            }
        }

        // verifie que le nombre de pixels recus correspond a la fenetre declaree
        private void CloseRamwr()
        {
            if(!inRamwr)
            {
                return;
            }
            inRamwr = false;

            rCa0[ringPos] = curCa0; rCa1[ringPos] = curCa1;
            rRa0[ringPos] = curRa0; rRa1[ringPos] = curRa1;
            rMad[ringPos] = curMad; rExp[ringPos] = pxThisRamwr;
            rInk[ringPos] = curInk;
            ringPos = (ringPos + 1) % RingSize;
            int expected = (curCa1 - curCa0 + 1) * (curRa1 - curRa0 + 1);
            if(pxThisRamwr != expected)
            {
                mismatchCount = mismatchCount + 1;
                if(mismatchStored < MaxStored)
                {
                    mCa0[mismatchStored] = curCa0; mCa1[mismatchStored] = curCa1;
                    mRa0[mismatchStored] = curRa0; mRa1[mismatchStored] = curRa1;
                    mExp[mismatchStored] = expected; mGot[mismatchStored] = pxThisRamwr;
                    mMad[mismatchStored] = curMad;
                    mismatchStored = mismatchStored + 1;
                }
            }
        }

        // diagnostic :  lcd Stats
        public void Stats()
        {
            this.Log(LogLevel.Info,
                "RAMWR={0} pixels={1} hors-ecran={2} donnees-perdues={3} madctl=0x{4:X2}",
                ramwrCount, pixelsWritten, droppedPixels, lostData, madctl);
            for(int i = 0; i < 256; i = i + 1)
            {
                if(cmdSeen[i] > 0 && i != CMD_CASET && i != CMD_RASET && i != CMD_RAMWR
                   && i != CMD_MADCTL && i != CMD_RAMRD && i != CMD_COLMOD)
                {
                    this.Log(LogLevel.Info, "commande NON GEREE 0x{0:X2} vue {1} fois", i, cmdSeen[i]);
                }
            }
            this.Log(LogLevel.Info, "fenetres vues : CASET {0}..{1}  RASET {2}..{3}",
                minCa, maxCa, minRa, maxRa);
            CloseRamwr();
            this.Log(LogLevel.Info, "rectangles incoherents : {0}", mismatchCount);
            this.Log(LogLevel.Info, "relectures : {0} pixels={1} dont encre={2}", pullCount, pulledPixels, pulledInk);
            for(int i = 0; i < mismatchStored; i = i + 1)
            {
                this.Log(LogLevel.Info,
                    "  CASET {0}..{1} RASET {2}..{3} madctl=0x{4:X2} attendu={5} recu={6}",
                    mCa0[i], mCa1[i], mRa0[i], mRa1[i], mMad[i], mExp[i], mGot[i]);
            }
        }

        private void HandleData(ushort d)
        {
            switch(command)
            {
                case CMD_MADCTL:
                    madctl = (byte)d;
                    break;

                case CMD_CASET:
                    p[paramCount++ & 0x3] = (byte)d;
                    if(paramCount == 4)
                    {
                        caStart = (p[0] << 8) | p[1];
                        caEnd   = (p[2] << 8) | p[3];
                        if(caStart < minCa) { minCa = caStart; }
                        if(caEnd > maxCa) { maxCa = caEnd; }
                    }
                    break;

                case CMD_RASET:
                    p[paramCount++ & 0x3] = (byte)d;
                    if(paramCount == 4)
                    {
                        raStart = (p[0] << 8) | p[1];
                        raEnd   = (p[2] << 8) | p[3];
                        if(raStart < minRa) { minRa = raStart; }
                        if(raEnd > maxRa) { maxRa = raEnd; }
                    }
                    break;

                case CMD_RAMWR:
                    PutPixel(d);
                    break;

                case CMD_COLMOD:
                    colmod = (byte)d;
                    break;

                default:
                    // donnee arrivant apres une commande non geree : elle est perdue
                    lostData = lostData + 1;
                    break;
            }
        }

        private void PutPixel(ushort color)
        {
            int gx, gy;
            if((madctl & 0x20) != 0)
            {
                // paysage (MV=1) : CASET = X ecran, RASET = Y ecran
                gx = caPtr;
                gy = raPtr;
            }
            else
            {
                // portrait (MV=0) : CASET = Y ecran, RASET = X ecran MIROITE
                //   Epsilon : y_start = Width - (r.x() + r.width()) ; y_end = Width - r.x() - 1
                gx = (ScreenWidth - 1) - raPtr;
                gy = caPtr;
            }

            pixelsWritten = pixelsWritten + 1;
            if(pxThisRamwr == 0)
            {
                curInk = 0;
            }
            if(color != 0xFFFF)
            {
                curInk = curInk + 1;
            }
            pxThisRamwr = pxThisRamwr + 1;
            if((uint)gx < ScreenWidth && (uint)gy < ScreenHeight)
            {
                fb[gy * ScreenWidth + gx] = color;
            }
            else
            {
                droppedPixels = droppedPixels + 1;
            }

            // l'adresse CASET s'incremente en premier
            caPtr++;
            if(caPtr > caEnd)
            {
                caPtr = caStart;
                raPtr++;
                if(raPtr > raEnd)
                {
                    raPtr = raStart;
                }
            }
        }

        protected override void Repaint()
        {
            for(int i = 0; i < fb.Length; i++)
            {
                ushort c = fb[i];
                buffer[2 * i]     = (byte)(c & 0xFF);
                buffer[2 * i + 1] = (byte)(c >> 8);
            }
        }

        private readonly ushort[] fb;
        private TcpListener listener;
        private Thread serverThread;
        private volatile bool serving;
        private readonly byte[] p = new byte[4];

        private byte command;
        private byte madctl;
        private int paramCount;
        private int caStart, caEnd, raStart, raEnd;
        private int caPtr, raPtr;

        private const int ScreenWidth = 320;
        private const int ScreenHeight = 240;

        // diagnostic
        private readonly int[] cmdSeen = new int[256];
        private int ramwrCount;
        private int droppedPixels;
        private int lostData;
        private long pixelsWritten;
        private int minCa = 99999;
        private int maxCa = -1;
        private int minRa = 99999;
        private int maxRa = -1;

        private const int RingSize = 24;
        private int ringPos;
        private readonly int[] rCa0 = new int[RingSize];
        private readonly int[] rCa1 = new int[RingSize];
        private readonly int[] rRa0 = new int[RingSize];
        private readonly int[] rRa1 = new int[RingSize];
        private readonly int[] rExp = new int[RingSize];
        private readonly byte[] rMad = new byte[RingSize];
        private readonly int[] rInk = new int[RingSize];
        private int curInk;
        private int pullCount;
        private long pulledPixels;
        private long pulledInk;

        private bool inRamwr;
        private int pxThisRamwr;
        private int curCa0, curCa1, curRa0, curRa1;
        private byte curMad;
        private int mismatchCount;
        private int mismatchStored;
        private const int MaxStored = 12;
        private readonly int[] mCa0 = new int[MaxStored];
        private readonly int[] mCa1 = new int[MaxStored];
        private readonly int[] mRa0 = new int[MaxStored];
        private readonly int[] mRa1 = new int[MaxStored];
        private readonly int[] mExp = new int[MaxStored];
        private readonly int[] mGot = new int[MaxStored];
        private readonly byte[] mMad = new byte[MaxStored];

        // relecture de l'ecran (pullPixels)
        private readonly byte[] rdBuf = new byte[3];
        private int rdPos = 3;
        private int rdCa, rdRa;
        private bool readDummyPending;
        private byte colmod;

        private const byte CMD_MADCTL = 0x36;
        private const byte CMD_CASET  = 0x2A;
        private const byte CMD_RASET  = 0x2B;
        private const byte CMD_RAMWR  = 0x2C;
        private const byte CMD_RAMRD  = 0x2E;
        private const byte CMD_COLMOD = 0x3A;
    }
}

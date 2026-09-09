// ============================================================================
//  NumWorksKeyboard  -  matrice clavier NumWorks N0110
// ============================================================================
//  9 lignes  = broches GPIOA {1,0,2,3,4,5,6,7,8}
//  6 colonnes = sorties GPIO Col0..Col5  (-> GPIOC 0..5 dans le .repl)
//  Key = ligne * 6 + colonne   (layout_B3 de epsilon 15.5.0)
//
//  Moniteur : keyboard PressKey "EXE" / ReleaseKey / TapKey "SEVEN" / ReleaseAll
//  Vrai clavier PC : implemente IKeyboard -> menu "Keyboard:" de la fenetre ecran
//
//  Mapping clavier PC :
//    0-9                  -> chiffres          Entree      -> EXE
//    fleches              -> fleches           Retour arr. -> effacer
//    Echap                -> retour            Debut(Home) -> accueil
//    + - * /              -> operateurs        . ou ,      -> point / virgule
//    ( )                  -> parentheses       Tab         -> shift   Verr.Maj -> alpha
//    x  p  s  c  t  e  l  -> x,n,t  pi  sin cos tan exp ln
// ============================================================================

using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Logging;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Input
{
    public class NumWorksKeyboard : IGPIOReceiver, IDoubleWordPeripheral, IKnownSize, IKeyboard
    {
        public long Size
        {
            get { return 0x100; }
        }

        public uint ReadDoubleWord(long offset)
        {
            return 0;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
        }

        public NumWorksKeyboard()
        {
            Col0 = new GPIO();
            Col1 = new GPIO();
            Col2 = new GPIO();
            Col3 = new GPIO();
            Col4 = new GPIO();
            Col5 = new GPIO();
            rowHigh = new bool[9];
            keyDown = new bool[54];
            releaseAt = new int[54];
            Reset();
        }

        public GPIO Col0 { get; private set; }
        public GPIO Col1 { get; private set; }
        public GPIO Col2 { get; private set; }
        public GPIO Col3 { get; private set; }
        public GPIO Col4 { get; private set; }
        public GPIO Col5 { get; private set; }

        public void Reset()
        {
            for(int i = 0; i < rowHigh.Length; i = i + 1)
            {
                rowHigh[i] = true;
            }
            for(int i = 0; i < keyDown.Length; i = i + 1)
            {
                keyDown[i] = false;
                releaseAt[i] = -1;
            }
            scanTicks = 0;
            Refresh();
        }

        public void OnGPIO(int number, bool value)
        {
            if(number >= 0 && number < rowHigh.Length)
            {
                bool wasHigh = rowHigh[number];
                rowHigh[number] = value;
                // front descendant = le firmware vient de scruter cette ligne
                if(wasHigh && !value)
                {
                    scanTicks = scanTicks + 1;
                    ExpireTaps();
                }
            }
            Refresh();
        }

        // relache les touches posees par TapKey une fois qu'elles ont ete vues
        private void ExpireTaps()
        {
            for(int i = 0; i < releaseAt.Length; i = i + 1)
            {
                if(releaseAt[i] >= 0 && scanTicks >= releaseAt[i])
                {
                    releaseAt[i] = -1;
                    keyDown[i] = false;
                }
            }
        }

        public void PressKey(string name)
        {
            SetKey(name, true);
        }

        public void ReleaseKey(string name)
        {
            SetKey(name, false);
        }

        // appui bref MAIS maintenu assez longtemps pour que le balayage matriciel
        // du firmware le voie (un press/release instantane passe entre deux scans)
        public void TapKey(string name)
        {
            int key = KeyIndex(name);
            if(key < 0)
            {
                this.Log(LogLevel.Warning, "Touche NumWorks inconnue : {0}", name);
                return;
            }
            keyDown[key] = true;
            releaseAt[key] = scanTicks + HoldTicks;
            Refresh();
        }

        public void ReleaseAll()
        {
            for(int i = 0; i < keyDown.Length; i = i + 1)
            {
                keyDown[i] = false;
                releaseAt[i] = -1;
            }
            Refresh();
        }

        // ---- IKeyboard : vrai clavier PC via la fenetre ecran --------------
        public void Press(KeyScanCode scanCode)
        {
            SetScan(scanCode, true);
        }

        public void Release(KeyScanCode scanCode)
        {
            SetScan(scanCode, false);
        }

        private void SetScan(KeyScanCode s, bool down)
        {
            int key = ScanToKey(s);
            if(key >= 0)
            {
                keyDown[key] = down;
                Refresh();
            }
        }

        private static int ScanToKey(KeyScanCode s)
        {
            switch(s)
            {
                case KeyScanCode.Number0: case KeyScanCode.Keypad0: return 48;
                case KeyScanCode.Number1: case KeyScanCode.Keypad1: return 42;
                case KeyScanCode.Number2: case KeyScanCode.Keypad2: return 43;
                case KeyScanCode.Number3: case KeyScanCode.Keypad3: return 44;
                case KeyScanCode.Number4: case KeyScanCode.Keypad4: return 36;
                case KeyScanCode.Number5: case KeyScanCode.Keypad5: return 37;
                case KeyScanCode.Number6: case KeyScanCode.Keypad6: return 38;
                case KeyScanCode.Number7: case KeyScanCode.Keypad7: return 30;
                case KeyScanCode.Number8: case KeyScanCode.Keypad8: return 31;
                case KeyScanCode.Number9: case KeyScanCode.Keypad9: return 32;

                case KeyScanCode.Left:  return 0;
                case KeyScanCode.Up:    return 1;
                case KeyScanCode.Down:  return 2;
                case KeyScanCode.Right: return 3;
                case KeyScanCode.Enter: case KeyScanCode.KeypadEnter: return 52; // EXE
                case KeyScanCode.Escape: return 5;   // BACK
                case KeyScanCode.BackSpace: case KeyScanCode.Delete: return 17;
                case KeyScanCode.Home: return 6;
                case KeyScanCode.Tab: case KeyScanCode.ShiftL: case KeyScanCode.ShiftR: return 12; // SHIFT
                case KeyScanCode.CapsLock: return 13; // ALPHA

                case KeyScanCode.OemPlus:  case KeyScanCode.KeypadPlus:     return 45;
                case KeyScanCode.OemMinus: case KeyScanCode.KeypadMinus:    return 46;
                case KeyScanCode.KeypadMultiply: return 39;
                case KeyScanCode.KeypadDivide:   return 40;
                case KeyScanCode.OemPeriod: case KeyScanCode.KeypadComma:   return 49; // DOT
                case KeyScanCode.OemComma: return 22; // COMMA
                case KeyScanCode.OemOpenBrackets:  return 33;
                case KeyScanCode.OemCloseBrackets: return 34;

                case KeyScanCode.X: return 14; // XNT
                case KeyScanCode.P: return 27; // PI
                case KeyScanCode.S: return 24; // SINE
                case KeyScanCode.C: return 25; // COSINE
                case KeyScanCode.T: return 26; // TANGENT
                case KeyScanCode.E: return 18; // EXP
                case KeyScanCode.L: return 19; // LN
                case KeyScanCode.R: return 28; // SQRT

                default: return -1;
            }
        }

        private void SetKey(string name, bool down)
        {
            int key = KeyIndex(name);
            if(key < 0)
            {
                this.Log(LogLevel.Warning, "Touche NumWorks inconnue : {0}", name);
                return;
            }
            keyDown[key] = down;
            releaseAt[key] = -1;   // appui manuel : pas de relachement automatique
            Refresh();
        }

        private void Refresh()
        {
            SetColumn(0, Col0);
            SetColumn(1, Col1);
            SetColumn(2, Col2);
            SetColumn(3, Col3);
            SetColumn(4, Col4);
            SetColumn(5, Col5);
        }

        private void SetColumn(int col, GPIO line)
        {
            bool low = false;
            for(int row = 0; row < 9; row = row + 1)
            {
                int pin = RowPin(row);
                if(keyDown[row * 6 + col] && !rowHigh[pin])
                {
                    low = true;
                }
            }
            line.Set(!low);
        }

        // RowPins = {1,0,2,3,4,5,6,7,8}
        private static int RowPin(int row)
        {
            if(row == 0)
            {
                return 1;
            }
            if(row == 1)
            {
                return 0;
            }
            return row;
        }

        // table "NOM=valeur;" ; valeur = ligne*6 + colonne (layout_B3)
        private const string Map =
            "LEFT=0;UP=1;DOWN=2;RIGHT=3;OK=4;BACK=5;HOME=6;ONOFF=8;" +
            "SHIFT=12;ALPHA=13;XNT=14;VAR=15;TOOLBOX=16;BACKSPACE=17;DELETE=17;" +
            "EXP=18;LN=19;LOG=20;IMAGINARY=21;COMMA=22;POWER=23;" +
            "SINE=24;COSINE=25;TANGENT=26;PI=27;SQRT=28;SQUARE=29;" +
            "SEVEN=30;EIGHT=31;NINE=32;LEFTPARENTHESIS=33;RIGHTPARENTHESIS=34;" +
            "FOUR=36;FIVE=37;SIX=38;MULTIPLICATION=39;DIVISION=40;" +
            "ONE=42;TWO=43;THREE=44;PLUS=45;MINUS=46;" +
            "ZERO=48;DOT=49;EE=50;ANS=51;EXE=52;ENTER=52;" +
            "7=30;8=31;9=32;4=36;5=37;6=38;1=42;2=43;3=44;0=48;";

        private static int KeyIndex(string name)
        {
            string needle = ";" + name.ToUpper() + "=";
            string hay = ";" + Map;
            int at = hay.IndexOf(needle);
            if(at < 0)
            {
                return -1;
            }
            int start = at + needle.Length;
            int end = hay.IndexOf(';', start);
            string num = hay.Substring(start, end - start);
            return int.Parse(num);
        }

        private readonly bool[] rowHigh;
        private readonly bool[] keyDown;
        private readonly int[] releaseAt;
        private int scanTicks;

        // 9 lignes par balayage -> ~5 balayages complets
        private const int HoldTicks = 45;
    }
}

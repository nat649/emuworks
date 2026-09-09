// ============================================================================
//  NumWorksCrc  -  unite de calcul CRC du STM32F730  (base 0x40023000)
// ============================================================================
//  Epsilon s'en sert pour ses verifications d'integrite (Ion::crc32).
//  Registres :
//    0x00 DR   ecriture = alimente le CRC ; lecture = CRC courant
//    0x04 IDR  octet libre (scratch)
//    0x08 CR   bit 0 = RESET  (remet DR a la valeur INIT)
//    0x10 INIT valeur initiale (defaut 0xFFFFFFFF)
//    0x14 POL  polynome (defaut 0x04C11DB7)
//  CRC-32 classique STM32 : MSB first, sans reflexion d'entree/sortie.
// ============================================================================

using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // le CRC du STM32 accepte aussi les ecritures 8 et 16 bits
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class NumWorksCrc : IDoubleWordPeripheral, IKnownSize
    {
        public NumWorksCrc()
        {
            Reset();
        }

        public long Size
        {
            get { return 0x400; }
        }

        public void Reset()
        {
            init = 0xFFFFFFFF;
            poly = 0x04C11DB7;
            crc = init;
            idr = 0;
        }

        public uint ReadDoubleWord(long offset)
        {
            if(offset == 0x00)
            {
                return crc;
            }
            if(offset == 0x04)
            {
                return idr;
            }
            if(offset == 0x10)
            {
                return init;
            }
            if(offset == 0x14)
            {
                return poly;
            }
            return 0;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(offset == 0x00)
            {
                crc = Feed(crc, value);
                return;
            }
            if(offset == 0x04)
            {
                idr = value & 0xFF;
                return;
            }
            if(offset == 0x08)
            {
                if((value & 0x1) != 0)
                {
                    crc = init;
                }
                return;
            }
            if(offset == 0x10)
            {
                init = value;
                return;
            }
            if(offset == 0x14)
            {
                poly = value;
                return;
            }
        }

        private uint Feed(uint current, uint data)
        {
            uint c = current ^ data;
            for(int i = 0; i < 32; i = i + 1)
            {
                if((c & 0x80000000) != 0)
                {
                    c = (c << 1) ^ poly;
                }
                else
                {
                    c = c << 1;
                }
            }
            return c;
        }

        private uint crc;
        private uint init;
        private uint poly;
        private uint idr;
    }
}

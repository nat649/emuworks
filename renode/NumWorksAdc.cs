// ============================================================================
//  NumWorksAdc  -  ADC1 du STM32F730, mesure de la tension batterie
// ============================================================================
//  Epsilon (ion/src/device/shared/drivers/battery.cpp) fait :
//      CR2.SWSTART = 1 ; while(SR.EOC != 1) ; value = DR
//      tension = 2.0 * (2.8 * value) / 0xFFF     (pont diviseur / Vref)
//  Seuils : < 3.60 V vide, < 3.70 V faible, < 3.80 V moyenne, sinon pleine.
//
//  ATTENTION : DR est a l'offset 0x4C (et non 0x34, qui est SQR3).
//
//  Moniteur :  adc SetMillivolts 4050      -> regle la batterie a 4,05 V
//              adc Millivolts              -> lit la valeur courante
// ============================================================================

using System;
using Antmicro.Renode.Core;
using Antmicro.Renode.Peripherals.Bus;

namespace Antmicro.Renode.Peripherals.Miscellaneous
{
    // Epsilon lit DR en 16 bits (uint16_t value = ADC.DR()->get()) : sans ces
    // traductions, Renode refuse l'acces et renvoie 0 (batterie vide).
    [AllowedTranslations(AllowedTranslation.ByteToDoubleWord | AllowedTranslation.WordToDoubleWord)]
    public class NumWorksAdc : IDoubleWordPeripheral, IKnownSize
    {
        public NumWorksAdc()
        {
            Reset();
        }

        public long Size
        {
            get { return 0x400; }
        }

        public void Reset()
        {
            millivolts = 4050;   // batterie pleine par defaut
            cr2 = 0;
        }

        // tension batterie simulee, en millivolts
        public int Millivolts
        {
            get { return millivolts; }
        }

        public void SetMillivolts(int mv)
        {
            if(mv < 0)
            {
                mv = 0;
            }
            if(mv > 5600)
            {
                mv = 5600;   // 2 * 2.8 V = plafond du pont diviseur
            }
            millivolts = mv;
        }

        public uint ReadDoubleWord(long offset)
        {
            if(offset == 0x00)
            {
                // SR : EOC (bit 1) et STRT (bit 4) toujours prets
                return 0x12;
            }
            if(offset == 0x08)
            {
                return cr2;
            }
            if(offset == 0x4C)
            {
                // DR : valeur inverse de la formule d'Epsilon
                //   mv = 2.0 * 2.8 * value / 4095   ->   value = mv * 4095 / 5600
                long value = ((long)millivolts * 4095) / 5600;
                if(value > 0xFFF)
                {
                    value = 0xFFF;
                }
                return (uint)value;
            }
            return 0;
        }

        public void WriteDoubleWord(long offset, uint value)
        {
            if(offset == 0x08)
            {
                cr2 = value;
            }
        }

        private int millivolts;
        private uint cr2;
    }
}

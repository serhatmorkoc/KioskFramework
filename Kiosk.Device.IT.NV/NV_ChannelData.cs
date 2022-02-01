using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV
{
   public class NV_ChannelData
    {
        public int Value;
        public byte Channel;
        public char[] Currency;
        public int Level;
        public bool Recycling;

        public NV_ChannelData()
        {
            Value = 0;
            Channel = 0;
            Currency = new char[3];
            Level = 0;
            Recycling = false;
        }
    }
}

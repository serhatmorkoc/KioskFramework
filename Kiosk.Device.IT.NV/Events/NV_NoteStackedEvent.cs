using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV.Events
{
    public class NV_NoteStackedEvent : EventArgs
    {
        public int Level { get; private set; }
        public int Value { get; private set; }
        public char[] Currency { get; private set; }

        public NV_NoteStackedEvent(int level, int value, char[] currency)
        {
            Level = level;
            Value = value;
            Currency = currency;
        }

        //public NV_ChannelData ChannelData { get; set; }

        //public NV_NoteStackedEvent()
        //{
        //    ChannelData = new NV_ChannelData();
        //}
    }
}

using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV.Events
{
    public class NV_NoteReadEscrowedEvent : EventArgs
    {
        public int Level { get; private set; }
        public int Value { get; private set; }
        public char[] Currency { get; private set; }

        public NV_NoteReadEscrowedEvent(int level, int value, char[] currency)
        {
            Level = value;
            Value = value;
            Currency = currency;
        }
    }
}

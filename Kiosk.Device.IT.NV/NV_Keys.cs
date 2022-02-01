using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV
{
    internal class NV_Keys
    {
        public UInt64 GeneratorKey;
        public UInt64 ModulusKey;
        public UInt64 HostRandom;
        public UInt64 HostInterKey;
        public UInt64 SlaveInterKey;
        public UInt64 HostKey;
        public UInt64 FixedKey = 0x0123456701234567;
        public byte[] EncryptKey;
    }
}

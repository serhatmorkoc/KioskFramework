using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV
{
    public class NV_UnitData
    {
        public string UnitType { get; set; }
        public string FirmwareVersion { get; set; }
        public string CountryCode { get; set; }
        public int ValueMultiplier { get; set; }
        public byte ProtocolVersion { get; set; }

        public NV_UnitData()
        {
            UnitType = string.Empty;
            FirmwareVersion = string.Empty;
            CountryCode = string.Empty;
            ValueMultiplier = 0;
            ProtocolVersion = 0x00;
        }
    }
}

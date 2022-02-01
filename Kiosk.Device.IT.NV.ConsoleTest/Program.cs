using Kiosk.Device.IT.NV.Events;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;
using System;

namespace Kiosk.Device.IT.NV.ConsoleTest
{
    class Program
    {
        private static int _realMultiplierValue { get; set; }
        private static NV_Manager _nv = new NV_Manager();

        static void Main(string[] args)
        {
            Log.Logger = new LoggerConfiguration()
                        .WriteTo.Console()
                        .CreateLogger();

            _nv.PortName = "COM11";
            _nv.KioskName = "TEST";
            _nv.TransactionNumber = Guid.NewGuid().ToString();
            _nv.DebugMode = false;

            _nv.NoteRead += _nv_ReadNote;
            _nv.NoteCreditAccepted += _nv_NoteAccepted;
            _nv.NoteStacked += _nv_NoteStacked;

            _nv.Connect();
            //_nv.Disconnect();

            var setupDeviceResult = SetupDevice();
            if (!setupDeviceResult)
            {
                Console.WriteLine("setup device fail");
                _nv.Disconnect();
                return;
            }

            #region Get Counters

            Console.WriteLine(new string('-', 50));
            var resultGetCounters = _nv.GetCounters();
            var text = string.Empty;
            foreach (var item in resultGetCounters.MetaData)
            {
                text += $"{item.Key} : {item.Value}\n";
            }
            Console.WriteLine(text);
            Console.WriteLine(new string('-',50));

            #endregion

            #region Get UnitData

            var resultUnitData = _nv.UnitData();
            text = string.Empty;
            foreach (var item in resultUnitData.MetaData)
            {
                text += $"{item.Key} : {item.Value}\n";
            }
            Console.WriteLine(text);
            Console.WriteLine(new string('-', 50));

            #endregion

            _nv.EnableValidator();
            //_nv.DisableValidator();

            Console.ReadLine();
        }

        static bool SetupDevice()
        {
            _nv.EncryptionStatus = false;

            var sync = _nv.Sync();
            var hostProtocolVersion = _nv.HostProtocolVersion();
            var initEncryption = _nv.InitEncryption();

            _nv.EncryptionStatus = true;

            var setupRequest = _nv.SetupRequest();
            _nv.SetChannelInhibits();
            _nv.ConfigureBezel();

            if (sync.ResponseStatus == NV_ResponseStatus.SSP_RESPONSE_OK &&
                hostProtocolVersion.ResponseStatus == NV_ResponseStatus.SSP_RESPONSE_OK &&
                initEncryption == true &&
                setupRequest.ResponseStatus == NV_ResponseStatus.SSP_RESPONSE_OK)
            {

                _realMultiplierValue = _nv.RealMultiplierValue;

                return true;
            }

            return false;
        }

        static void _nv_ReadNote(object sender, NV_NoteReadEventArgs e)
        {
            //Console.WriteLine("NOTE_READING");
        }

        static void _nv_NoteAccepted(object sender, NV_NoteCreditAcceptedEvent e)
        {
            Log.Information("#EVENT NOTE_CREDIT VALUE: " + (e.Value / _realMultiplierValue) + " " + new String(e.Currency));
        }

        static void _nv_NoteStacked(object sender, NV_NoteStackedEvent e)
        {
            Log.Information("#EVENT NOTE_STACKED VALUE: " + (e.Value / _realMultiplierValue) + " " + new String(e.Currency));
        }
    }
}

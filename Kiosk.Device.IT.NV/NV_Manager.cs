//+------------------------------------------------------------+
//|                     Encryption Layer                       |
//+------------------------------------------------------------+
//
//+------+----------------+----------+--------+--------+-------+
//| STX  |  SEQ/SLAVE ID  |  LENGTH  |  DATA  |  CRCL  |  CRCH |
//+------+----------------+----------+--------+--------+-------+
//
//
//+------------------------------------------------------------+
//|DATA                                                        |
//+---------+--------------------------------------------------+
//|STEX     |             Encrypted Data                       |
//+---------+--------------------------------------------------+
//
//
//+------------------------------------------------------------+
//|Encrypted Data                                              |
//+---------+----------+---------+------------+---------+------+
//|eLENGTH  |  eCOUNT  |  eDATA  |  ePACKING  |  eCRCL  | eCRCH|
//+---------+----------+---------+------------+---------+------+
using Kiosk.Device.IT.NV.Events;
using Serilog;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Kiosk.Device.IT.NV
{
    public partial class NV_Manager
    {
        #region Events

        public event EventHandler<NV_DispensedEvent> Dispensed;
        public event EventHandler<NV_NoteReadEscrowedEvent> NoteReadEscrowed;
        public event EventHandler<NV_NoteCreditAcceptedEvent> NoteCreditAccepted;
        public event EventHandler<NV_NoteReadEventArgs> NoteRead;
        public event EventHandler<NV_NoteStackedEvent> NoteStacked;
        public event EventHandler<NV_NoteStoredInPayoutEvent> NoteStoredInPayout;
        public event EventHandler<NV_PayoutEmptiedEvent> PayoutEmptied;
        public event EventHandler<NV_PayoutSmartEmptiedEvent> PayoutSmartEmptied;

        protected virtual void OnDispensed(NV_DispensedEvent e)
        {
            EventHandler<NV_DispensedEvent> handler = Dispensed;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnNoteReadEscrowed(NV_NoteReadEscrowedEvent e)
        {
            EventHandler<NV_NoteReadEscrowedEvent> handler = NoteReadEscrowed;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnNoteCreditAccepted(NV_NoteCreditAcceptedEvent e)
        {
            EventHandler<NV_NoteCreditAcceptedEvent> handler = NoteCreditAccepted;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnNoteRead(NV_NoteReadEventArgs e)
        {
            EventHandler<NV_NoteReadEventArgs> handler = NoteRead;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnNoteStacked(NV_NoteStackedEvent e)
        {
            EventHandler<NV_NoteStackedEvent> handler = NoteStacked;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnNoteStoredInPayout(NV_NoteStoredInPayoutEvent e)
        {
            EventHandler<NV_NoteStoredInPayoutEvent> handler = NoteStoredInPayout;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnPayoutEmptied(NV_PayoutEmptiedEvent e)
        {
            EventHandler<NV_PayoutEmptiedEvent> handler = PayoutEmptied;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        protected virtual void OnPayoutSmartEmptied(NV_PayoutSmartEmptiedEvent e)
        {
            EventHandler<NV_PayoutSmartEmptiedEvent> handler = PayoutSmartEmptied;
            if (handler != null)
            {
                handler(this, e);
            }
        }

        #endregion

        #region Props

        public string PortName;
        public string KioskName;
        public string TransactionNumber;
        public int RealMultiplierValue;
        public bool EncryptionStatus;
        public bool DebugMode;
        public List<NV_ChannelData> ChannelDataList;
        
        private readonly Object _lockPoll;
        private readonly Object _lockCommand;
        private readonly SerialPort _serialPort;
        private NV_Keys _keys;
        private byte _sequence;
        private int _count;
        private byte _protocolVersion;
        private string _unitType;
        private System.Timers.Timer _listener;
        private bool _isListening;
        private string _deviceType;

        #endregion

        #region Ctor

        public NV_Manager()
        {
            try
            {
                _serialPort = new SerialPort
                {
                    BaudRate = 9600,
                    StopBits = StopBits.Two,
                    Parity = Parity.None,
                    DataBits = 8,
                    Handshake = Handshake.None,
                    WriteTimeout = 500,
                    ReadTimeout = 500,
                };

                _lockPoll = new object();
                _lockCommand = new object();
                _keys = new NV_Keys();
                _protocolVersion = 0x06;
                _unitType = "UNKNOWN_TYPE";

                EncryptionStatus = false;
                DebugMode = false;
                ChannelDataList = new List<NV_ChannelData>();

                _listener = new System.Timers.Timer();
                _listener.Interval = 400;
                _listener.Enabled = false;
                _listener.Elapsed += new System.Timers.ElapsedEventHandler(_listener_Elapsed);
                _isListening = false;
            }
            catch (Exception err)
            {
                Log.Error(err, $"Kiosk Name:{KioskName} Device Type:{_deviceType} Transaction Number:{TransactionNumber} Error Message: {err.StackTrace}");

                throw new Exception(err.Message, err);
            }
        }

        #endregion

        #region Public Methods

        public bool Connect()
        {
            try
            {
                if (_serialPort.IsOpen)
                    _serialPort.Close();

                _serialPort.PortName = PortName;
                _serialPort.Open();

                return true;

            }
            catch (Exception err)
            {
                Log.Error(err, $"Kiosk Name:{KioskName} Device Type:{_deviceType} Transaction Number:{TransactionNumber} Error Message: {err.StackTrace}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_serialPort.IsOpen)
                {
                    _serialPort.Close();
                }

                //todo:
                _isListening = false;
                _listener.Enabled = false;
                _listener.Stop();
            }
            catch (Exception err)
            {
                Log.Error(err, $"Kiosk Name:{KioskName} Device Type:{_deviceType} Transaction Number:{TransactionNumber} Error Message: {err.StackTrace}");

            }
        }

        public bool InitEncryption()
        {
            _keys.GeneratorKey = NV_Util.GeneratePrimeRandomNumber();
            _keys.ModulusKey = NV_Util.GeneratePrimeRandomNumber();
            _keys.HostRandom = NV_Util.GenerateRandomNumber() % 2147483648UL;

            if (_keys.GeneratorKey < _keys.ModulusKey)
            {
                var g = _keys.GeneratorKey;
                _keys.GeneratorKey = _keys.ModulusKey;
                _keys.ModulusKey = g;
            }

            _keys.HostInterKey = NV_Util.XpowYmodN(_keys.GeneratorKey,
                                                                    _keys.HostRandom,
                                                                    _keys.ModulusKey);


            var sg = SetGenerator(_keys.GeneratorKey);
            if (sg.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            var sm = SetModulus(_keys.ModulusKey);
            if (sg.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            var rke = RequestKeyExchange(_keys.HostInterKey);
            if (sg.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            //todo:
            _count = 0;

            return true;
        }

        public NV_Response Reset()
        {
            DisableValidator();

            byte[] data = new byte[1];
            data[0] = CMD_RESET;

            return SendCommand(CMD_RESET, data);
        }

        public NV_Response SetChannelInhibits()
        {
            byte[] data = new byte[3];
            data[0] = CMD_SET_CHANNEL_INHIBITS;
            data[1] = 0xFF;
            data[2] = 0xFF;

            var response = SendCommand(CMD_SET_CHANNEL_INHIBITS, data);
            return response;
        }

        public NV_Response DisplayOn()
        {
            byte[] data = new byte[1];
            data[0] = CMD_DISPLAY_ON;

            return SendCommand(CMD_DISPLAY_ON, data);
        }

        public NV_Response DisplayOff()
        {
            byte[] data = new byte[1];
            data[0] = CMD_DISPLAY_OFF;

            return SendCommand(CMD_DISPLAY_OFF, data);
        }

        public NV_Response SetupRequest()
        {
            byte[] data = new byte[1];
            data[0] = CMD_SETUP_REQUEST;

            var response = SendCommand(CMD_SETUP_REQUEST, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            //todo: try-catch

            var result = response.Data[1..];

            string unitType = (result[0]) switch
            {
                0x00 => "VALIDATOR",
                0x03 => "SMART_HOPPER",
                0x06 => "SMART_PAYOUT",
                0x07 => "NV11",
                0x0D => "TEBS",
                _ => "UNKNOWN_TYPE",
            };

            _unitType = _deviceType = unitType;

            string firmwareVersion = Encoding.ASCII.GetString(result[1..5]);
            string countryCode = Encoding.ASCII.GetString(result[5..8]);

            byte[] valueMultiplierArray = result[8..11];
            if (BitConverter.IsLittleEndian)
                Array.Reverse(valueMultiplierArray);

            int valueMultiplier = valueMultiplierArray[0] * 100 +
                                  valueMultiplierArray[1] * 10 +
                                  valueMultiplierArray[2] * 1;

            RealMultiplierValue = valueMultiplier;

            int n = result[11];
            byte[] channelValue = result[12..(12 + n)];
            byte[] channelSecurity = result[(12 + n)..(12 + (n * 2))];
            byte[] realValueMultiplier = result[(12 + (n * 2))..(12 + (n * 2) + 3)];
            int protocolVersion = result[(15 + (n * 2))..(15 + (n * 2) + 1)][0];

            ChannelDataList.Clear();

            if (protocolVersion >= 6)
            {
                for (var i = 0; i < n; i++)
                {
                    var channelData = new NV_ChannelData();

                    channelData.Currency[0] = (char)result[15 + (n * 2) + 1 + (i * 3)];
                    channelData.Currency[1] = (char)result[15 + (n * 2) + 2 + (i * 3)];
                    channelData.Currency[2] = (char)result[15 + (n * 2) + 3 + (i * 3)];
                    channelData.Channel = (byte)(i + 1);
                    channelData.Value = result[(16 + (n * 5) + (i * 4))..(20 + (n * 5) + (i * 4))][0] * valueMultiplier;


                    if (unitType == "SMART_PAYOUT")
                    {
                        channelData.Level = GetDenominationLevel(channelData.Value, channelData.Currency);
                        channelData.Recycling = GetDenominationRoute(channelData.Value, channelData.Currency);
                    }

                    ChannelDataList.Add(channelData);
                }

            }
            else
            {
                return response;
            }

            return response;
        }

        public NV_Response HostProtocolVersion()
        {
            byte[] data = new byte[2];
            data[0] = CMD_HOST_PROTOCOL_VERSION;
            data[1] = _protocolVersion;

            var response = SendCommand(CMD_HOST_PROTOCOL_VERSION, data);
            return response;
        }

        public NV_Response SetGenerator(UInt64 generatorKey)
        {
            byte[] buff = new byte[8];
            buff = BitConverter.GetBytes(generatorKey);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(buff);

            var data = new List<byte>
            {
                CMD_SET_GENERATOR
            };
            data.AddRange(buff);

            return SendCommand(CMD_SET_GENERATOR, data.ToArray());
        }

        public NV_Response SetModulus(UInt64 modulusKey)
        {
            byte[] buff = new byte[8];
            buff = BitConverter.GetBytes(modulusKey);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(buff);

            var data = new List<byte>
            {
                CMD_SET_MODULUS
            };
            data.AddRange(buff);

            return SendCommand(CMD_SET_MODULUS, data.ToArray());
        }

        public NV_Response RequestKeyExchange(UInt64 hostInterKey)
        {
            byte[] buff = new byte[8];
            buff = BitConverter.GetBytes(hostInterKey);

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(buff);

            var data = new List<byte>
            {
                CMD_REQUEST_KEY_EXCHANGE
            };
            data.AddRange(buff);

            NV_Response response = SendCommand(CMD_REQUEST_KEY_EXCHANGE, data.ToArray());
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            _keys.SlaveInterKey = BitConverter.ToUInt64(response.Data[1..]);
            _keys.HostKey = NV_Util.XpowYmodN(_keys.SlaveInterKey, _keys.HostRandom, _keys.ModulusKey);

            return response;
        }

        public NV_Response DisableValidator()
        {
            byte[] data = new byte[1];
            data[0] = CMD_DISABLE;

            var response = SendCommand(CMD_DISABLE, data);

            _isListening = false;
            _listener.Enabled = false;
            _listener.Stop();

            return response;
        }

        public NV_Response EnableValidator()
        {
            byte[] data = new byte[1];
            data[0] = CMD_ENABLE;

            var response = SendCommand(CMD_ENABLE, data);

            _isListening = true;
            _listener.Enabled = true;
            _listener.Start();

            return response;
        }

        public NV_Response GetSerialNumber()
        {
            byte[] data = new byte[1];
            data[0] = CMD_GET_SERIAL_NUMBER;

            var response = SendCommand(CMD_GET_SERIAL_NUMBER, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            byte[] buff = new byte[4];
            buff = response.Data[1..5];

            if (BitConverter.IsLittleEndian)
                Array.Reverse(buff);

            UInt32 serialNumber = BitConverter.ToUInt32(buff);
            response.MetaData = new Dictionary<string, object>()
            {
                { "SERIAL_NUMBER", serialNumber }
            };

            return response;
        }

        public NV_Response UnitData()
        {
            byte[] data = new byte[1];
            data[0] = CMD_UNIT_DATA;

            var response = SendCommand(CMD_UNIT_DATA, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            var result = response.Data[1..];

            //todo: try-catch konulacak.

            string unitType = (result[0]) switch
            {
                0x00 => "Validator",
                0x03 => "SMART Hopper",
                0x06 => "SMART Payout",
                0x07 => "NV11",
                0x0D => "TEBS",
                _ => "Unknown Type",
            };

            string firmwareVersion = Encoding.ASCII.GetString(result[1..5]);
            string countryCode = Encoding.ASCII.GetString(result[5..8]);

            byte[] valueMultiplierArray = result[8..11];
            if (BitConverter.IsLittleEndian)
                Array.Reverse(valueMultiplierArray);

            int valueMultiplier = valueMultiplierArray[0] * 100 +
                                  valueMultiplierArray[1] * 10 +
                                  valueMultiplierArray[2] * 1;

            byte protocolVersion = result[11];


            var unitData = new NV_UnitData()
            {
                UnitType = unitType,
                FirmwareVersion = firmwareVersion,
                CountryCode = countryCode,
                ValueMultiplier = valueMultiplier,
                ProtocolVersion = protocolVersion

            };

            response.MetaData.Add("UNIT_DATA_UNITTYPE", unitData.UnitType);
            response.MetaData.Add("UNIT_DATA_FIRMWAREVERSION", unitData.FirmwareVersion);
            response.MetaData.Add("UNIT_DATA_COUNTRYCODE", unitData.CountryCode);
            response.MetaData.Add("UNIT_DATA_VALUEMULTIPLIER", unitData.ValueMultiplier);
            response.MetaData.Add("UNIT_DATA_PROTOCOLVERSION", unitData.ProtocolVersion);

            return response;
        }

        //ChannelValueRequest

        public NV_Response Sync()
        {
            byte[] data = new byte[1];
            data[0] = CMD_SYNC;

            var response = SendCommand(CMD_SYNC, data);
            return response;
        }

        // Set Coin Mech Global Inhibit

        public NV_Response PayoutByDenomination(List<NV_ChannelData> demons, bool test)
        {
            List<byte> data = new List<byte>();
            byte requestCount = 0x00;

            data.Add(CMD_PAYOUT_BY_DENOMINATION);
            data.Add(requestCount);

            foreach (var item in demons)
            {
                if (item.Level > 0)
                {
                    requestCount++;

                    byte[] amountByte = BitConverter.GetBytes(item.Level);
                    byte[] noteByte = BitConverter.GetBytes(item.Value);
                    data.Add(amountByte[0]);
                    data.Add(amountByte[1]);

                    data.Add(noteByte[0]);
                    data.Add(noteByte[1]);
                    data.Add(noteByte[2]);
                    data.Add(noteByte[3]);

                    data.Add((byte)'T');
                    data.Add((byte)'R');
                    data.Add((byte)'Y');
                }
            }

            data[1] = requestCount;

            if (test)
            {
                data.Add(0x19);
            }
            else
            {
                data.Add(0x58);
            }

            return SendCommand(CMD_PAYOUT_BY_DENOMINATION, data.ToArray());

        }

        // Set Value Reporting Type
        // Float By Denomination
        // Stack Note
        // Payout Note
        // Get Note Positions
        // Set Coin Mech Inhibits

        public NV_Response EmptyAll()
        {
            byte[] data = new byte[1];
            data[0] = CMD_EMPTY_ALL;

            var response = SendCommand(CMD_EMPTY_ALL, data);
            return response;
        }

        // Get Minimum Payout
        // Float Amount

        public bool GetDenominationRoute(int note, char[] currency)
        {
            byte[] noteByte = BitConverter.GetBytes(note);
            byte[] data = new byte[8];
            data[0] = CMD_GET_DENOMINATION_ROUTE;
            data[1] = noteByte[0];
            data[2] = noteByte[1];
            data[3] = noteByte[2];
            data[4] = noteByte[3];

            data[5] = (byte)currency[0];
            data[6] = (byte)currency[1];
            data[7] = (byte)currency[2];

            var result = SendCommand(CMD_GET_DENOMINATION_ROUTE, data);
            if (result.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            //Recycled and used for payouts
            //0x00

            //Detected denomination is routed to system cashbox
            //0x01

            if (result.Data[1] == 0x00)
            {
                return true;
            }

            if (result.Data[1] == 0x01)
            {
                return false;
            }

            return false;
        }

        public bool SetDenominationRoute(int note, char[] currency, bool stack)
        {
            byte[] noteByte = BitConverter.GetBytes(note);
            byte[] data = new byte[9];
            data[0] = CMD_SET_DENOMINATION_ROUTE;

            if (stack)
            {
                //cashbox
                data[1] = 0x01;
            }
            else
            {
                //stored
                data[1] = 0x00;
            }

            data[2] = noteByte[0];
            data[3] = noteByte[1];
            data[4] = noteByte[2];
            data[5] = noteByte[3];

            data[6] = (byte)currency[0];
            data[7] = (byte)currency[1];
            data[8] = (byte)currency[2];

            var result = SendCommand(CMD_SET_DENOMINATION_ROUTE, data);
            if (result.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            return true;
        }

        // Halt Payout
        // Communication Pass Through

        public int GetDenominationLevel(int note, char[] currency)
        {
            byte[] b = BitConverter.GetBytes(note);
            byte[] data = new byte[8];
            data[0] = CMD_GET_DENOMINATION_LEVEL;
            data[1] = b[0];
            data[2] = b[1];
            data[3] = b[2];
            data[4] = b[3];

            data[5] = (byte)currency[0];
            data[6] = (byte)currency[1];
            data[7] = (byte)currency[2];

            var result = SendCommand(CMD_GET_DENOMINATION_LEVEL, data);
            if (result.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return 0;
            }

            return result.Data[1];
        }

        // SetDenominationLevel
        // Payout Amount
        // Set Refill Mode
        // Get Bar Code Data
        // Set Bar Code Inhibit Status
        // Get Bar Code Inhibit Status
        // Set Bar Code Configuration
        // Get Bar Code Reader Configuration
        // Get All Levels

        public NV_Response GetDatasetVersion()
        {
            byte[] data = new byte[1];
            data[0] = CMD_GET_DATASET_VERSION;

            var response = SendCommand(CMD_GET_DATASET_VERSION, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            string datasetVersion = Encoding.ASCII.GetString(response.Data[1..^2]);
            response.MetaData = new Dictionary<string, object>()
            {
                { "DATASET_VERSION", datasetVersion }
            };

            return response;
        }

        // Get Firmware Version

        public NV_Response Hold()
        {
            byte[] data = new byte[1];
            data[0] = CMD_HOLD;

            var response = SendCommand(CMD_HOLD, data);
            return response;
        }

        // Last Reject Code

        public void GetHopperOptions()
        {

        }

        public NV_Response SmartEmpty()
        {
            byte[] data = new byte[1];
            data[0] = CMD_SMART_EMPTY;

            var response = SendCommand(CMD_SMART_EMPTY, data);
            return response;
        }

        public NV_Response CashboxPayoutOperationData()
        {
            byte[] data = new byte[1];
            data[0] = CMD_CASHBOX_PAYOUT_OPERATION_DATA;

            var response = SendCommand(CMD_CASHBOX_PAYOUT_OPERATION_DATA, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }


            var CashboxPayoutOperationDataList = new List<CashboxPayoutOperationData>();
            var result = response.Data;

            for (int i = 0; i < result[1]; i++)
            {
                CashboxPayoutOperationDataList.Add(new CashboxPayoutOperationData()
                {
                    Quantity = BitConverter.ToInt16(result[((i * 9) + 2)..((i * 9) + 4)]),
                    Value = BitConverter.ToInt32(result[((i * 9) + 4)..((i * 9) + 8)]),
                    CountryCode = Encoding.ASCII.GetString(result[((i * 9) + 8)..((i * 9) + 11)])
                });

            }

            response.MetaData.Add("CASHBOX_PAYOUT_OPERATION_DATA", CashboxPayoutOperationDataList);
            return response;
        }

        public NV_Response ConfigureBezel()
        {
            byte[] data = new byte[5];
            data[0] = CMD_CONFIGURE_BEZEL;
            data[1] = 0x00; //Red intensity (0-255)
            data[2] = 0xFF; //Green intensity (0-255)
            data[3] = 0x00; //Blue intensity (0-255)
            data[4] = 0x00; //Config 0 for volatile,1 - for non-volatile.

            var response = SendCommand(CMD_CONFIGURE_BEZEL, data);
            return response;
        }

        //EventACK

        public NV_Response GetCounters()
        {
            byte[] data = new byte[1];
            data[0] = CMD_GET_COUNTERS;

            var response = SendCommand(CMD_GET_COUNTERS, data);
            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return response;
            }

            var result = response.Data[1..];

            var numberOfCountersInSet = result[0];
            var stacked = BitConverter.ToUInt32(result[1..5]);
            var stored = BitConverter.ToUInt32(result[5..9]);
            var dispensed = BitConverter.ToUInt32(result[9..13]);
            var transferredFromStoreToStacker = BitConverter.ToUInt32(result[13..17]);
            var rejected = BitConverter.ToUInt32(result[17..21]);

            response.MetaData = new Dictionary<string, object>()
            {
                { "NUMBER_OF_COUNTERS_IN_SET", numberOfCountersInSet },
                { "STACKED", stacked },
                { "STORED", stored },
                { "DISPENSED", dispensed },
                { "TRANSFERRED_FROM_STORE_TO_STACKER", transferredFromStoreToStacker },
                { "REJECTED", rejected },
            };

            return response;
        }

        public NV_Response ResetCounters()
        {
            byte[] data = new byte[1];
            data[0] = CMD_RESET_COUNTERS;

            var response = SendCommand(CMD_RESET_COUNTERS, data);
            return response;
        }

        public NV_Response DisablePayoutDevice()
        {
            byte[] data = new byte[1];
            data[0] = CMD_DISABLE_PAYOUT_DEVICE;

            return SendCommand(CMD_DISABLE_PAYOUT_DEVICE, data);
        }

        public NV_Response EnablePayoutDevice()
        {
            byte[] data = new byte[1];
            data[0] = CMD_ENABLE_PAYOUT_DEVICE;

            return SendCommand(CMD_ENABLE_PAYOUT_DEVICE, data);
        }

        public NV_Response RejectBanknote()
        {
            byte[] data = new byte[1];
            data[0] = CMD_REJECT_BANKNOTE;

            return SendCommand(CMD_REJECT_BANKNOTE, data);
        }

        public void SetFixedEncryptionKey()
        {

        }

        //ResetFixedEncryptionKey

        public void UpdateByChannelData()
        {
            foreach (NV_ChannelData cd in ChannelDataList)
            {
                cd.Level = GetDenominationLevel(cd.Value, cd.Currency);
                cd.Recycling = GetDenominationRoute(cd.Value, cd.Currency);

            }
        }

        public List<NV_ChannelData> IsHasNoteAvailable(int amount)
        {
            var channels = ChannelDataList.OrderByDescending(x => x.Value).ToList();
            var usedNotes = new List<NV_ChannelData>
                                            {
                                                new NV_ChannelData { Value = 20000 , Level = 0  },
                                                new NV_ChannelData { Value = 10000 , Level = 0 },
                                                new NV_ChannelData { Value = 5000 , Level = 0 },
                                                new NV_ChannelData { Value = 2000, Level = 0  },
                                                new NV_ChannelData { Value = 1000, Level = 0 },
                                                new NV_ChannelData { Value = 500, Level = 0 },
                                            };

            var result = Change(amount * 100, channels, usedNotes);
            if (result == null)
            {
                return null;
            }

            return usedNotes;
        }

        #endregion

        #region Private Methods

        private void _listener_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            this._listener.Stop();

            try
            {
                lock (_lockPoll)
                {
                    if (Poll() == false)
                    {
                        var r = 3;
                        do
                        {

                            //todo: cihazın tekrar init olması yapılacak.
                            //todo: önemli
                            r--;

                        } while (r > 0);

                        Disconnect();

                        _isListening = false;
                        _listener.Enabled = false;
                        _listener.Stop();


                        Console.WriteLine("Stoped");
                    }

                }
            }
            catch (Exception err)
            {
                Log.Error(err, $"Kiosk Name:{KioskName} Device Type:{_deviceType} Transaction Number:{TransactionNumber} Error Message: {err.StackTrace}");

            }
            finally
            {
                if (!_listener.Enabled && _isListening)
                    _listener.Start();
            }
        }

        NV_ChannelData lastChannelData = new NV_ChannelData();

        private bool Poll()
        {
            byte[] data = new byte[1];
            data[0] = CMD_POLL;
            var response = SendCommand(CMD_POLL, data);

            if (response.ResponseStatus != NV_ResponseStatus.SSP_RESPONSE_OK)
            {
                return false;
            }

            NV_ChannelData channelData = new NV_ChannelData();
            //var rData = response.Data[1..];

            for (int i = 1; i < response.DataLen; i++)
            {
                switch (response.Data[i])
                {
                    case POLL_SLAVE_RESET:
                        UpdateByChannelData();

                        Log.Information("POLL_SLAVE_RESET");
                        break;
                    case POLL_DISABLED:
                        Log.Information("POLL_DISABLED");
                        break;
                    case POLL_READ_NOTE:

                        Log.Information("POLL_READ_NOTE");

                        if (response.Data[i + 1] > 0)
                        {
                            Log.Information("NOTE_IN_ESCROW");

                            channelData = GetByChannelData(response.Data[i + 1]);
                            OnNoteReadEscrowed(new NV_NoteReadEscrowedEvent(1, (channelData.Value), channelData.Currency));
                        }

                        i++;
                        break;
                    case POLL_CREDIT_NOTE:

                        channelData = GetByChannelData(response.Data[i + 1]);
                        OnNoteCreditAccepted(new NV_NoteCreditAcceptedEvent(1, (channelData.Value), channelData.Currency));

                        if (_unitType == "SMART_PAYOUT")
                        {
                            UpdateByChannelData();
                        }

                        lastChannelData = channelData;

                        i++;

                        Log.Information("POLL_CREDIT_NOTE_VALUE:" + channelData.Value / 100);
                        break;
                    case POLL_NOTE_REJECTING:
                        Log.Information("POLL_NOTE_REJECTING");
                        break;
                    case POLL_NOTE_REJECTED:
                        //QueryRejection(log);
                        Log.Information("POLL_NOTE_REJECTED");
                        break;
                    case POLL_NOTE_STACKING:
                        Log.Information("POLL_NOTE_STACKING");
                        break;
                    case POLL_FLOATING:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_FLOATING");
                        break;
                    case POLL_NOTE_STACKED:

                        OnNoteStacked(new NV_NoteStackedEvent(1, (lastChannelData.Value), lastChannelData.Currency));

                        //OnNoteStacked(new NV_NoteStackedEvent()
                        //{
                        //    ChannelData = lastChannelData
                        //});

                        lastChannelData = new NV_ChannelData();

                        Log.Information("POLL_NOTE_STACKED");
                        break;
                    case POLL_FLOATED:

                        //GetCashboxPayoutOpData(log);
                        UpdateByChannelData();
                        EnableValidator();

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_FLOATED");
                        break;
                    case POLL_NOTE_STORED_IN_PAYOUT:

                        UpdateByChannelData();
                        OnNoteStoredInPayout(new NV_NoteStoredInPayoutEvent() { });

                        Log.Information("POLL_NOTE_STORED_IN_PAYOUT");

                        break;
                    case POLL_SAFE_NOTE_JAM:
                        Log.Information("POLL_SAFE_NOTE_JAM");
                        break;
                    case POLL_UNSAFE_NOTE_JAM:
                        Log.Information("POLL_UNSAFE_NOTE_JAM");
                        break;
                    case POLL_ERROR_DURING_PAYOUT:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_ERROR_DURING_PAYOUT");
                        break;
                    case POLL_FRAUD_ATTEMPT:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_FRAUD_ATTEMPT");
                        break;
                    case POLL_STACKER_FULL:
                        Log.Information("POLL_STACKER_FULL");
                        break;
                    case POLL_NOTE_CLEARED_FROM_FRONT:
                        Log.Information("POLL_NOTE_CLEARED_FROM_FRONT");

                        i++;
                        break;
                    case POLL_NOTE_CLEARED_TO_CASHBOX:
                        Log.Information("POLL_NOTE_CLEARED_TO_CASHBOX");

                        i++;
                        break;
                    case POLL_NOTE_PAID_INTO_STORE_AT_POWER_UP:
                        Log.Information("POLL_NOTE_PAID_INTO_STORE_AT_POWER_UP");

                        i += 7;

                        break;
                    case POLL_NOTE_PAID_INTO_STACKER_AT_POWER_UP:
                        Log.Information("POLL_NOTE_PAID_INTO_STACKER_AT_POWER_UP");

                        i += 7;

                        break;
                    case POLL_CASHBOX_REMOVED:
                        Log.Information("POLL_CASHBOX_REMOVED");
                        break;
                    case POLL_CASHBOX_REPLACED:
                        Log.Information("POLL_CASHBOX_REPLACED");
                        break;
                    case POLL_DISPENSING:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_DISPENSING");
                        break;
                    case POLL_DISPENSED:

                        UpdateByChannelData();
                        EnableValidator();

                        OnDispensed(new NV_DispensedEvent() { });


                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_DISPENSED");
                        break;
                    case POLL_EMPTYING:
                        Console.WriteLine("POLL_EMPTYING");
                        break;
                    case POLL_EMPTIED:

                        UpdateByChannelData();
                        EnableValidator();

                        OnPayoutEmptied(new NV_PayoutEmptiedEvent());

                        Log.Information("POLL_EMPTIED");
                        break;
                    case POLL_SMART_EMPTYING:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_SMART_EMPTYING");
                        break;
                    case POLL_SMART_EMPTIED:

                        UpdateByChannelData();
                        EnableValidator();

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_SMART_EMPTIED");
                        break;
                    case POLL_JAMMED:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_JAMMED");
                        break;
                    case POLL_HALTED:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_HALTED");
                        break;
                    case POLL_INCOMPLETE_PAYOUT:

                        i += (byte)((response.Data[i + 1] * 11) + 1);

                        Log.Information("POLL_INCOMPLETE_PAYOUT");
                        break;
                    case POLL_INCOMPLETE_FLOAT:

                        i += (byte)((response.Data[i + 1] * 11) + 1);

                        Log.Information("POLL_INCOMPLETE_FLOAT");
                        break;
                    case POLL_NOTE_TRANSFERED_TO_STACKER:

                        i += 7;

                        Log.Information("POLL_NOTE_TRANSFERED_TO_STACKER");
                        break;
                    case POLL_NOTE_HELD_IN_BEZEL:

                        i += 7;

                        Log.Information("POLL_NOTE_HELD_IN_BEZEL");
                        break;
                    case POLL_PAYOUT_OUT_OF_SERVICE:
                        Log.Information("POLL_PAYOUT_OUT_OF_SERVICE");
                        break;
                    case POLL_TIME_OUT:

                        i += (byte)((response.Data[i + 1] * 7) + 1);

                        Log.Information("POLL_TIME_OUT");
                        break;
                    default:
                        break;
                }
            }

            return true;
        }

        private NV_Response SendCommand(byte command, byte[] data)
        {
            Monitor.Enter(_lockCommand);
            NV_Response response = new NV_Response();
            try
            {
                response.CommandName = NV_Commands.Command[command];

                byte len = (byte)data.Length;
                _sequence = _sequence != (byte)128 ? (byte)128 : (byte)0;

                if (EncryptionStatus)
                {
                    byte[] packet = null;
                    try
                    {
                        packet = PacketEncrypt(data);
                    }
                    catch (Exception err)
                    {
                        response.ResponseStatus = NV_ResponseStatus.PACKET_ERROR_ENCRYPT_FAIL;
                        response.ErrorMessage = err.Message;
                        response.LastException = err;
                        return response;
                    }

                    len = (byte)(packet.Length);
                    data = packet;
                }

                List<byte> buff = new List<byte>();
                buff.Add(_sequence);
                buff.Add(len);
                buff.AddRange(data);
                buff.AddRange(NV_Util.CRC16(buff.ToArray()));

                List<byte> tempArray = new List<byte>();
                for (int i = 0; i < buff.Count(); i++)
                {
                    if (buff[i] == STX)
                    {
                        tempArray.Add(STX);
                    }
                    tempArray.Add(buff[i]);
                }

                buff.Clear();
                buff.Add(STX);
                buff.AddRange(tempArray);

                response.RequestDataByte = buff.ToArray();
                response.RequestDataHex = BitConverter.ToString(buff.ToArray());

                byte[] receivedByte = null;
                int r = 3;
                do
                {
                    try
                    {
                        PortWrite(buff.ToArray());
                    }
                    catch (Exception err)
                    {
                        response.ResponseStatus = NV_ResponseStatus.PORT_ERROR;
                        response.ErrorMessage = err.Message;
                        response.LastException = err;
                        return response;
                    }

                    response.ResponseStatus = NV_ResponseStatus.SSP_REPLY_OK;

                    try
                    {
                        receivedByte = PortRead();
                    }
                    catch (TimeoutException err)
                    {
                        response.ResponseStatus = NV_ResponseStatus.PORT_TIMEOUT;
                        response.ErrorMessage = err.Message;
                        response.LastException = err;
                    }

                    if (response.ResponseStatus == NV_ResponseStatus.SSP_REPLY_OK)
                    {
                        break;
                    }

                    r -= 1;

                } while (r > 0);

                if (response.ResponseStatus != NV_ResponseStatus.SSP_REPLY_OK)
                {
                    return response;
                }

                if (EncryptionStatus)
                {
                    try
                    {
                        receivedByte = PacketDecrypt(receivedByte);
                    }
                    catch (Exception err)
                    {
                        response.ResponseStatus = NV_ResponseStatus.PACKET_ERROR_DECRYPT_FAIL;
                        response.ErrorMessage = err.Message;
                        response.LastException = err;
                        return response;
                    }
                }

                response.ResponseDataByte = receivedByte;
                response.ResponseDataHex = BitConverter.ToString(receivedByte);
                response.DataLen = receivedByte[2];
                response.Data = receivedByte[3..^2];

                if (response.Data[0] == RESPONSE_OK)
                {
                    response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_OK;

                    response.ErrorCode = new byte { };
                    response.ErrorMessage = string.Empty;

                    if (DebugMode)
                    {

                        var sb = new StringBuilder();
                        sb.AppendLine("");
                        sb.AppendLine($"COM-> {response.RequestDataHex}");
                        sb.AppendLine($"COM<- {response.ResponseDataHex}");
                        sb.AppendLine($"result: success, command: {NV_Commands.Command[command]}, count: {_count}");


                        Log.Information(sb.ToString());

                        //Log.Information($"result: success, command: {NV_Commands.Command[command]}, count: {_count}");

                        //if (_keys.EncryptKey != null)
                        //{
                        //    Log.Information($"decrypt key: {BitConverter.ToString(_keys.EncryptKey)}");

                        //}

                        //Log.Information($"COM-> {response.RequestDataHex}");
                        //Log.Information($"COM<- {response.ResponseDataHex}");
                        //Log.Information("------------------------------------------------------------");

                    }

                    return response;
                }
                else
                {
                    if (response.Data[0] == RESPONSE_COMMAND_CANNOT_BE_PROCESSED)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_COMMAND_CANNOT_BE_PROCESSED;

                        if (command == CMD_ENABLE_PAYOUT_DEVICE)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NO DEVICE CONNECTED";
                            }

                            if (response.Data[1] == 2)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "INVALID CURRENCY DETECTED";
                            }

                            if (response.Data[1] == 3)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE BUSY";
                            }

                            if (response.Data[1] == 4)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "EMPTY ONLY (NOTE FLOAT ONLY)";
                            }

                            if (response.Data[1] == 5)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE ERROR";
                            }
                        }

                        if (command == CMD_PAYOUT_BY_DENOMINATION || command == CMD_FLOAT_AMOUNT || command == CMD_PAYOUT_AMOUNT)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOT ENOUGH VALUE IN DEVICE";
                            }

                            if (response.Data[1] == 2)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "CANNOT PAY EXACT AMOUNT";
                            }

                            if (response.Data[1] == 3)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE BUSY";
                            }

                            if (response.Data[1] == 4)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE DISABLED";
                            }
                        }

                        if (command == CMD_SET_VALUE_REPORTING_TYPE || command == CMD_GET_DENOMINATION_ROUTE || command == CMD_SET_DENOMINATION_ROUTE)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NO PAYOUT CONNECTED";
                            }

                            if (response.Data[1] == 2)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "INVALID CURRENCY DETECTED";
                            }

                            if (response.Data[1] == 3)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "PAYOUT DEVICE ERROR";
                            }
                        }

                        if (command == CMD_FLOAT_BY_DENOMINATION)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOT ENOUGH VALUE IN DEVICE";
                            }

                            if (response.Data[1] == 2)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "CANNOT PAY EXACT AMOUNT";
                            }

                            if (response.Data[1] == 3)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE BUSY";
                            }

                            if (response.Data[1] == 4)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "DEVICE DISABLED";
                            }

                        }

                        if (command == CMD_STACK_NOTE || command == CMD_PAYOUT_NOTE)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOTE FLOAT UNIT NOT CONNECTED";
                            }

                            if (response.Data[1] == 2)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOTE FLOAT EMPTY";
                            }

                            if (response.Data[1] == 3)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOTE FLOAT BUSY";
                            }

                            if (response.Data[1] == 4)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "NOTE FLOAT DISABLED";
                            }
                        }

                        if (command == CMD_GET_NOTE_POSITIONS)
                        {
                            if (response.Data[1] == 1)
                            {
                                response.ErrorCode = response.Data[0];
                                response.ErrorMessage = "INVALID CURRENCY";
                            }
                        }
                    }

                    if (response.Data[0] == RESPONSE_FAIL)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_FAIL;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS FAIL";
                    }

                    if (response.Data[0] == RESPONSE_KEY_NOT_SET)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_KEY_NOT_SET;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS KEY NOT SET, RENEGOTIATE KEYS";
                    }

                    if (response.Data[0] == RESPONSE_PARAMETER_OUT_OF_RANGE)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_PARAMETER_OUT_OF_RANGE;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS PARAM OUT OF RANGE";
                    }

                    if (response.Data[0] == RESPONSE_SOFTWARE_ERROR)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_PARAMETER_OUT_OF_RANGE;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS SOFTWARE ERROR";
                    }

                    if (response.Data[0] == RESPONSE_COMMAND_NOT_KNOWN)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_COMMAND_NOT_KNOWN;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS NOT KNOWN";
                    }

                    if (response.Data[0] == RESPONSE_WRONG_NO_PARAMETERS)
                    {
                        response.ResponseStatus = NV_ResponseStatus.SSP_RESPONSE_WRONG_NO_PARAMETERS;

                        response.ErrorCode = response.Data[0];
                        response.ErrorMessage = "COMMAND RESPONSE IS WRONG NUMBER OF PARAMETERS";
                    }

                    //response.ResponseStatus = NV_ResponseStatus.UNKOWN;

                    if (DebugMode)
                    {
                        var sb = new StringBuilder();
                        sb.AppendLine("");
                        sb.AppendLine($"COM-> {response.RequestDataHex}");
                        sb.AppendLine($"COM<- {response.ResponseDataHex}");
                        sb.AppendLine($"result: fail, command: {NV_Commands.Command[command]}, count: {_count}");
                        sb.AppendLine($"error messsage: {response.ErrorMessage}, error code: {response.ErrorCode}");

                        Log.Information(sb.ToString());

                        //Log.Information($"COM-> {response.RequestDataHex}");
                        //Log.Information($"COM<- {response.ResponseDataHex}");
                        //Log.Information($"result: fail, command: {NV_Commands.Command[command]}, count: {_count}");
                        //Log.Information($"error messsage: {response.ErrorMessage}, error code: {response.ErrorCode}");

                    }

                    return response;
                }
            }
            catch (Exception err)
            {
                response.ErrorCode = 0x00;
                response.ResponseStatus = NV_ResponseStatus.SYSTEM_ERROR;
                response.ErrorMessage = err.Message;
                response.LastException = err;
                return response;
            }
            finally
            {
                Monitor.Exit(_lockCommand);
            }
        }

        private byte[] PacketEncrypt(byte[] packet)
        {
            try
            {
                byte[] eCount = new byte[4];
                eCount[0] = (byte)_count;
                eCount[1] = (byte)(_count >> 8);
                eCount[2] = (byte)(_count >> 16);
                eCount[3] = (byte)(_count >> 24);

                List<byte> buff = new List<byte>();
                byte len = (byte)packet.Length;
                buff.Add(len);
                buff.AddRange(eCount);
                buff.AddRange(packet);

                byte[] ePacketing = NV_Util.RandomArray((int)Math.Ceiling((decimal)(buff.Count + 2) / 16) * 16 - (buff.Count + 2));
                buff.AddRange(ePacketing);
                byte[] crc = NV_Util.CRC16(buff.ToArray());
                buff.AddRange(crc);

                byte[] eData = NV_Util.AESEncrypt(_keys, buff.ToArray());

                buff.Clear();
                buff.Add(STEX);
                buff.AddRange(eData);

                return buff.ToArray();
            }
            catch (Exception err)
            {

                throw err;
            }
        }

        private byte[] PacketDecrypt(byte[] packet)
        {
            try
            {
                byte[] data = packet[3..^2];
                byte[] eData = NV_Util.AESDecrypt(_keys, data[1..]);
                byte eLenght = eData[0];
                byte[] eCount = eData[1..5];
                byte[] eCRC = eData[^2..];
                _count = BitConverter.ToInt32(eCount);

                List<byte> buff = new List<byte>
                {
                    packet[0], //STX
                    packet[1], //SEQ
                    (byte)(eLenght)
                };
                buff.AddRange(eData[5..(eLenght + 5)]);
                buff.AddRange(eCRC);

                return buff.ToArray();
            }
            catch (Exception err)
            {
                throw err;
            }
        }

        private void PortWrite(byte[] data)
        {
            _serialPort.DiscardOutBuffer();
            _serialPort.DiscardInBuffer();
            _serialPort.Write(data, 0, data.Length);

        }

        private byte[] PortRead()
        {
            List<byte> buffer = new List<byte>();
            bool readCompleted = false;
            int checkDouble7F = 0;
            int bufferLen = 0;

            Stopwatch sw = new Stopwatch();
            sw.Start();

            while (true)
            {
                if (sw.ElapsedMilliseconds > 1500)
                {
                    throw new TimeoutException();
                };

                if (_serialPort.IsOpen)
                {
                    while (_serialPort.BytesToRead > 0)
                    {
                        byte data = (byte)_serialPort.ReadByte();

                        if (data == 127 && buffer.Count() == 0)
                        {
                            buffer.Add(data);
                            continue;
                        }

                        if (checkDouble7F == 1)
                        {

                            buffer.Add(data);

                            checkDouble7F = 0;
                        }
                        else if (data == 127)
                        {
                            checkDouble7F = 1;
                        }
                        else
                        {
                            buffer.Add(data);
                            if (buffer.Count() == 3)
                            {
                                bufferLen = (byte)(buffer[2] + 5);
                            }
                        }

                        if (buffer.Count() == bufferLen)
                        {
                            readCompleted = true;
                            break;
                        }
                    }

                    if (readCompleted)
                    {
                        break;
                    }
                }

            }

            return buffer.ToArray();
        }

        private NV_ChannelData GetByChannelData(int channel)
        {
            var cd = new NV_ChannelData();

            foreach (NV_ChannelData item in ChannelDataList)
            {
                if (item.Channel == channel)
                {
                    cd = item;
                    break;
                }
            }

            return cd;
        }

        private List<NV_ChannelData> Change(int amount, List<NV_ChannelData> availableNotes, List<NV_ChannelData> usedNotes)
        {
            if (amount == 0)
            {
                return availableNotes;
            }

            if (amount < 0)
            {
                return null;
            }

            foreach (var availableCoin in availableNotes.Where(ac => ac.Level > 0 && amount >= ac.Value))
            {
                var newAvailableCoins = CopyNotes(availableNotes);
                newAvailableCoins.First(c => c.Value == availableCoin.Value).Level--;
                var change = Change(amount - availableCoin.Value, newAvailableCoins, usedNotes);

                if (change == newAvailableCoins)
                {
                    usedNotes.First(c => c.Value == availableCoin.Value).Level++;
                    return availableNotes;
                }
            }

            return null;
        }

        private List<NV_ChannelData> CopyNotes(List<NV_ChannelData> input)
        {
            var copy = new List<NV_ChannelData>();
            foreach (var item in input)
            {
                copy.Add(new NV_ChannelData { Value = item.Value, Level = item.Level });
            }
            return copy;
        }

        #endregion

    }
}

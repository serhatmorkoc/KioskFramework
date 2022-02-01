using System;
using System.Collections.Generic;
using System.Text;

namespace Kiosk.Device.IT.NV
{
    public class NV_Response
    {
        public string CommandName { get; set; }

        public int DataLen { get; set; }
        public byte[] Data { get; set; }
        public string RequestDataHex { get; set; }
        public string ResponseDataHex { get; set; }
        public byte[] RequestDataByte { get; set; }
        public byte[] ResponseDataByte { get; set; }
        public byte ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
        public NV_ResponseStatus ResponseStatus { get; set; }
        public Exception LastException { get; set; }

        public bool EncryptionStatus { get; set; }
        public int EncryptionPacketCount { get; set; }
        public int Sequence { get; set; }

        public Dictionary<string, object> MetaData { get; set; } = new Dictionary<string, object>();
    }

    public enum NV_ResponseStatus
    {
        UNKOWN,
        SSP_RESPONSE_OK,
        SSP_RESPONSE_COMMAND_NOT_KNOWN,
        SSP_RESPONSE_WRONG_NO_PARAMETERS,
        SSP_RESPONSE_PARAMETER_OUT_OF_RANGE,
        SSP_RESPONSE_COMMAND_CANNOT_BE_PROCESSED,
        SSP_RESPONSE_SOFTWARE_ERROR,
        SSP_RESPONSE_FAIL,
        SSP_RESPONSE_KEY_NOT_SET,
        SSP_REPLY_OK,
        PORT_CLOSED,
        PORT_ERROR,
        PORT_TIMEOUT,
        PACKET_ERROR_ENCRYPT_FAIL,
        PACKET_ERROR_DECRYPT_FAIL,
        SYSTEM_ERROR
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight
{
    // ReSharper disable InconsistentNaming
    internal class CONST
    {
        public const string CMD_STREAM = "STRM";
        public const string CMD_USER = "USER";
        public const string CMD_AUTH = "AUTH";
        public const string CMD_OPEN = "OPEN";
        public const string CMD_CLOSE = "CLSE";
        public const string CMD_EVENT = "EVT";
        public const string CMD_EXECUTE = "EXE";

        public const string HDR_CMD = "CMD";
        public const string HDR_TOKEN = "TK";
        public const string HDR_ID = "ID";
        public const string HDR_METHOD = "MTH";
        public const string HDR_ARGS = "ARG";
        public const string HDR_VALUE = "VAL";
        public const string HDR_STATUS = "STS";
        public const string HDR_LOCATION = "LOC";
        public const string HDR_TIME = "TIME";

        public const string STA_NORMAL = "OK";
        public const string STA_ERROR = "EX";
        public const string STA_VOID = "VD";
        public const string STA_STREAM = "SR";
        public const string STA_SERVICE = "OP";
    }
    // ReSharper enable InconsistentNaming
}

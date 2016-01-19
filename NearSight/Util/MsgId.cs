using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Util
{
    public static class MsgId
    {
        private static short _id;
        private static readonly object _lock = new object();
        public static short Get()
        {
            lock (_lock)
            {
                //MPack can serialize anything less than 127 as a single byte with no identifier.
                _id = (_id > 125) ? (short)1 : (short)(_id + 1);
                return _id;
            }
        }
    }
}

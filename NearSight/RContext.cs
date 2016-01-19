using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace NearSight
{
    public class RContext
    {
        public EndPoint RemoteAddress { get; }

        public RContext(EndPoint remote)
        {
            RemoteAddress = remote;
        }
    }
}

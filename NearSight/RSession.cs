using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NearSight;

namespace NearSight
{
    internal class RSession : IDisposable
    {
        public DateTime Created { get; }
        public string Token { get; }
        public REndpoint Endpoint { get; }

        public RSession(REndpoint endpoint, object instance)
            :this(endpoint, instance, Guid.NewGuid().ToString())
        {
        }
        public RSession(REndpoint endpoint, object instance, string token)
        {
            Created = DateTime.Now;
            Token = token;
            Endpoint = endpoint;
            Instance = instance;
        }
        public object Instance { get; }

        public void Dispose()
        {
            if (Instance is IDisposable)
                ((IDisposable)Instance).Dispose();
        }
    }
}

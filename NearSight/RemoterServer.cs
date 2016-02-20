using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Net;
using System.Net.Configuration;
using System.Net.Security;
using System.Net.Sockets;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.Caching;
using System.Runtime.ExceptionServices;
using System.Runtime.Serialization.Formatters.Binary;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Anotar.NLog;
using CS;
using CS.Reactive;
using CS.Reactive.Network;
using NearSight.Network;
using NearSight.Protocol;
using NearSight.Util;
using RT.Util;
using RT.Util.ExtensionMethods;
using RT.Util.Json;
using RT.Util.Serialization;

namespace NearSight
{
    public class RemoterServer
    {
        public IPAddress LocalEndpoint { get; }
        public int Port { get; }
        public bool Listening { get; private set; }
        public bool PropagateExceptions { get; set; }
        public IReadOnlyCollection<REndpoint> Endpoints => _endpoints.AsReadOnly();
        public TimeSpan SessionTimeout { get; set; } = TimeSpan.FromMinutes(5);
        public bool SSL
        {
            get { return _ssl; }
            set
            {
                if (value && Certificate == null)
                    throw new InvalidOperationException("Certificate property must be non-null.");
                _ssl = value;
            }
        }
        public X509Certificate2 Certificate { get; set; }

        internal readonly GenericMemoryCache<RSession> Sessions = new GenericMemoryCache<RSession>();

        private bool _ssl;
        private NetServer _server;
        private readonly Dictionary<MessageTransferClient<byte[]>, IServerContext> _connections;
        private readonly List<REndpoint> _endpoints;
        //private Task _acceptLoop;
        //private CancellationTokenSource _cancelSource;
        private readonly object _lock = new object();

        public RemoterServer(int port)
            : this(IPAddress.Any, port)
        {
        }
        public RemoterServer(IPAddress localEp, int port)
        {
            LocalEndpoint = localEp;
            Port = port;
            _connections = new Dictionary<MessageTransferClient<byte[]>, IServerContext>();
            _endpoints = new List<REndpoint>();
        }

        public void Start()
        {
            lock (_lock)
            {
                try
                {
                    // Define the socket, bind to the port, and start accepting connections
                    _server = new NetServer(LocalEndpoint, Port);
                    _server.Start();
                    Listening = true;
                    _server.ClientConnected.Subscribe(Server_AcceptClient);
                    LogTo.Info("[Remoter] Listening on port " + Port);
                }
                catch (Exception ex)
                {
                    LogTo.ErrorException("[Remoter] Error opening socket on port " + Port, ex);
                    Stop();

                    if (PropagateExceptions)
                        throw;
                }
            }
        }
        public void Stop()
        {
            lock (_lock)
            {
                using (new AsyncContextChange())
                {
                    Listening = false;

                    // Close all child sockets
                    foreach (var socket in _connections.ToArray())
                    {
                        socket.Value.Dispose();
                        socket.Key.Dispose();
                    }
                    _connections.Clear();
                    Sessions.Clear();

                    // Close the listening socket
                    _server.Stop();
                    _server = null;

                    LogTo.Info("[Remoter] Shutdown complete.");
                }
            }
        }

        public void AddService<TInterface, TImpl>(string path)
        {
            Type theType = typeof(TImpl);
            var constructor = theType.GetConstructor(Type.EmptyTypes);
            if (constructor == null)
                throw new MissingMethodException("No parameterless constructor defined for this service implementation.");

            AddService<TInterface, TImpl>(path, context => (TImpl)Activator.CreateInstance(theType));
        }
        public void AddService<TInterface, TImpl>(string path, Func<RContext, TImpl> factory)
        {
            lock (_lock)
            {
                var obj = _endpoints.SingleOrDefault(end => end.Path.EqualsNoCase(path));
                if (obj != null)
                    throw new ArgumentException("Specified endpoint path already exists.", nameof(path));

                _endpoints.Add(new REndpoint(path, typeof(TInterface), context => factory(context)));
            }
        }
        public void RemoveService(string path)
        {
            lock (_lock)
            {
                var obj = _endpoints.SingleOrDefault(end => end.Path.EqualsNoCase(path));
                if (obj != null)
                {
                    RemoveService(obj);
                }
            }
        }
        public void RemoveService(REndpoint e)
        {
            lock (_lock)
            {
                _endpoints.Remove(e);
            }
        }

        private async void Server_AcceptClient(ITracked<MessageTransferClient<byte[]>> callback)
        {
            var client = callback.Value;
            try
            {
                ObservablePacketProtocol protocol = new ObservablePacketProtocol(client);
                var init = protocol.Buffer.Take(1).ToTask();
                callback.Observe();
                var hs = (await init).Observe().To<double>();

                var supported = new Dictionary<double, Func<IServerContext>>()
                {
                    {1.0, () => new v1_0ServerContext(this, protocol)}
                };
                if (supported.ContainsKey(hs))
                {
                    var impl = supported[hs]();
                    await client.WriteAsync(MPack.From("OK").EncodeToBytes()).ConfigureAwait(false);
                    _connections.Add(client, impl);
                }
                else
                {
                    await client.WriteAsync(MPack.From("Unsupported version").EncodeToBytes()).ConfigureAwait(false);
                    throw new Exception("Unsupported client version");
                }

                LogTo.Debug("[Remoter] Client connected: " + client.RemoteEndpoint + Environment.NewLine);

                protocol.Buffer.Subscribe(
                    (next) => { },
                    (error) => Client_Dispose(client),
                    () => Client_Dispose(client));

                //protocol.Buffer.Unobserved.Subscribe((p) =>
                //{
                //    MPackMap err = new MPackMap();
                //    err[CONST.HDR_STATUS] = MPack.From(CONST.STA_ERROR);
                //    err[CONST.HDR_VALUE] = MPack.From("The message recieved was unrecognized or unexpected.");
                //    if (p is MPackMap && ((MPackMap)p).ContainsKey(CONST.HDR_ID))
                //        err[CONST.HDR_ID] = p[CONST.HDR_ID];
                //    protocol.Write(err);
                //});
            }
            catch (Exception ex)
            {
                Client_Dispose(client);
                LogTo.ErrorException("[Remoter] Error accepting client connection: " + ex.Message, ex);
                if (PropagateExceptions)
                    throw;
            }
        }

        private void Client_Dispose(MessageTransferClient<byte[]> childSocket)
        {
            if (childSocket == null)
                return;
            try
            {
                childSocket.Dispose();
                if (_connections.ContainsKey(childSocket))
                {
                    var impl = _connections[childSocket];
                    impl.Dispose();
                    _connections.Remove(childSocket);
                }
            }
            catch
            {
            }
        }
    }
}

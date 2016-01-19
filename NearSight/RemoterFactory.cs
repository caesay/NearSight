using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CS.Network;
using MsgPack;
using NearSight.Network;
using NearSight.Protocol;
using NearSight.Util;
using RT.Util;
using RT.Util.Json;
using RT.Util.Serialization;
using RT.Util.ExtensionMethods;

namespace NearSight
{
    public class RemoterFactory : CommunicationObject
    {
        public Uri Endpoint { get; }
        public bool SSL { get; }
        public RemoterOptions Options { get; set; } = new RemoterOptions();

        internal NetClient Client { get; private set; }
        internal ObservablePacketProtocol Protocol { get; private set; }
        internal CancellationToken CancelToken => _cancelSource.Token;

        private CancellationTokenSource _cancelSource;

        public RemoterFactory(Uri endpoint, bool ssl = false)
        {
            Endpoint = endpoint;
            SSL = ssl;
        }
        public RemoterFactory(string endpoint, bool ssl = false)
            : this(new Uri(endpoint), ssl)
        {
        }

        public T OpenServicePath<T>(string path)
        {
            var p = v1_0ClientProxy.FromPath(this, typeof(T), path);
            p.Open();
            return (T)p.GetTransparentProxy();
        }
        public IExtendedProxy<T> OpenServicePathExtended<T>(string path)
        {
            var p = v1_0ClientProxy.FromPath(this, typeof(T), path);
            p.Open();
            return (IExtendedProxy<T>)p.GetTransparentProxy();
        }
        public async Task<T> OpenServicePathAsync<T>(string path)
        {
            var p = v1_0ClientProxy.FromPath(this, typeof(T), path);
            await p.OpenAsync();
            var obj = p.GetTransparentProxy();
            return (T)obj;
        }
        public async Task<IExtendedProxy<T>> OpenServicePathExtendedAsync<T>(string path)
        {
            var p = v1_0ClientProxy.FromPath(this, typeof(T), path);
            await p.OpenAsync();
            var obj = p.GetTransparentProxy();
            return (IExtendedProxy<T>)obj;
        }

        public override void Open()
        {
            if (Client != null)
            {
                if (Client.Connected)
                {
                    throw new ThreadStateException("Factory is opening or already opened.");
                }
                Client.Dispose();
                Client = null;
            }
            _cancelSource = new CancellationTokenSource();
            Client = new NetClient(Endpoint.Host, Endpoint.Port);
            Protocol = new ObservablePacketProtocol(Client);
            Client.Connect();

            using (new AsyncContextChange())
            {
                var tsk = Protocol.Buffer.Take(1).ToTask();
                Protocol.Write(MPack.FromDouble(1.0));
                if (!tsk.Wait(Options.OperationTimeout))
                    throw new TimeoutException(
                       "Did not recieve reply from the server in the specified operation timeout");
                var respTxt = tsk.Result.Observe().To<string>();
                if (!respTxt.EqualsNoCase("OK"))
                    throw new InvalidOperationException(respTxt);
            }
            //if (Credentials != null)
            //{
            //    using (new AsyncContextChange())
            //    {
            //        var task = AuthAsync(Credentials);
            //        if (!task.Wait(Options.OperationTimeout))
            //            throw new TimeoutException(
            //                "Did not recieve auth reply from the server in the specified operation timeout");
            //        if (task.Exception != null)
            //        {
            //            if (task.Exception.InnerException != null)
            //                throw task.Exception.InnerException;
            //            throw task.Exception;
            //        }
            //    }
            //}
        }
        public override void Close()
        {
            _cancelSource?.Cancel();
            var t = Client;
            Client = null;
            t.Close();
        }
        public override void Abort()
        {
            Close();
        }

        public override async Task OpenAsync(CancellationToken token)
        {
            if (Client != null)
            {
                if (Client.Connected)
                {
                    throw new ThreadStateException("Factory is opening or already opened.");
                }
                Client.Dispose();
                Client = null;
            }
            Client = new NetClient(Endpoint.Host, Endpoint.Port);
            Protocol = new ObservablePacketProtocol(Client);
            await Client.ConnectAsync();
            //write client version number.
            await Protocol.WriteAsync(MPack.FromDouble(1.0), token);
            var respTxt = (await Protocol.Buffer.Take(1).ToTask(token)).Observe().To<string>();
            if (!respTxt.EqualsNoCase("OK"))
                throw new InvalidOperationException(respTxt);
            //if (Credentials != null)
            //    await AuthAsync(Credentials);
        }
        public override Task CloseAsync(CancellationToken token)
        {
            var t = Client;
            Client = null;
            return Task.Factory.StartNew(() =>
            {
                t.Close();
            }, token);
        }

        /*
        private async Task AuthAsync(RCredentials cred)
        {
            var id = MsgId.Get();
            MPackMap req = new MPackMap();
            req[CONST.HDR_CMD] = MPack.From(CONST.CMD_USER);
            req[CONST.HDR_ID] = MPack.From(id);
            req[CONST.HDR_ARGS] = new MPackArray
                {
                    MPack.From(cred.Username)
                };
            var task = PacketChannel.Buffer.Where(m =>
            {
                var map = m.Value as MPackMap;
                return map != null && map.ContainsKey(CONST.HDR_ID) && map[CONST.HDR_ID].To<int>() == id;
            }).Timeout(Options.RecieveTimeout).Take(1).Observe()
            .Select(m => (MPackMap)m).ToTask(PacketChannel.CancelToken);
            await PacketChannel.SendPacketAsync(req);
            var resp = await task;
            if (resp[CONST.HDR_STATUS].To<string>() == CONST.STA_ERROR)
                throw new Exception(resp[CONST.HDR_VALUE].To<string>());

            var steps = ((MPackArray)resp[CONST.HDR_ARGS]).Select(m =>
            {
                string method = m.To<string>();
                var type = HashHelper.Parse(ref method);
                return Tuple.Create(type, method);
            }).ToArray();

            string password = cred.Password;
            foreach (var step in steps)
            {
                password = HashHelper.Compute(step.Item1, false, password, step.Item2);
            }

            id = MsgId.Get();
            req = new MPackMap();
            req[CONST.HDR_CMD] = MPack.From(CONST.CMD_AUTH);
            req[CONST.HDR_ID] = MPack.From(id);
            req[CONST.HDR_ARGS] = new MPackArray
                {
                    MPack.From(password)
                };
            task = PacketChannel.Buffer.Where(m =>
            {
                var map = m.Value as MPackMap;
                return map != null && map.ContainsKey(CONST.HDR_ID) && map[CONST.HDR_ID].To<int>() == id;
            }).Timeout(Options.RecieveTimeout).Take(1).Observe()
            .Select(m => (MPackMap)m).ToTask(PacketChannel.CancelToken);
            await PacketChannel.SendPacketAsync(req);
            resp = await task;
            if (resp[CONST.HDR_STATUS].To<string>() == CONST.STA_ERROR)
                throw new Exception(resp[CONST.HDR_VALUE].To<string>());
        }
        */
    }



    public class RemoterOptions
    {
        public TimeSpan RecieveTimeout { get; set; } = TimeSpan.FromSeconds(30);
        public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(10);
        public bool RetryUponNetworkError { get; set; } = true;
    }
}

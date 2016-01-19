
using System;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anotar.NLog;
using CS.Network;
using MsgPack;
using NearSight.Util;
using RT.Util.ExtensionMethods;

namespace NearSight.Network
{
    public class ObservablePacketProtocol
    {
        public ITrackableObservable<MPack> Buffer { get; }
        public NetClient Client { get; }

        //private Subject<MPack> _buffer;

        public ObservablePacketProtocol(NetClient client)
        {
            this.Client = client;
            Buffer = client.Observable
                .Select(args => MPack.ParseFromBytes(args.Bytes))
                .Do(LogRead)
                .ToTrackableObservable();

            //_buffer = new Subject<MPack>();
            //Buffer = _buffer.ToTrackableObservable();
            //client.PacketRecieved += Recieve;
            //client.Disconnected += Disconnected;
            //client.Error += Error;
        }

        //private void Error(object sender, ClientEventArgs<Exception> e)
        //{
        //    _buffer.OnError(e.Value);
        //}
        //private void Disconnected(object sender, EventArgs e)
        //{
        //    _buffer.OnCompleted();
        //}
        //private void Recieve(object sender, ClientEventArgs<byte[]> e)
        //{
        //    try
        //    {
        //        var mpack = MPack.ParseFromBytes(e.Value);
        //        LogRead(mpack);
        //        _buffer.OnNext(mpack);
        //    }
        //    catch (Exception ex)
        //    {
        //        _buffer.OnError(ex);
        //    }
        //}

        public void Write(MPack packet)
        {
            LogWrite(packet);
            var bytes = packet.EncodeToBytes();
            Client.Write(bytes);
        }
        public Task WriteAsync(MPack packet)
        {
            return WriteAsync(packet, CancellationToken.None);
        }
        public Task WriteAsync(MPack packet, CancellationToken token)
        {
            LogWrite(packet);
            var bytes = packet.EncodeToBytes();
            return Client.WriteAsync(bytes, token);
        }

        private void LogRead(MPack pack)
        {
            LogTo.Trace(() => "[Remoter] Recieving: " + pack.ToString().SubstringSafe(0, 1000));
        }
        private void LogWrite(MPack pack)
        {
            LogTo.Trace(() => "[Remoter] Sending: " + pack.ToString().SubstringSafe(0, 1000));
        }
    }
}

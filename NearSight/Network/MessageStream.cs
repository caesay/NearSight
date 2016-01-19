using System.IO;
using System;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LZ4;
using MsgPack;
using NearSight.Util;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace NearSight.Network
{
    internal class MessageConsumerStream : Stream
    {
        public override bool CanRead => GetProperty<bool>("CanRead");
        public override bool CanSeek => GetProperty<bool>("CanSeek");
        public override bool CanWrite => GetProperty<bool>("CanWrite");
        public override long Length => GetProperty<long>("Length");
        public override long Position
        {
            get { return GetProperty<long>("Position"); }
            set { SetProperty("Position", value); }
        }
        public override int ReadTimeout { get; set; } = 20000;

        private readonly ITrackableObservable<MPack> _source;
        private readonly string _token;
        private readonly Func<MPack, CancellationToken, Task> _publishAsync;
        private readonly Action<MPack> _publishSync;
        private readonly bool _asyncPublish;
        private readonly CancellationToken _cancel;

        internal MessageConsumerStream(string token, ITrackableObservable<MPack> source, CancellationToken cancel, Func<MPack, CancellationToken, Task> publisher)
        {
            _source = source;
            _token = token;
            _publishAsync = publisher;
            _asyncPublish = true;
            _cancel = cancel;
        }
        internal MessageConsumerStream(string token, ITrackableObservable<MPack> source, CancellationToken cancel, Action<MPack> publisher)
        {
            _source = source;
            _token = token;
            _publishSync = publisher;
            _asyncPublish = false;
            _cancel = cancel;
        }

        public override void Flush()
        {
            var p2 = GenFlushPacket();
            GetResponse(p2);
        }
        public override long Seek(long offset, SeekOrigin origin)
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(Seek))},
                {CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.FromInteger(offset),
                        MPack.FromInteger((int)origin),
                    }
                },
            };
            return GetResponse<long>(p2);
        }
        public override void SetLength(long value)
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(SetLength))},
                {CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.FromInteger(value),
                    }
                },
            };
            GetResponse(p2);
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            var p2 = GenReadPacket(count);
            var resp = SendRequest(p2);
            var bytesRead = ParseReturn<int>(resp);
            if (bytesRead > 0)
            {
                var binary = resp["BUFFER"].To<byte[]>();
                Buffer.BlockCopy(LZ4Wrap.DecodeWrapped(binary), 0, buffer, offset, bytesRead);
            }
            return bytesRead;
        }
        public override void Write(byte[] buffer, int offset, int count)
        {
            var p2 = GenWritePacket(buffer, offset, count);
            GetResponse(p2);
        }
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(Dispose))},
                {
                    CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.FromBool(disposing),
                    }
                },
            };
            // we don't want to block on a dispose call.
#pragma warning disable 4014
            SendRequestAsync(p2, _cancel);
#pragma warning restore 4014
        }

        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            var p2 = GenFlushPacket();
            return GetResponseAsync(p2, cancellationToken);
        }
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var p2 = GenReadPacket(count);

            var resp = await SendRequestAsync(p2, cancellationToken);
            var bytesRead = ParseReturn<int>(resp);
            if (bytesRead > 0)
            {
                var binary = resp["BUFFER"].To<byte[]>();
                Buffer.BlockCopy(LZ4Wrap.DecodeWrapped(binary), 0, buffer, offset, bytesRead);
            }
            return bytesRead;
        }
        public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var p2 = GenWritePacket(buffer, offset, count);
            return GetResponseAsync(p2, cancellationToken);
        }

        private MPackMap GenFlushPacket()
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(Flush))},
            };
            return p2;
        }
        private MPackMap GenReadPacket(int count)
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(Read))},
                {CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.FromInteger(count),
                    }
                },
            };
            return p2;
        }
        private MPackMap GenWritePacket(byte[] buffer, int offset, int count)
        {
            byte[] tmp = new byte[count];
            Buffer.BlockCopy(buffer, offset, tmp, 0, count);

            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString(nameof(Write))},
                {CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.FromBytes(LZ4Wrap.EncodeWrapped(tmp, true)),
                    }
                },
            };
            return p2;
        }

        private T GetProperty<T>(string name)
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString("GET_PROPERTY")},
                {
                    CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.From(name),
                    }
                },
            };

            return GetResponse<T>(p2);
        }
        private void SetProperty<T>(string name, T value)
        {
            MPackMap p2 = new MPackMap
            {
                {CONST.HDR_METHOD, MPack.FromString("SET_PROPERTY")},
                {
                    CONST.HDR_ARGS, new MPackArray
                    {
                        MPack.From(name),
                        MPack.From(value),
                    }
                },
            };
            GetResponse(p2);
        }

        private async Task<T> GetResponseAsync<T>(MPackMap request, CancellationToken token)
        {
            var resp = await SendRequestAsync(request, token);
            return ParseReturn<T>(resp);
        }
        private async Task GetResponseAsync(MPackMap request, CancellationToken token)
        {
            var resp = SendRequestAsync(request, token);
            ParseReturn(await resp);
        }
        private T GetResponse<T>(MPackMap request)
        {
            var resp = SendRequest(request);
            return ParseReturn<T>(resp);
        }
        private void GetResponse(MPackMap request)
        {
            var resp = SendRequest(request);
            ParseReturn(resp);
        }

        private Task<MPack> GetRequestTask(int id, CancellationToken token)
        {
            return _source.Where(pack =>
            {
                var map = pack.Value as MPackMap;
                return map != null
                       && map.ContainsKey(CONST.HDR_CMD)
                       && map.ContainsKey(CONST.HDR_ID)
                       && map[CONST.HDR_CMD].To<string>().EqualsNoCase(CONST.CMD_STREAM)
                       && map[CONST.HDR_ID].To<int>() == id;
            })
                .Timeout(TimeSpan.FromMilliseconds(ReadTimeout))
                .Take(1)
                .Observe()
                .ToTask(token);
        }
        private MPackMap SendRequest(MPackMap request)
        {
            var m = request;
            int id = MsgId.Get();
            m[CONST.HDR_ID] = MPack.FromInteger(id);
            m[CONST.HDR_CMD] = MPack.FromString(CONST.CMD_STREAM);
            m[CONST.HDR_TOKEN] = MPack.FromString(_token);

            using (new AsyncContextChange(null))
            {
                var respTsk = GetRequestTask(id, _cancel);

                if (_asyncPublish)
                    _publishAsync(request, _cancel).Wait(_cancel);
                else
                    _publishSync(request);
                respTsk.Wait(_cancel);
                return (MPackMap)respTsk.Result;
            }
        }
        private async Task<MPackMap> SendRequestAsync(MPackMap request, CancellationToken token)
        {
            var m = request;
            int id = MsgId.Get();
            m[CONST.HDR_ID] = MPack.FromInteger(id);
            m[CONST.HDR_CMD] = MPack.FromString(CONST.CMD_STREAM);
            m[CONST.HDR_TOKEN] = MPack.FromString(_token);

            using (CancellationTokenSource linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, _cancel))
            {
                var resp = GetRequestTask(id, linkedCts.Token);

                if (_asyncPublish)
                    await _publishAsync(request, linkedCts.Token);
                else
                    await Task.Run(() => _publishSync(request), linkedCts.Token);
                var r = await resp;
                linkedCts.Dispose();
                return (MPackMap)r;
            }
        }

        private T ParseReturn<T>(MPackMap resp)
        {
            if (resp[CONST.HDR_STATUS].To<string>().EqualsNoCase(CONST.STA_ERROR))
                throw new Exception(resp[CONST.HDR_VALUE].To<string>());
            if (resp[CONST.HDR_STATUS].To<string>().EqualsNoCase(CONST.STA_NORMAL))
                return resp[CONST.HDR_VALUE].To<T>();

            throw new NotSupportedException("Call returned a status that is not valid.");
        }
        private void ParseReturn(MPackMap resp)
        {
            if (resp[CONST.HDR_STATUS].To<string>().EqualsNoCase(CONST.STA_ERROR))
                throw new Exception(resp[CONST.HDR_VALUE].To<string>());
            if (resp[CONST.HDR_STATUS].To<string>().EqualsNoCase(CONST.STA_VOID))
                return;
            if (resp[CONST.HDR_STATUS].To<string>().EqualsNoCase(CONST.STA_NORMAL))
                return;

            throw new NotSupportedException("Call returned a status that is not valid.");
        }
    }

    internal class MessageProviderStream : IDisposable
    {
        private readonly ITrackableObservable<MPack> _source;
        private readonly string _token;
        private readonly Stream _stream;
        private readonly Action<MPack> _publish;
        private readonly bool _writable;
        private CancellationToken _cancel;
        private IDisposable _observable;
        private CancellationTokenRegistration _tokenRegistration;

        internal MessageProviderStream(string token, Stream stream, ITrackableObservable<MPack> source,
            Action<MPack> publisher, CancellationToken cancel, bool writable)
        {
            _source = source;
            _publish = publisher;
            _cancel = cancel;
            _writable = writable;
            _token = token;
            _stream = stream;
            _tokenRegistration = _cancel.Register(this.Dispose);
            _observable = _source.DoWhile(() => !_cancel.IsCancellationRequested && (stream.CanRead || stream.CanWrite))
                .Where(p => p.Value is MPackMap
                            && ((MPackMap)p.Value).ContainsKeys(new[] { CONST.HDR_CMD, CONST.HDR_TOKEN })
                            && p.Value[CONST.HDR_CMD].To<string>().EqualsNoCase(CONST.CMD_STREAM)
                            && p.Value[CONST.HDR_TOKEN].To<string>().EqualsNoCase(_token))
                .Observe()
                .ObserveOn(TaskPoolScheduler.Default)
                .Subscribe(OnNext, OnCompleted);
        }

        private void OnNext(MPack p)
        {
            try
            {
                MPackMap resp = new MPackMap()
                {
                    {CONST.HDR_CMD, MPack.From(CONST.CMD_STREAM) },
                    {CONST.HDR_TOKEN, MPack.From(_token) },
                };
                if (((MPackMap)p).ContainsKey(CONST.HDR_ID))
                    resp.Add(CONST.HDR_ID, p[CONST.HDR_ID]);

                var method = p[CONST.HDR_METHOD].To<string>().ToUpper();
                switch (method)
                {
                    case "FLUSH":
                        _stream.Flush();
                        break;
                    case "SEEK":
                        resp[CONST.HDR_VALUE] = MPack.From(_stream.Seek(p[CONST.HDR_ARGS][0].To<long>(),
                            (SeekOrigin)p[CONST.HDR_ARGS][0].To<int>()));
                        break;
                    case "SETLENGTH":
                        _stream.SetLength(p[CONST.HDR_ARGS][0].To<long>());
                        break;
                    case "READ":
                        int readCount = p[CONST.HDR_ARGS][0].To<int>();
                        byte[] readBuffer = new byte[readCount];
                        var readResult = _stream.Read(readBuffer, 0, readCount);
                        if (readResult > 0)
                        {
                            if (readResult < readCount)
                                Array.Resize(ref readBuffer, readResult);
                            resp[CONST.HDR_VALUE] = MPack.From(readResult);
                            resp["BUFFER"] = MPack.From(LZ4Wrap.EncodeWrapped(readBuffer, true));
                        }
                        break;
                    case "WRITE":
                        if (!_writable || !_stream.CanWrite)
                            throw new InvalidOperationException("Writing is not supported for this stream");
                        var decomp = LZ4Wrap.DecodeWrapped(p[CONST.HDR_ARGS][0].To<byte[]>());
                        _stream.Write(decomp, 0, decomp.Length);
                        break;
                    case "DISPOSE":
                        this.Dispose();
                        break;
                    case "GET_PROPERTY":
                        var getName = p[CONST.HDR_ARGS][0].To<string>();
                        if (getName.EqualsNoCase("CanWrite"))
                        {
                            resp[CONST.HDR_VALUE] = MPack.From(_writable);
                            break;
                        }
                        var getProp = _stream.GetType().GetProperties().Single(x => x.Name.EqualsNoCase(getName));
                        resp[CONST.HDR_VALUE] = MPack.From(getProp.PropertyType, getProp.GetValue(_stream));
                        break;
                    case "SET_PROPERTY":
                        var setName = p[CONST.HDR_ARGS][0].To<string>();
                        var setProp = _stream.GetType().GetProperties().Single(x => x.Name.EqualsNoCase(setName));
                        setProp.SetValue(_stream, Convert.ChangeType(p[CONST.HDR_ARGS][1].Value, setProp.PropertyType));
                        break;
                }
                resp[CONST.HDR_STATUS] = MPack.From(CONST.STA_NORMAL);
                _publish(resp);
            }
            catch (Exception ex)
            {
                MPackMap err = new MPackMap()
                {
                    {CONST.HDR_CMD, MPack.From(CONST.CMD_STREAM) },
                    {CONST.HDR_TOKEN, MPack.From(_token) },
                    {CONST.HDR_STATUS, MPack.From(CONST.STA_ERROR) },
                    {CONST.HDR_VALUE, MPack.From(ex.Message) },
                };
                if (((MPackMap)p).ContainsKey(CONST.HDR_ID))
                    err.Add(CONST.HDR_ID, p[CONST.HDR_ID]);

                _publish(err);
            }
        }
        private void OnCompleted()
        {
            Dispose();
        }
        public void Dispose()
        {
            _tokenRegistration.Dispose();
            _observable.Dispose();
            _stream.Dispose();
        }
    }
}
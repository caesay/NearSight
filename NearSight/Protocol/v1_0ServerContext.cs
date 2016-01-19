using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reflection;
using System.Runtime.Caching;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Anotar.NLog;
using CS.Network;
using MsgPack;
using NearSight.Network;
using NearSight.Util;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace NearSight.Protocol
{
    internal class v1_0ServerContext : IServerContext
    {
        private readonly RemoterServer _parent;
        private readonly NetClient _client;
        private readonly ObservablePacketProtocol _protocol;
        private readonly IDisposable _observable;
        private ListKvp<string, EventPropegator> _events;
        private List<string> _sessions;
        private CancellationTokenSource _cancelSource;

        public v1_0ServerContext(RemoterServer parent, ObservablePacketProtocol protocol)
        {
            _parent = parent;
            _client = protocol.Client;
            _protocol = protocol;
            _events = new ListKvp<string, EventPropegator>();
            _sessions = new List<string>();
            _cancelSource = new CancellationTokenSource();

            _observable = protocol.Buffer
                // .ObserveOn(ThreadPoolScheduler.Instance)
                .Subscribe(Buffer_OnNext, Buffer_OnError, Buffer_OnComplete);
        }

        private void Buffer_OnComplete()
        {
            var seshs = _sessions.ToArray();
            foreach (var sesh in seshs)
            {
                CloseSession(sesh);
            }
            LogTo.Debug("Client disconnected. ");
        }
        private void Buffer_OnError(Exception ex)
        {
            LogTo.WarnException("Client read error, disposing.", ex);
            this.Dispose();
        }
        private void Buffer_OnNext(ITracked<MPack> p)
        {
            try
            {
                var map = p.Value as MPackMap;
                if (map != null)
                {
                    if (map.ContainsKey(CONST.HDR_CMD))
                    {
                        MPack response = null;
                        switch (map[CONST.HDR_CMD].To<string>())
                        {
                            //case CONST.CMD_USER:
                            //    logCmd = "USER";
                            //    response = User(p.Observe());
                            //    break;
                            //case CONST.CMD_AUTH:
                            //    logCmd = "AUTH";
                            //    response = Auth(p.Observe());
                            //    break;
                            case CONST.CMD_OPEN:
                                response = Open(p.Observe());
                                break;
                            case CONST.CMD_CLOSE:
                                response = Close(p.Observe());
                                break;
                            case CONST.CMD_EXECUTE:
                                response = Execute(p.Observe());
                                break;
                                //default:
                                //    throw new ArgumentException($"Command {map[CONST.HDR_CMD].To<string>()} not recognized.");
                        }
                        if (response != null)
                            WritePacket(response);
                    }
                }
            }
            catch (Exception e)
            {
                p.Observe();
                MPackMap err = new MPackMap();
                err[CONST.HDR_STATUS] = MPack.From(CONST.STA_ERROR);
                err[CONST.HDR_VALUE] = MPack.From(e.Message);
                if (p.Value is MPackMap && ((MPackMap)p.Value).ContainsKey(CONST.HDR_ID))
                    err[CONST.HDR_ID] = p.Value[CONST.HDR_ID];
                WritePacket(err);
            }
        }

        private MPack Open(MPack p)
        {
            var map = (MPackMap)p;
            REndpoint endpoint = null;
            string clToken = null;
            if (map.ContainsKey(CONST.HDR_LOCATION))
                endpoint = _parent.Endpoints.SingleOrDefault(end => end.Path.EqualsNoCase(map[CONST.HDR_LOCATION].To<string>()));
            if (map.ContainsKey(CONST.HDR_TOKEN))
                clToken = map[CONST.HDR_TOKEN].To<string>();

            RSession session = null;
            MPackMap result = new MPackMap();
            if (map.ContainsKey(CONST.HDR_ID))
                result[CONST.HDR_ID] = map[CONST.HDR_ID];

            if (clToken != null)
            {
                session = _parent.Sessions.Get(clToken);
                if (session != null && _sessions.Contains(clToken))
                {
                    result[CONST.HDR_STATUS] = MPack.From(CONST.STA_NORMAL);
                    result[CONST.HDR_VALUE] = MPack.From(clToken);
                    return result;
                }
            }
            else if (endpoint != null)
            {
                object service = endpoint.GenerateService(new RContext(_client.RemoteEndpoint));
                clToken = Guid.NewGuid().ToString();
                session = new RSession(endpoint, service, clToken);
                var policy = new CacheItemPolicy() { SlidingExpiration = _parent.SessionTimeout };
                if (service is IDisposable)
                {
                    policy.RemovedCallback = args => ((IDisposable)args.CacheItem.Value).Dispose();
                }
                _parent.Sessions.Set(clToken, session, policy);
                //_parent._sessions.Add(clToken, session);
            }

            if (session == null)
            {
                result[CONST.HDR_STATUS] = MPack.From(CONST.STA_ERROR);
                result[CONST.HDR_VALUE] = MPack.From("No valid path or token was provided.");
                return result;
            }
            else
            {
                _sessions.Add(clToken);
            }

            foreach (var evt in session.Endpoint.Events)
            {
                _events.Add(clToken, new EventPropegator(session.Instance, evt, (o, args) =>
                {
                    MPackMap evtP = new MPackMap
                        {
                            {CONST.HDR_CMD, MPack.From(CONST.CMD_EVENT)},
                            {CONST.HDR_TOKEN, MPack.From(clToken)},
                            {CONST.HDR_METHOD, MPack.From(evt.Name)},
                            {CONST.HDR_VALUE, ClassifyMPack.Serialize(args)}
                        };
                    WritePacket(evtP);
                }));
            }

            result[CONST.HDR_STATUS] = MPack.From(CONST.STA_NORMAL);
            result[CONST.HDR_VALUE] = MPack.From(clToken);
            return result;
        }
        private MPack Close(MPack p)
        {
            var map = (MPackMap)p;
            MPackMap result = new MPackMap();
            if (map.ContainsKey(CONST.HDR_ID))
                result[CONST.HDR_ID] = map[CONST.HDR_ID];

            if (!map.ContainsKey(CONST.HDR_TOKEN))
            {
                result[CONST.HDR_STATUS] = MPack.From(CONST.STA_ERROR);
                result[CONST.HDR_VALUE] = MPack.From("Parameter missing: " + nameof(CONST.HDR_TOKEN));
                return result;
            }
            var token = map[CONST.HDR_TOKEN].To<string>();

            CloseSession(token);

            result[CONST.HDR_STATUS] = MPack.From(CONST.STA_VOID);
            return result;
        }
        private MPack Execute(MPack p)
        {
            var map = (MPackMap)p;
            //var responseBody = new RResponseBody();
            RSession sesh = null;
            DateTime start = DateTime.Now;
            string method = null;
            MPackMap result = new MPackMap();
            if (map.ContainsKey(CONST.HDR_ID))
                result[CONST.HDR_ID] = map[CONST.HDR_ID];
            try
            {
                // parse client input packet
                var token = map[CONST.HDR_TOKEN].To<string>();
                var sig = map[CONST.HDR_METHOD].To<string>();
                var paramStart = sig.IndexOf('(');
                var paramEnd = sig.IndexOf(')');
                method = sig.Substring(0, paramStart);
                var argTypes = sig.Substring(paramStart, paramEnd - paramStart)
                    .TrimStart('(')
                    .TrimEnd(')')
                    .Split(';')
                    .Select(t => Type.GetType(t.Trim()))
                    .Where(t => t != null)
                    .ToArray();
                var returnType = Type.GetType(sig.Substring(paramEnd + 1).Trim());

                sesh = _parent.Sessions.Get(token);
                if (sesh == null)
                    throw new ArgumentException("Specified token is invalid");

                result[CONST.HDR_TOKEN] = MPack.From(token);

                // get method details, parameters, return type, etc
                //sesh = _parent._sessions[token];
                var methodInfo = sesh.Endpoint.Methods.Single(mth => mth.Name == method);
                var methodParams = methodInfo.Parameters;
                var serverArgTypes = methodParams
                    .Select(x => x.ParameterType)
                    .ToArray();
                var serverRetType = methodInfo.ReturnType;

                if (!Enumerable.SequenceEqual(argTypes, serverArgTypes) || serverRetType != returnType)
                    throw new InvalidOperationException("Method signature does not match that of the server's implementation");

                // check if user is authorized to execute this method
                //if (methodInfo.Attributes.FirstOrDefault(a => a is RRequireAuth) != null)
                //    if (!_authenticated && _parent.CredentialValidator != null)
                //        throw new MethodAccessException("This method requires authentication");

                //var role = methodInfo.Attributes.FirstOrDefault(a => a is RRequireRole) as RRequireRole;
                //if (role != null && _parent.CredentialValidator != null)
                //{
                //    if (!_authenticated || !_parent.CredentialValidator.IsInRole(_user, role.RequiredRole))
                //        throw new MethodAccessException(
                //            $"User must be in {role.RequiredRole} role to access this method.");
                //}

                // deserialize the request body
                //var requestBody = ClassifyJson.Deserialize<RRequestBody>(JsonValue.Parse(p.Payload));
                var mpackArgs = map[CONST.HDR_ARGS];
                object[] reconArgs = new object[serverArgTypes.Length];
                List<int> refParams = new List<int>();
                for (int i = 0; i < reconArgs.Length; i++)
                {
                    var mpk = mpackArgs[i];
                    var type = serverArgTypes[i];
                    var byref = methodParams[i].IsOut || type.IsByRef;
                    var streamed = serverArgTypes[i].IsAssignableFrom(typeof(Stream));
                    if (streamed)
                    {
                        reconArgs[i] = new MessageConsumerStream(mpk.To<string>(), _protocol.Buffer, _cancelSource.Token, WritePacket);
                    }
                    else
                    {
                        if (byref)
                        {
                            refParams.Add(i);
                        }
                        if (mpk.ValueType == MsgPackType.Map)
                            reconArgs[i] = ClassifyMPack.Deserialize(type, mpk);
                        else
                        {
                            if (type.IsByRef)
                                type = type.GetElementType();
                            //reconArgs[i] = ExactConvert.To(type, mpk.Value);
                            reconArgs[i] = Convert.ChangeType(mpk.Value, type);
                        }
                    }
                }

                // validate that we have all the required parameters
                for (int i = 0; i < reconArgs.Length; i++)
                {
                    //if (reconArgs[i] == null && !methodParams[i].IsOptional && !methodParams[i].IsOut)
                    //    throw new ArgumentException("A required method parameter is missing.");
                    if (reconArgs[i] == null && methodParams[i].IsOptional)
                        reconArgs[i] = Type.Missing;
                }

                //var returnValue = methodInfo.Delegate(sesh.Instance, reconArgs);
                var returnValue = methodInfo.Method.Invoke(sesh.Instance, reconArgs);

                if (returnValue != null && !returnType.IsInstanceOfType(returnValue))
                    throw new InvalidOperationException("Return value type mismatch");

                Func<Type, object, MPack> sanitizeParam = (type, o) =>
                {
                    if (type.IsByRef)
                        type = type.GetElementType();
                    var tcode = (int)Type.GetTypeCode(type);
                    if (tcode > 2 || type == typeof(byte[]))
                        return MPack.From(type, o);
                    else
                        return ClassifyMPack.Serialize(type, o);
                };

                // populate ref / out parameters to the response body
                if (refParams.Any())
                {
                    var refoutParams = new MPackArray(Enumerable.Range(0, reconArgs.Length).Select(v => MPack.Null()));
                    for (int i = 0; i < refParams.Count; i++)
                    {
                        var paramIndex = refParams[i];
                        refoutParams[paramIndex] = sanitizeParam(serverArgTypes[paramIndex], reconArgs[paramIndex]);
                    }
                    result[CONST.HDR_ARGS] = refoutParams;
                }

                // set the return value of response body
                if (typeof(Stream).IsAssignableFrom(returnType))
                {
                    bool writable = methodInfo.Method.ReturnParameter.GetCustomAttributes(typeof(RWritable)).Any();
                    string tkn = Guid.NewGuid().ToString();
                    var wrappedStream = new MessageProviderStream(tkn, (Stream)returnValue, _protocol.Buffer,
                        WritePacket, _cancelSource.Token, writable);

                    result[CONST.HDR_STATUS] = MPack.From(CONST.STA_STREAM);
                    result[CONST.HDR_VALUE] = MPack.From(tkn);
                }
                else if (returnType == typeof(void))
                {
                    result[CONST.HDR_STATUS] = MPack.From(CONST.STA_VOID);
                }
                else if (Attribute.IsDefined(returnType ?? typeof(object), typeof(RContractProvider)))
                {
                    result[CONST.HDR_STATUS] = MPack.From(CONST.STA_SERVICE);
                    REndpoint endpoint = _parent.Endpoints.SingleOrDefault(ed => ed.Interface == returnType);
                    if (endpoint == null)
                        endpoint = new REndpoint(RandomEx.GetString(20), returnType, context => null);
                    // create new session
                    var ses = new RSession(endpoint, returnValue);
                    var policy = new CacheItemPolicy() { SlidingExpiration = _parent.SessionTimeout };
                    _parent.Sessions.Add(ses.Token, ses, policy);
                    if (returnValue is IDisposable)
                    {
                        policy.RemovedCallback = args => ((IDisposable)args.CacheItem.Value).Dispose();
                    }
                    result[CONST.HDR_VALUE] = MPack.From(ses.Token);
                }
                else
                {
                    result[CONST.HDR_STATUS] = MPack.From(CONST.STA_NORMAL);
                    result[CONST.HDR_VALUE] = sanitizeParam(returnType, returnValue);
                }
            }
            catch (Exception ex)
            {
                string message;
                string type;
                if (ex is TargetInvocationException || ex is AggregateException)
                {
                    type = ex.InnerException.GetType().Name;
                    message = ex.InnerException.Message;
                }
                else
                {
                    type = ex.GetType().Name;
                    message = ex.Message;
                }

                result[CONST.HDR_STATUS] = MPack.From(CONST.STA_ERROR);
                result[CONST.HDR_VALUE] = MPack.From($"({type}){message}");
                result[CONST.HDR_LOCATION] = MPack.From($"{sesh?.Endpoint.Path ?? "[invalid]"}/{method ?? "[invalid]"}()");
            }
            DateTime end = DateTime.Now;
            var time = end - start;
            result[CONST.HDR_TIME] = MPack.FromDouble(time.TotalMilliseconds);
            return result;
        }

        private void WritePacket(MPack p)
        {
            var map = p as MPackMap;
            if (map == null)
                return;

            int id = -1;
            string cmd = "null";
            if (map?.ContainsKey(CONST.HDR_ID) == true)
                id = map[CONST.HDR_ID].To<int>();
            if (map?.ContainsKey(CONST.HDR_CMD) == true)
                cmd = map[CONST.HDR_CMD].To<string>();

            _protocol.Write(p);
        }

        private void CloseSession(string token)
        {
            _sessions.Remove(token);
            if (_parent.Sessions.ContainsKey(token))
            {
                var s = _parent.Sessions.Get(token);
                if (s != null)
                {
                    if (s.Instance != null && s.Instance is IDisposable)
                    {
                        ((IDisposable)s.Instance).Dispose();
                    }
                    s.Dispose();
                    _parent.Sessions.Delete(token);
                }

                // dispose event handlers for this session
                _events.GetAllByKey(token).ToList().ForEach(kvp =>
                {
                    kvp.Value.Dispose();
                    _events.Remove(kvp);
                });
            }
        }
        public void Dispose()
        {
            try
            {
                _observable.Dispose();
                _cancelSource.Cancel();
                foreach (var pair in _events)
                {
                    pair.Value.Dispose();
                }
                _events.Clear();
                _client.Close();
            }
            catch when (!_parent.PropagateExceptions)
            {
            }
        }
    }
}

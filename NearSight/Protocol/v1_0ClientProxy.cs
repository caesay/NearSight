using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Reflection;
using System.Runtime.Remoting.Messaging;
using System.Runtime.Remoting.Proxies;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MsgPack;
using NearSight.Network;
using NearSight.Util;
using RT.Util;
using RT.Util.ExtensionMethods;

namespace NearSight.Protocol
{
    internal sealed class v1_0ClientProxy : CommunicationObject
    {
        private readonly RemoterFactory _factory;
        private string _path;
        private string _token;
        private IDisposable _eventWatcher;
        private Dictionary<EventInfo, List<object>> _events;
        private RealProxyProxy _proxy;
        private object _extendedProxy;

        private v1_0ClientProxy(RemoterFactory factory, Type t)
        {
            var extendedInterfaceType = typeof(IExtendedProxy<>).MakeGenericType(t);
            var extendedType = typeof(PacketExtendedProxy<>).MakeGenericType(t);
            var extendedCtor = extendedType.GetConstructor(new Type[] { this.GetType() });
            _extendedProxy = extendedCtor.Invoke(new[] { this });

            _proxy = new RealProxyProxy(DynamicTypeFactory.Merge(t, extendedInterfaceType, typeof(IDisposable)), Invoke);
            _factory = factory;
            _events = (from m in t.GetEvents()
                       let attrs = m.GetCustomAttributes(typeof(REvent))
                       where attrs.Any()
                       select m).ToDictionary(ev => ev, ev => new List<object>());
        }

        ~v1_0ClientProxy()
        {
            _eventWatcher.Dispose();
        }

        public static v1_0ClientProxy FromPath(RemoterFactory factory, Type t, string path)
        {
            return new v1_0ClientProxy(factory, t) { _path = path };
        }
        public static v1_0ClientProxy FromToken(RemoterFactory factory, Type t, string token)
        {
            return new v1_0ClientProxy(factory, t) { _token = token };
        }

        public override void Open()
        {
            using (new AsyncContextChange())
            {
                var t = OpenAsync();
                t.Wait(_factory.Options.OperationTimeout);
            }
        }
        public override void Close()
        {
            using (new AsyncContextChange())
            {
                var t = CloseAsync();
                t.Wait(_factory.Options.OperationTimeout);
            }
        }
        public override void Abort()
        {
            CloseAsync();
        }

        public override async Task OpenAsync(CancellationToken cancelToken)
        {
            if (State == CommunicationState.Opened)
                return;

            SetState(CommunicationState.Opening);
            var id = MsgId.Get();
            MPackMap req = new MPackMap();
            req[CONST.HDR_CMD] = MPack.From(CONST.CMD_OPEN);
            if (!String.IsNullOrEmpty(_path))
                req[CONST.HDR_LOCATION] = MPack.From(_path);
            else if (!String.IsNullOrEmpty(_token))
                req[CONST.HDR_TOKEN] = MPack.From(_token);
            else
                throw new InvalidOperationException("No token or path specified");
            req[CONST.HDR_ID] = MPack.From(id);

            var task = _factory.Protocol.Buffer
                .Where(m =>
                {
                    var map = m.Value as MPackMap;
                    return map != null && map.ContainsKey(CONST.HDR_ID) && map[CONST.HDR_ID].To<int>() == id;
                }).Timeout(_factory.Options.RecieveTimeout).Take(1).Observe()
            .Select(m => (MPackMap)m).ToTask(cancelToken);

            await _factory.Protocol.WriteAsync(req, cancelToken).ConfigureAwait(false);
            var resp = await task.ConfigureAwait(false);
            var token = (string)await ParseResponseToObjectOrThrow(typeof(string), resp).ConfigureAwait(false);
            _token = token;

            _eventWatcher?.Dispose();

            // propagate events that arrive on the stream to any applicable event handlers
            _eventWatcher = _factory.Protocol.Buffer
                .Where(track => track.Value is MPackMap
                               && ((MPackMap)track.Value).ContainsKeys(new[] { CONST.HDR_CMD, CONST.HDR_TOKEN, CONST.HDR_METHOD })
                               && track.Value[CONST.HDR_CMD].To<string>() == CONST.CMD_EVENT
                               && track.Value[CONST.HDR_TOKEN].To<string>() == _token)
                .Observe()
                .Subscribe((next) =>
                {
                    var eventInfo = _events.Single(kvp => kvp.Key.Name.Equals(next[CONST.HDR_METHOD].To<string>()));
                    object eventArgs = ClassifyMPack.Deserialize<EventArgs>(next[CONST.HDR_VALUE]);
                    foreach (object deleg in eventInfo.Value)
                    {
                        var invMeth = deleg.GetType().GetMethod("Invoke");
                        invMeth.Invoke(deleg, new object[]
                        {
                                // return a transparent proxy as the event sender object
                                this.GetTransparentProxy(),
                                eventArgs
                        });
                    }
                });
            SetState(CommunicationState.Opened);
        }
        public override async Task CloseAsync(CancellationToken token)
        {
            if (State == CommunicationState.Closing || State == CommunicationState.Opening)
                throw new InvalidOperationException("Can not close proxy while currently " + State.ToString());

            if (State != CommunicationState.Opened)
            {
                SetState(CommunicationState.Closed);
                return;
            }

            SetState(CommunicationState.Closing);
            _eventWatcher?.Dispose();
            var id = MsgId.Get();
            MPackMap req = new MPackMap();
            req[CONST.HDR_CMD] = MPack.From(CONST.CMD_CLOSE);
            req[CONST.HDR_TOKEN] = MPack.From(_token);
            req[CONST.HDR_ID] = MPack.From(id);

            var task = _factory.Protocol.Buffer.Where(m =>
            {
                var map = m.Value as MPackMap;
                return map != null && map.ContainsKey(CONST.HDR_ID) && map[CONST.HDR_ID].To<int>() == id;
            }).Timeout(_factory.Options.RecieveTimeout).Take(1).Observe()
            .Select(m => (MPackMap)m).ToTask(token);

            await _factory.Protocol.WriteAsync(req, token).ConfigureAwait(false);
            var resp = await task.ConfigureAwait(false);
            _token = null;
            SetState(CommunicationState.Closed);
        }

        #region RealProxy Impl
        public object GetTransparentProxy()
        {
            return _proxy.GetTransparentProxy();
        }
        private bool CheckTypesEqual(Type t1, Type t2)
        {
            if (t1.IsGenericParameter || t2.IsGenericParameter)
                return true;
            if (t1.IsGenericType && t2.IsGenericType)
            {
                if (t1.GetGenericTypeDefinition() != t2.GetGenericTypeDefinition())
                    return false;
                var t1args = t1.GetGenericArguments();
                var t2args = t2.GetGenericArguments();
                if (t1args.Length != t2args.Length)
                    return false;
                for (int i = 0; i < t1args.Length; i++)
                {
                    if (!CheckTypesEqual(t1args[i], t2args[i]))
                        return false;
                }
                return true;
            }
            return t1 == t2;
        }
        private bool CheckObjectContainsMethod(MethodInfo m, object obj)
        {
            var whereMethods = obj.GetType()
                .GetMethods()
                .Where(method => method.Name.EqualsNoCase(m.Name))
                .Where(method => CheckTypesEqual(method.ReturnType, m.ReturnType))
                .Where(method =>
                {
                    var p1 = method.GetParameters();
                    var p2 = m.GetParameters();
                    if (p1.Length != p2.Length)
                        return false;
                    for (int i = 0; i < p1.Length; i++)
                    {
                        if (!CheckTypesEqual(p1[i].ParameterType, p2[i].ParameterType))
                            return false;
                    }
                    return true;
                });
            return whereMethods.SingleOrDefault() != null;
        }
        private IMessage Invoke(IMessage rawMsg)
        {
            var mcm = (IMethodCallMessage)rawMsg;
            var m = mcm.MethodBase as MethodInfo;
            var args = mcm.Args;

            if (m == null || args == null)
                throw new InvalidOperationException("The transparent proxy received an invalid message.");

            // if invocation is a event registration/removal, pass to InvokeEvent
            if ((m.Name.StartsWith("add_") || m.Name.StartsWith("remove_")))
            {
                return InvokeEvent(mcm);
            }

            // if invocation is for an interface that is implemented by this RealProxy
            if (m.DeclaringType.IsGenericType
                && m.DeclaringType.GetGenericTypeDefinition() == (typeof(IExtendedProxy<>))
                && CheckObjectContainsMethod(m, _extendedProxy))
                return InvokeLocal(_extendedProxy, mcm);

            if (CheckObjectContainsMethod(m, this))
                return InvokeLocal(this, mcm);

            // else attempt to invoke remote method
            return InvokeRemote(mcm);
        }
        private ReturnMessage InvokeEvent(IMethodCallMessage mcm)
        {
            var m = mcm.MethodBase as MethodInfo;
            var args = mcm.Args;
            var nameSplit = m.Name.Split('_');
            string name = nameSplit.Skip(1).JoinString();

            var eventInfo = _events.Single(kvp => kvp.Key.Name.Equals(name));
            string function = nameSplit[0];

            if (function == "add")
            {
                eventInfo.Value.Add(args[0]);
            }
            else if (function == "remove")
            {
                eventInfo.Value.RemoveAll(ev => ev.Equals(args[0]));
            }
            else
            {
                return new ReturnMessage(new InvalidOperationException("The transparent proxy received an invalid message."), mcm);
            }
            return new ReturnMessage(null, null, 0, mcm.LogicalCallContext, mcm);
        }
        private ReturnMessage InvokeRemote(IMethodCallMessage mcm)
        {
            var method = (MethodInfo)mcm.MethodBase;
            try
            {
                var task = ExecuteRemoteCall(method, mcm.Args);
                var resp = task.ConfigureAwait(false).GetAwaiter().GetResult();
                var result = ParseResponseToObjectOrThrow(method.ReturnType, resp).ConfigureAwait(false).GetAwaiter().GetResult();
                var reconArgs = new object[0];
                if (resp.ContainsKey(CONST.HDR_ARGS))
                {
                    var mpackArgs = (MPackArray)resp[CONST.HDR_ARGS];
                    reconArgs = new object[mpackArgs.Count];
                    for (int i = 0; i < reconArgs.Length; i++)
                    {
                        var mpk = mpackArgs[i];
                        //var type = mcm.Args[i].GetType();
                        var type = method.GetParameters()[i].ParameterType;
                        if (type.IsByRef)
                            type = type.GetElementType();
                        if (mpk.ValueType == MsgPackType.Map)
                            reconArgs[i] = ClassifyMPack.Deserialize(type, mpk);
                        else
                        {
                            reconArgs[i] = Convert.ChangeType(mpk.Value, type);
                            //reconArgs[i] = ExactConvert.To(type, mpk.Value);
                        }
                    }
                }
                return new ReturnMessage(result, reconArgs, reconArgs.Length, mcm.LogicalCallContext, mcm);
            }
            catch (Exception e)
            {
                return new ReturnMessage(e, mcm);
            }
        }
        private ReturnMessage InvokeLocal(object obj, IMethodCallMessage mcm)
        {
            MethodInfo method = (MethodInfo)mcm.MethodBase;
            try
            {
                var args = mcm.Args;
                object callResult = method.Invoke(obj, args);
                LogicalCallContext context = mcm.LogicalCallContext;
                return new ReturnMessage(callResult, args, args.Count(), context, mcm);
            }
            catch (Exception e)
            {
                return new ReturnMessage(e, mcm);
            }
        }
        #endregion

        #region Remoting Methods
        protected async Task<MPackMap> ExecuteRemoteCall(MethodInfo method, object[] paramaters)
        {
            var id = MsgId.Get();
            MPackMap req = new MPackMap();
            req[CONST.HDR_CMD] = MPack.From(CONST.CMD_EXECUTE);
            req[CONST.HDR_ID] = MPack.From(id);
            req[CONST.HDR_TOKEN] = MPack.From(_token);
            req[CONST.HDR_LOCATION] = MPack.From(_path);
            req[CONST.HDR_METHOD] = MPack.From(CreateMethodName(method));
            req[CONST.HDR_ARGS] = new MPackArray(paramaters.Select(p =>
                p != null ? SanitizeParam(p.GetType(), p) : MPack.Null()));
            var resp = _factory.Protocol.Buffer.Where(m =>
            {
                var map = m.Value as MPackMap;
                if (map != null && map.ContainsKey(CONST.HDR_ID) && map.ContainsKey(CONST.HDR_TOKEN))
                    if (map[CONST.HDR_TOKEN].To<string>().EqualsNoCase(_token) && map[CONST.HDR_ID].To<int>() == id)
                        return true;
                return false;
            }).Timeout(_factory.Options.RecieveTimeout).Take(1).Observe()
            .Select(m => (MPackMap)m).ToTask();
            await _factory.Protocol.WriteAsync(req);
            return await resp;
        }
        private MPack SanitizeParam(Type type, object o)
        {
            var tcode = (int)Type.GetTypeCode(type);
            if (tcode > 2)
                return MPack.From(type, o);
            else
                return ClassifyMPack.Serialize(type, o);
        }
        protected async Task<object> ParseResponseToObjectOrThrow(Type returnType, MPackMap map)
        {
            switch (map[CONST.HDR_STATUS].To<string>())
            {
                case CONST.STA_VOID:
                    return null;
                case CONST.STA_NORMAL:
                    var value = map[CONST.HDR_VALUE];
                    if (value.ValueType == MsgPackType.Map)
                        return ClassifyMPack.Deserialize(returnType, value);
                    return ExactConvert.To(returnType, value.Value);
                case CONST.STA_SERVICE:
                    string tok = map[CONST.HDR_VALUE].To<string>();
                    var prox = v1_0ClientProxy.FromToken(_factory, returnType, tok);
                    await prox.OpenAsync();
                    return prox.GetTransparentProxy();
                case CONST.STA_STREAM:
                    return new MessageConsumerStream(map[CONST.HDR_VALUE].To<string>(), _factory.Protocol.Buffer,
                    _factory.CancelToken, (pack, token) => _factory.Protocol.WriteAsync(pack, token));
                case CONST.STA_ERROR:
                    throw new Exception(map[CONST.HDR_VALUE].To<string>());
                default:
                    throw new NotSupportedException("Server has returned an unsupported type");
            }
        }
        protected async Task<RemoteResult> ParseResponse(MethodInfo method, MPackMap map)
        {
            var remote = new RemoteResult();
            try
            {
                var result = await ParseResponseToObjectOrThrow(null, map);
                if (map.ContainsKey(CONST.HDR_TIME))
                    remote.ExecutionTime = TimeSpan.FromMilliseconds(map[CONST.HDR_TIME].To<double>());
                if (map.ContainsKey(CONST.HDR_ARGS))
                {
                    var mpackArgs = (MPackArray)map[CONST.HDR_ARGS];
                    var reconArgs = new object[mpackArgs.Count];
                    for (int i = 0; i < reconArgs.Length; i++)
                    {
                        var mpk = mpackArgs[i];
                        var type = method.GetParameters()[i].ParameterType;
                        if (type.IsByRef)
                            type = type.GetElementType();
                        if (mpk.ValueType == MsgPackType.Map)
                            reconArgs[i] = ClassifyMPack.Deserialize(type, mpk);
                        else
                        {
                            reconArgs[i] = Convert.ChangeType(mpk.Value, type);
                        }
                    }
                    remote.RefParams = reconArgs;
                }
                remote.State = RemoteResultState.Completed;
            }
            catch (Exception e)
            {
                remote.State = RemoteResultState.Faulted;
                remote.Exception = e;
            }
            return remote;
        }
        protected async Task<RemoteResult<Y>> ParseResponse<Y>(MethodInfo method, MPackMap map)
        {
            var remote = new RemoteResult<Y>();
            try
            {
                var result = await ParseResponseToObjectOrThrow(typeof(Y), map);
                remote.Result = (Y)result;
                if (map.ContainsKey(CONST.HDR_TIME))
                    remote.ExecutionTime = TimeSpan.FromMilliseconds(map[CONST.HDR_TIME].To<double>());
                if (map.ContainsKey(CONST.HDR_ARGS))
                {
                    var mpackArgs = (MPackArray)map[CONST.HDR_ARGS];
                    var reconArgs = new object[mpackArgs.Count];
                    for (int i = 0; i < reconArgs.Length; i++)
                    {
                        var mpk = mpackArgs[i];
                        var type = method.GetParameters()[i].ParameterType;
                        if (type.IsByRef)
                            type = type.GetElementType();
                        if (mpk.ValueType == MsgPackType.Map)
                            reconArgs[i] = ClassifyMPack.Deserialize(type, mpk);
                        else
                        {
                            reconArgs[i] = Convert.ChangeType(mpk.Value, type);
                        }
                    }
                    remote.RefParams = reconArgs;
                }
                remote.State = RemoteResultState.Completed;
            }
            catch (Exception e)
            {
                remote.State = RemoteResultState.Faulted;
                remote.Exception = e;
            }
            return remote;
        }
        private string CreateMethodName(MethodInfo m)
        {
            return "{0}({1}) {2}".Fmt(
                m.Name,
                m.GetParameters().Select(p => GetTypeString(p.ParameterType)).JoinString("; "),
                GetTypeString(m.ReturnType));
        }

        private string GetTypeString(Type t)
        {
            if (IsSimpleType(t) || t.Module.ScopeName == "CommonLanguageRuntimeLibrary")
                return t.FullName;

            return t.AssemblyQualifiedName;
        }
        public static bool IsSimpleType(Type type)
        {
            return
                type.IsPrimitive ||
                new Type[] {
            typeof(Enum),
            typeof(String),
            typeof(Decimal),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid)
                }.Contains(type) ||
                Convert.GetTypeCode(type) != TypeCode.Object ||
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && IsSimpleType(type.GetGenericArguments()[0]))
                ;
        }
        #endregion

        #region RealProxy
        private class RealProxyProxy : RealProxy
        {
            private readonly Func<IMessage, IMessage> _invoke;
            public RealProxyProxy(Type t, Func<IMessage, IMessage> invoke)
                : base(t)
            {
                _invoke = invoke;
            }
            public override IMessage Invoke(IMessage msg)
            {
                return _invoke(msg);
            }
        }
        #endregion

        #region IExtendedProxy<T>
        private class PacketExtendedProxy<T> : IExtendedProxy<T>
        {
            private readonly v1_0ClientProxy _proxy;

            public PacketExtendedProxy(v1_0ClientProxy proxy)
            {
                _proxy = proxy;
            }

            #region IExtendedProxy<T>
            public RemoteResult Call(Expression<Action<T>> function)
            {
                var method = GetMethodFromExpr(function);
                var result = _proxy.ExecuteRemoteCall(method.Item1, method.Item2).ConfigureAwait(false).GetAwaiter().GetResult();
                return _proxy.ParseResponse(method.Item1, result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            public RemoteResult<Y> Call<Y>(Expression<Func<T, Y>> function)
            {
                var method = GetMethodFromExpr(function);
                var result = _proxy.ExecuteRemoteCall(method.Item1, method.Item2).ConfigureAwait(false).GetAwaiter().GetResult();
                return _proxy.ParseResponse<Y>(method.Item1, result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            public RemoteResult Set<Y>(Expression<Func<T, Y>> property, Y value)
            {
                var prop = GetPropertyFromExpr(property);
                if (!prop.CanWrite)
                    throw new ArgumentException("The specified property does not support writing");
                var result = _proxy.ExecuteRemoteCall(prop.SetMethod, new object[] { value }).ConfigureAwait(false).GetAwaiter().GetResult();
                return _proxy.ParseResponse(prop.SetMethod, result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            public RemoteResult<Y> Get<Y>(Expression<Func<T, Y>> property)
            {
                var prop = GetPropertyFromExpr(property);
                if (!prop.CanRead)
                    throw new ArgumentException("The specified property does not support reading");
                var result = _proxy.ExecuteRemoteCall(prop.GetMethod, new object[0]).ConfigureAwait(false).GetAwaiter().GetResult();
                return _proxy.ParseResponse<Y>(prop.GetMethod, result).ConfigureAwait(false).GetAwaiter().GetResult();
            }
            public async Task<RemoteResult> CallAsync(Expression<Action<T>> function)
            {
                var method = GetMethodFromExpr(function);
                var result = await _proxy.ExecuteRemoteCall(method.Item1, method.Item2);
                return await _proxy.ParseResponse(method.Item1, result);
            }
            public async Task<RemoteResult<Y>> CallAsync<Y>(Expression<Func<T, Y>> function)
            {
                var method = GetMethodFromExpr(function);
                var result = await _proxy.ExecuteRemoteCall(method.Item1, method.Item2);
                return await _proxy.ParseResponse<Y>(method.Item1, result);
            }
            public async Task<RemoteResult> SetAsync<Y>(Expression<Func<T, Y>> property, Y value)
            {
                var prop = GetPropertyFromExpr(property);
                if (!prop.CanWrite)
                    throw new ArgumentException("The specified property does not support writing");
                var result = await _proxy.ExecuteRemoteCall(prop.SetMethod, new object[] { value });
                return await _proxy.ParseResponse(prop.SetMethod, result);
            }
            public async Task<RemoteResult<Y>> GetAsync<Y>(Expression<Func<T, Y>> property)
            {
                var prop = GetPropertyFromExpr(property);
                if (!prop.CanRead)
                    throw new ArgumentException("The specified property does not support reading");
                var result = await _proxy.ExecuteRemoteCall(prop.GetMethod, new object[0]);
                return await _proxy.ParseResponse<Y>(prop.GetMethod, result);
            }

            public void Close()
            {
                _proxy.Close();
            }
            public Task CloseAsync()
            {
                return _proxy.CloseAsync();
            }
            public Task CloseAsync(CancellationToken token)
            {
                return _proxy.CloseAsync(token);
            }

            private Tuple<MethodInfo, object[]> GetMethodFromExpr(Expression exp)
            {
                switch (exp.NodeType)
                {
                    case ExpressionType.Call:
                        var exprCall = (MethodCallExpression)exp;
                        var arguments = new object[exprCall.Arguments.Count];
                        var method = exprCall.Method;
                        for (int i = 0; i < exprCall.Arguments.Count; i++)
                        {

                            var expr = exprCall.Arguments[i];
                            if (expr is ConstantExpression)
                            {
                                arguments[i] = ((ConstantExpression)expr).Value;
                            }
                            else if (expr is MemberExpression)
                            {
                                //MemberExpression right = (MemberExpression)((BinaryExpression)p.Body).Right;
                                var obj = Expression.Lambda(expr).Compile().DynamicInvoke();
                                arguments[i] = obj;
                            }
                            else
                            {
                                throw new NotSupportedException($"Expression argument {i} is of unknown type {expr.GetType().Name}.");
                            }
                        }
                        return Tuple.Create(method, arguments);
                    case ExpressionType.Lambda:
                        return GetMethodFromExpr(((LambdaExpression)exp).Body);
                    default:
                        throw new ArgumentException("Expression was not a known or supported method call.");
                }
            }
            private PropertyInfo GetPropertyFromExpr(Expression exp)
            {
                switch (exp.NodeType)
                {
                    case ExpressionType.MemberAccess:
                        var memberExpr = (MemberExpression)exp;
                        MemberInfo member = memberExpr.Member;
                        if (member is PropertyInfo)
                        {
                            return (PropertyInfo)member;
                        }
                        throw new ArgumentException("Provided Expression does not refer to property access.");
                    case ExpressionType.Lambda:
                        return GetPropertyFromExpr(((LambdaExpression)exp).Body);
                    default:
                        throw new ArgumentException("Expression was not a known or supported call.");
                }
            }
            #endregion

            public void Dispose()
            {
                _proxy.Abort();
            }
        }
        #endregion
    }

}

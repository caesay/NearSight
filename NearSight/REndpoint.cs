using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NearSight.Util;

namespace NearSight
{
    public sealed class REndpoint
    {
        public string Path { get; private set; }
        public Type Interface { get; private set; }
        internal readonly Func<RContext, object> GenerateService;
        internal CompiledMethodInfo[] Methods;
        internal EventInfo[] Events;
        internal PropertyInfo[] Properties;

        public REndpoint(string path, Type inInterface, Func<RContext, object> generateService)
        {
            GenerateService = generateService;
            Path = path;
            Interface = inInterface;
            if (!Interface.GetCustomAttributes(typeof(RContractProvider)).Any())
                throw new ArgumentException("Interface must be decorated with RContractProvider Attribute");
            Properties = (from p in Interface.GetProperties()
                          let attrs = p.GetCustomAttributes(typeof(RProperty))
                          where attrs.Any()
                          select p).ToArray();

            //var requiresAuth = Interface.GetCustomAttributes(typeof (RRequireAuth)).FirstOrDefault();
            //var requiresRole = Interface.GetCustomAttributes(typeof (RRequireRole)).FirstOrDefault();

            //var customAttributes = new [] {requiresAuth, requiresRole}.Where(a => a != null).ToArray();

            var propmethods = Properties.SelectMany(prop =>
            {
                if (prop.CanRead && prop.CanWrite)
                    return new [] { prop.GetMethod, prop.SetMethod };
                else if (prop.CanRead)
                    return new [] { prop.GetMethod };
                return new [] { prop.SetMethod };
            });

            Methods = (from m in Interface.GetMethods()
                       let attrs = m.GetCustomAttributes(typeof(ROperation))
                       where attrs.Any()
                       select m).Concat(propmethods)
                       //.Select(m => new CompiledMethodInfo(m, customAttributes))
                       .Select(m => new CompiledMethodInfo(m))
                       .ToArray();

            Events = (from m in Interface.GetEvents()
                      let attrs = m.GetCustomAttributes(typeof(REvent))
                      where attrs.Any()
                      select m).ToArray();

            var tmp = Events.Select(evt => evt.EventHandlerType
                .GetMethod("Invoke")
                .GetParameters()
                .ToArray());
            foreach (var prms in tmp)
            {
                int count = prms.Length;
                bool obj0 = prms[0].ParameterType == typeof(object);
                bool obj1 = typeof(EventArgs).IsAssignableFrom(prms[1].ParameterType);
                if (count != 2 || !obj0 || !obj1)
                    throw new ArgumentException("All REvent's must follow the standard Eventhandler<EventArgs> pattern");
            }
        }
    }
}

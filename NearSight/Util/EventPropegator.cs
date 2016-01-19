using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Util
{
    internal class EventPropegator : IDisposable
    {
        private readonly object _instance;
        private readonly EventInfo _evt;
        private readonly Delegate _delegate;
        private bool _disposed;

        public EventPropegator(object instance, EventInfo evt, Action<object, EventArgs> handler)
        {
            _instance = instance;
            _evt = evt;
            _disposed = false;

            var methodInfo = handler.GetType().GetMethod("Invoke");
            _delegate = Delegate.CreateDelegate(evt.EventHandlerType, handler, methodInfo);

            evt.AddEventHandler(instance, _delegate);
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _evt.RemoveEventHandler(_instance, _delegate);
        }
    }
}

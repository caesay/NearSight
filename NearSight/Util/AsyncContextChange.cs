using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NearSight.Util
{
    public class AsyncContextChange : IDisposable
    {
        private SynchronizationContext previous;
        public AsyncContextChange(SynchronizationContext newContext = null)
        {
            previous = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(newContext);
        }

        public void Dispose()
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Util
{
    public interface IAsyncDisposable
    {
        Task DisposeAsync();
    }

    public static class Async
    {
        public static async Task Using<TResource>(TResource resource, Func<TResource, Task> body)
            where TResource : IAsyncDisposable
        {
            Exception exception = null;
            try
            {
                await body(resource);
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            await resource.DisposeAsync();
            if (exception != null)
            {
                var info = ExceptionDispatchInfo.Capture(exception);
                info.Throw();
            }
        }
    }
}

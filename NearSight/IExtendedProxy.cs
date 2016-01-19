using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NearSight
{
    public interface IExtendedProxy<T> : IDisposable
    {
        RemoteResult Call(Expression<Action<T>> function);
        RemoteResult<Y> Call<Y>(Expression<Func<T, Y>> function);
        RemoteResult Set<Y>(Expression<Func<T, Y>> property, Y value);
        RemoteResult<Y> Get<Y>(Expression<Func<T, Y>> property);

        Task<RemoteResult> CallAsync(Expression<Action<T>> expression);
        Task<RemoteResult<Y>> CallAsync<Y>(Expression<Func<T, Y>> expression);
        Task<RemoteResult> SetAsync<Y>(Expression<Func<T, Y>> property, Y value);
        Task<RemoteResult<Y>> GetAsync<Y>(Expression<Func<T, Y>> property);

        void Close();
        Task CloseAsync();
        Task CloseAsync(CancellationToken token);
    }

    public class RemoteResult<T> : RemoteResult
    {
        private T _result;

        public T Result
        {
            get
            {
                if (State == RemoteResultState.Faulted)
                    if (Exception != null)
                        throw Exception;
                    else
                        throw new Exception("The call failed for an unspecified reason.");
                return _result;
            }
            internal set { _result = value; }
        }
    }
    public class RemoteResult
    {
        public RemoteResultState State { get; internal set; }
        public Exception Exception { get; internal set; }
        public object[] RefParams { get; internal set; }
        public TimeSpan ExecutionTime { get; internal set; }
    }

    public enum RemoteResultState
    {
        Completed,
        Faulted,
    }
}

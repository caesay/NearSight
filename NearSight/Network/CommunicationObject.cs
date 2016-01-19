using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NearSight.Network
{
    public enum CommunicationState
    {
        Created,
        Opening,
        Opened,
        Closing,
        Closed,
        Faulted
    }
    public interface ICommunicationObject
    {
        CommunicationState State { get; }
        event EventHandler Closed;
        event EventHandler Closing;
        event EventHandler Faulted;
        event EventHandler Opened;
        event EventHandler Opening;
        event EventHandler StateChanged;
        void Open();
        Task OpenAsync();
        Task OpenAsync(CancellationToken token);
        void Close();
        Task CloseAsync();
        Task CloseAsync(CancellationToken token);
        void Abort();
    }

    public abstract class CommunicationObject : ICommunicationObject
    {
        public CommunicationState State { get; private set; } = CommunicationState.Created;

        public event EventHandler StateChanged;
        public event EventHandler Closed;
        public event EventHandler Closing;
        public event EventHandler Faulted;
        public event EventHandler Opened;
        public event EventHandler Opening;

        protected static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
        protected readonly object _lock = new object();

        public abstract void Open();

        public Task OpenAsync()
        {
            return OpenAsync(CancellationToken.None);
        }
        public abstract Task OpenAsync(CancellationToken token);

        public abstract void Close();

        public Task CloseAsync()
        {
            return CloseAsync(CancellationToken.None);
        }
        public abstract Task CloseAsync(CancellationToken token);

        public abstract void Abort();

        protected virtual void SetState(CommunicationState state)
        {
            State = state;
            switch (state)
            {
                case CommunicationState.Created:
                    break;
                case CommunicationState.Opening:
                    Opening?.Invoke(this, new EventArgs());
                    break;
                case CommunicationState.Opened:
                    Opened?.Invoke(this, new EventArgs());
                    break;
                case CommunicationState.Closing:
                    Closing?.Invoke(this, new EventArgs());
                    break;
                case CommunicationState.Closed:
                    Closed?.Invoke(this, new EventArgs());
                    break;
                case CommunicationState.Faulted:
                    Faulted?.Invoke(this, new EventArgs());
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(state), state, null);
            }
            StateChanged?.Invoke(this, new EventArgs());
        }
    }
}

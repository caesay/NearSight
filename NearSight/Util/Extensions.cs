using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RT.Util.ExtensionMethods;

namespace NearSight.Util
{
    internal static class Extensions
    {
        //http://stackoverflow.com/a/13742421/184746
        public static Task ToTask(this WaitHandle waitHandle)
        {
            var tcs = new TaskCompletionSource<object>();

            // Registering callback to wait till WaitHandle changes its state

            ThreadPool.RegisterWaitForSingleObject(
                waitObject: waitHandle,
                callBack: (o, timeout) => { tcs.SetResult(null); },
                state: null,
                timeout: TimeSpan.MaxValue,
                executeOnlyOnce: true);

            return tcs.Task;
        }
        public static async Task<T> WithWaitCancellation<T>(this Task<T> task, CancellationToken cancellationToken)
        {
            var tcs = new TaskCompletionSource<bool>();

            // Register with the cancellation token.
            using (cancellationToken.Register(s => ((TaskCompletionSource<bool>)s).TrySetResult(true), tcs))
                if (task != await Task.WhenAny(task, tcs.Task))
                    throw new OperationCanceledException(cancellationToken);

            // Wait for one or the other to complete.
            return await task;
        }

        public static TValue GetOr<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue orValue)
        {
            if (dict.ContainsKey(key))
            {
                return dict[key];
            }
            return orValue;
        }

        public static Task WriteAsync(this Stream stream, byte[] array)
        {
            return stream.WriteAsync(array, 0, array.Count());
        }
        public static Task WriteAsync(this Stream stream, byte[] array, CancellationToken token)
        {
            return stream.WriteAsync(array, 0, array.Count(), token);
        }
        public static byte[] ReadUntil(this Stream stream, byte delimiter, bool includeDelimiter = true)
        {
            byte[] buffer = new byte[1024];
            int index = 0;

            while (true)
            {
                int cast;
                try
                {
                    cast = stream.ReadByte();
                }
                catch (Exception ex) when (ex is SocketException || ex is IOException)
                {
                    if (index == 0) return null;
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
                if (cast == -1)
                {
                    //end of stream
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
                byte b = (byte)cast;
                if (includeDelimiter || b != delimiter)
                {
                    if (index + 1 > buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }
                    buffer[index] = b;
                    index++;
                }
                if (b == delimiter)
                {
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
            }
        }

        public static async Task<int> FillBufferAsync(this Stream stream, byte[] buffer, int offset, int length, CancellationToken token)
        {
            int totalRead = 0;
            while (length > 0 && !token.IsCancellationRequested)
            {
                var read = await stream.ReadAsync(buffer, offset, length, token);
                if (read == 0)
                    return totalRead;
                offset += read;
                length -= read;
                totalRead += read;
            }
            
            return totalRead;
        }

        public static async Task<int> ReadByteAsync(this Stream stream)
        {
            byte[] buffer = new byte[1];
            int read = await stream.ReadAsync(buffer, 0, 1);
            if (read < 1)
                return -1;
            return buffer[0];
        }
        public static async Task<int> ReadByteAsync(this Stream stream, CancellationToken token)
        {
            byte[] buffer = new byte[1];
            int read = await stream.ReadAsync(buffer, 0, 1, token);
            if (read < 1)
                return -1;
            return buffer[0];
        }
        public static async Task<byte[]> ReadUntilAsync(this Stream stream, byte delimiter, CancellationToken token, bool includeDelimiter = true)
        {
            byte[] buffer = new byte[1024];
            int index = 0;

            while (true)
            {
                int cast;
                try
                {
                    cast = await stream.ReadByteAsync(token);
                }
                catch (Exception ex) when (ex is SocketException || ex is IOException)
                {
                    if (index == 0) return null;
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
                if (cast == -1)
                {
                    //end of stream
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
                byte b = (byte)cast;
                if (includeDelimiter || b != delimiter)
                {
                    if (index + 1 > buffer.Length)
                    {
                        Array.Resize(ref buffer, buffer.Length * 2);
                    }
                    buffer[index] = b;
                    index++;
                }
                if (b == delimiter)
                {
                    Array.Resize(ref buffer, index);
                    return buffer;
                }
            }
        }
    }
}

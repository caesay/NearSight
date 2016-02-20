using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CS;

namespace NearSight.Util
{
    public static class MPackReader
    {
        public static MPack ReadOne(Stream stream)
        {
            return MPack.ParseFromStream(stream);
        }

        public static Task<MPack> ReadOneAsync(Stream stream, CancellationToken token)
        {
            return MPack.ParseFromStreamAsync(stream, token);
        }

        public static Task CreateReadLoop(Stream stream, CancellationToken token, Action<MPack> recieved)
        {
            var loop = new Func<Task>(async () =>
            {
                while (!token.IsCancellationRequested)
                {
                    try
                    {
                        var parsed = await ReadOneAsync(stream, token);
                        recieved(parsed);
                    }
                    catch
                    {
                        // this only occurs if there is a read error or the data is invalid.
                        // break out of the loop to signal to listeners that the stream has ended.
                        break;
                    }
                }
            });
            return loop();
        }
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Caching;
using System.Text;
using System.Threading.Tasks;
using RT.Util;

namespace NearSight.Util
{
    public abstract class TrackedObject : IDisposable
    {
        public string Token { get; }

        private static Dictionary<string, TrackedObject> _objs = new Dictionary<string, TrackedObject>();
        private bool _disposed;

        protected TrackedObject()
        {
            Token = GenerateUniqueToken();
            _objs.Add(Token, this);
        }

        protected virtual string GenerateUniqueToken()
        {
            return Guid.NewGuid().ToString();
        }

        public virtual void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _objs.Remove(Token);
        }

        public static IEnumerable<TrackedObject> GetAllTrackedObjects()
        {
            return _objs.Values;
        }

        protected static T GetItem<T>(string token, bool throwIfInvalid)
            where T : TrackedObject
        {
            if (throwIfInvalid)
                return (T)_objs[token];

            if (_objs.ContainsKey(token))
            {
                return _objs[token] as T;
            }

            return null;
        }
    }

    public abstract class TrackedObject<T> : TrackedObject
        where T : TrackedObject<T>
    {
        public static T Get(string token)
        {
            return GetItem<T>(token, true);
        }
        public static T Get(string token, bool throwIfMissing)
        {
            return GetItem<T>(token, throwIfMissing);
        }

        public static IEnumerable<T> Get()
        {
            return GetAllTrackedObjects().Where(t => t.GetType() == typeof (T)).Select(t => (T) t);
        }
    }
}

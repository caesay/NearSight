using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Util
{
    internal class ListKvp<T, Y> : List<KeyValuePair<T, Y>>
    {
        public void Add(T key, Y value)
        {
            var element = new KeyValuePair<T, Y>(key, value);
            this.Add(element);
        }

        public IEnumerable<KeyValuePair<T, Y>> GetAllByKey(T key)
        {
            return this.Where(kvp => kvp.Key.Equals(key));
        }
        public void RemoveAllByKey(T key)
        {
            for (int i = this.Count - 1; i >= 0; i--)
            {
                if (this[i].Key.Equals(key))
                {
                    this.RemoveAt(i);
                }
            }
        }
    }
}

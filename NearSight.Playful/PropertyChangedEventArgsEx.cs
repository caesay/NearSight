using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Playful
{
    public class PropertyChangedEventArgsEx : EventArgs
    {
        public string PropertyName { get; set; }
        public object NewValue { get; set; }
        public PropertyChangedEventArgsEx()
        {
        }
        public PropertyChangedEventArgsEx(string propertyName, object newValue)
        {
            PropertyName = propertyName;
            NewValue = newValue;
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Playful
{
    public class RemoterTest : IRemoterTest
    {
        public string MyProperty
        {
            get { return _myPropertyBacking; }
            set
            {
                _myPropertyBacking = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgsEx(nameof(MyProperty), value));
            }
        }

        public event EventHandler<PropertyChangedEventArgsEx> PropertyChanged;
        private string _myPropertyBacking;

        public int Add(int one, int two)
        {
            MyProperty = (one + two).ToString();
            return one + two;
        }


        public string Reverse(string input)
        {
            throw new NotSupportedException();
        }

        public string RefTest(string addTo, ref string input, string ret)
        {
            input = input + addTo;
            return ret;
        }

        public void OutTest(out string input)
        {
            input = "HAHA";
        }

        public Stream GetRandomStream(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            var str = new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());

            return new MemoryStream(Encoding.UTF8.GetBytes(str));
        }

        public IRemoterValueTest GetInterface(string input)
        {
            return new InterfaceImpl(input);
        }
    }

    public class InterfaceImpl : IRemoterValueTest
    {
        private readonly string _str;

        public InterfaceImpl(string str)
        {
            _str = str;
        }

        public string Get()
        {
            return _str;
        }
    }

}

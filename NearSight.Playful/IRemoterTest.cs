using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NearSight.Playful
{
    [RContractProvider]
    public interface IRemoterTest
    {
        [RProperty]
        string MyProperty { get; set; }

        [REvent]
        event EventHandler<PropertyChangedEventArgsEx> PropertyChanged;

        [ROperation]
        int Add(int one, int two);


        [ROperation]
        string Reverse(string input);

        [ROperation]
        string RefTest(string addTo, ref string input, string ret);

        [ROperation]
        void OutTest(out string input);

        [ROperation]
        Stream GetRandomStream(int length);

        [ROperation]
        IRemoterValueTest GetInterface(string input);
    }

    [RContractProvider]
    public interface IRemoterValueTest
    {
        [ROperation]
        string Get();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CS.Sockets;

namespace NearSight.Playful
{
    class Program
    {
        static void Main(string[] args)
        {
            RemoterServer server = new RemoterServer(7750);
            server.PropagateExceptions = true;
            server.AddService<IRemoterTest, RemoterTest>("/path");
            server.Start();

            RemoterFactory factory = new RemoterFactory("tcp://localhost:7750");
            factory.Open();
            IRemoterTest proxy = factory.OpenServicePath<IRemoterTest>("/path");

            int addRes = proxy.Add(15, 10); // should be 25

            string outRes;
            proxy.OutTest(out outRes);

            string refRes = " it worked ";
            string refReturn = proxy.RefTest("ha", ref refRes, "return value");


            IRemoterValueTest valueTest = proxy.GetInterface("You got it back.");
            var valTestRes = valueTest.Get();

            Stream streamTest = proxy.GetRandomStream(100);
            streamTest.ReadByte();
            streamTest.ReadByte();

            long streamPos = streamTest.Position;

            try
            {
                var reverseRes = proxy.Reverse("hello");
            }
            catch { }

            server.Stop();
            long streamPos2 = streamTest.Position;
            Console.Read();
        }
    }
}

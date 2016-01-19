#NearSight
Just another RPC library. It does some important stuff that WCF can't do, and the messaging protocol is a very compact binary format (uses much less bandwidth than WCF)
___________________

##Notable Features:
0. Two-way streams: pass a stream object as a method parameter or as a return parameter.
0. CLR event propagation: server defines events, client can subscribe and get notified when they execute
0. Return services from services: You can return a decorated object as the return value of a method call from another service and the client can call methods on the returned object, too. (serviceception!)
0. Turn a synchronous server API call into an (truly) asynchronous client call.
0. Automatic SSL handling, just provide a x509 certificate.
0. Session persistence: If a client disconnects because of a network error, it can re-connect and resume a session (your code won't even know that the network issue happened)
0. <del>Built in authentication</del> (removed for api improvements. possibly re-introduce this in the future.)

______________

##Usage:

#### Define service interface and implementation:
```csharp
// define interface
[RContractProvider]
public interface IRemoterTest
{
    [RProperty]
    string MyProperty { get; set; }

    [REvent]
    event EventHandler<PropertyChangedEventArgsEx> PropertyChanged;

    [ROperation]
    int Add(int one, int two);
}

// implement interface
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
}
```

#### Run server and execute methods:
```csharp
// create a server
RemoterServer server = new RemoterServer(7750);
server.AddService<IRemoterTest, RemoterTest>("/path");
server.Start();

// create a proxy
RemoterFactory factory = new RemoterFactory("tcp://localhost:7750");
factory.Open();
IRemoterTest proxy = factory.OpenServicePath<IRemoterTest>("/path");

// call methods
int addRes = proxy.Add(15, 10); // should be 25
```
_______________

##License

Creative Commons Attribution-ShareAlike 4.0 International 
(CC BY-SA 4.0)
http://creativecommons.org/licenses/by-sa/4.0/

_______________

##Credits
0. Caelan (Me) [caelantsayler]at[gmail]com
1. Contributers of RT.Util: https://github.com/RT-Projects/RT.Util

Feel free to open an issue if you have questions or find a bug.


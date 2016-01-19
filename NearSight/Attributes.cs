using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace NearSight
{
    //[AttributeUsage(AttributeTargets.All)]
    //public class RRequireAuth : RemoterBaseAttribute
    //{
    //}

    //[AttributeUsage(AttributeTargets.All)]
    //public class RRequireRole : RemoterBaseAttribute
    //{
    //    public string RequiredRole { get; private set; }
    //    public RRequireRole(string role)
    //    {
    //        RequiredRole = role;
    //    }
    //}

    [AttributeUsage(AttributeTargets.Interface)]
    public class RContractProvider : RemoterBaseAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class ROperation : RemoterBaseAttribute
    {
    }
    [AttributeUsage(AttributeTargets.Event)]
    public class REvent: RemoterBaseAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class RProperty : RemoterBaseAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Parameter | AttributeTargets.ReturnValue)]
    public class RWritable : RemoterBaseAttribute
    {
    }

    public class RemoterBaseAttribute : Attribute
    {
    }
}

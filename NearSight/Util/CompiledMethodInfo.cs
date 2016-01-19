using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using NearSight.Util;

namespace NearSight.Util
{
    internal class CompiledMethodInfo
    {
        public string Name { get; private set; }
        public MethodInfo Method { get; private set; }
        public DynamicMethodDelegate Delegate { get; private set; }
        public Attribute[] Attributes { get; private set; }
        public ParameterInfo[] Parameters { get; private set; }
        public Type ReturnType { get; private set; }

        public CompiledMethodInfo(MethodInfo method, Attribute[] extraAttributes = null)
        {
            if(extraAttributes == null)
                extraAttributes = new Attribute[0];
            Method = method;
            Name = method.Name;
            Delegate = DynamicMethodFactory.Generate(method);
            Attributes = method.GetCustomAttributes().Concat(extraAttributes).ToArray();
            Parameters = method.GetParameters();
            ReturnType = method.ReturnType;
        }
    }
}

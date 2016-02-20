using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CS;
using NearSight.Util;
using RT.Util.ExtensionMethods;

namespace NearSight
{
    public class RemoteCallHelper
    {
        public static string GetMethodName(MethodInfo m)
        {
            return "{0}({1}) {2}".Fmt(
               m.Name,
               m.GetParameters().Select(p => TypeToString(p.ParameterType)).JoinString("; "),
               TypeToString(m.ReturnType));
        }
        public static ParsedMethodName ParseMethodName(string sig)
        {
            var paramStart = sig.IndexOf('(');
            var paramEnd = sig.IndexOf(')');
            var method = sig.Substring(0, paramStart);
            var argStrings = sig.Substring(paramStart, paramEnd - paramStart)
                .TrimStart('(')
                .TrimEnd(')')
                .Split(';')
                .Select(str => str.Trim())
                .Where(str=> !String.IsNullOrWhiteSpace(str))
                .ToArray();
            var argTypes = argStrings
                .Select(StringToType)
                .ToArray();

            for (int index = 0; index < argTypes.Length; index++)
            {
                var t = argTypes[index];
                if (t == null)
                    throw new ArgumentException($"Param #{index} ({argStrings[index]}) could not be parsed.");
            }

            var returnType = StringToType(sig.Substring(paramEnd + 1).Trim());
            return new ParsedMethodName(method, argTypes, returnType);
        }
        public static bool CheckMethodSignature(MethodInfo method, ParsedMethodName signature)
        {
            var serverArgTypes = method.GetParameters()
                    .Select(x => x.ParameterType)
                    .ToArray();
            var serverRetType = method.ReturnType;

            return serverRetType == signature.ReturnType && Enumerable.SequenceEqual(signature.ArgumentTypes, serverArgTypes);
        }
        public static bool CheckMethodSignature(MethodInfo method, string signature)
        {
            var parsed = ParseMethodName(signature);
            return CheckMethodSignature(method, parsed);
        }

        public static Type StringToType(string str)
        {
            int tcode;
            if (int.TryParse(str, out tcode))
            {
                return FromTypeCode((TypeCode)tcode);
            }
            return Type.GetType(str);
        }
        public static string TypeToString(Type t)
        {
            var tcode = (int)Type.GetTypeCode(t);
            if (tcode > 2)
                return tcode.ToString();

            if (IsSimpleType(t) || t.Module.ScopeName == "CommonLanguageRuntimeLibrary")
                return t.FullName;

            return t.AssemblyQualifiedName;
        }

        public static MPack ObjectToParamater(object o)
        {
            var type = o.GetType();
            if (type.IsByRef)
                type = type.GetElementType();
            if (type == typeof(byte[]))
                return MPack.From((byte[])o);
            var tcode = (int)Type.GetTypeCode(type);
            if (tcode > 2)
                return MPack.From(o);

            return ClassifyMPack.Serialize(type, o);
        }
        public static object ParamaterToObject(Type type, MPack param)
        {
            if (param.ValueType == MPackType.Binary && type == typeof(byte[]))
                return (byte[])param.Value;

            var tcode = (int)Type.GetTypeCode(type);
            if (tcode > 2)
                return param.To(type);

            if (type.IsByRef)
                type = type.GetElementType();

            return ClassifyMPack.Deserialize(type, param);
        }

        private static bool IsSimpleType(Type type)
        {
            return
                type.IsPrimitive ||
                new Type[] {
            typeof(Enum),
            typeof(String),
            typeof(Decimal),
            typeof(DateTime),
            typeof(DateTimeOffset),
            typeof(TimeSpan),
            typeof(Guid)
                }.Contains(type) ||
                Convert.GetTypeCode(type) != TypeCode.Object ||
                (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>) && IsSimpleType(type.GetGenericArguments()[0]))
                ;
        }
        private static Type FromTypeCode(TypeCode code)
        {
            switch (code)
            {
                case TypeCode.Boolean:
                    return typeof(bool);
                case TypeCode.Byte:
                    return typeof(byte);
                case TypeCode.Char:
                    return typeof(char);
                case TypeCode.DateTime:
                    return typeof(DateTime);
                case TypeCode.DBNull:
                    return typeof(DBNull);
                case TypeCode.Decimal:
                    return typeof(decimal);
                case TypeCode.Double:
                    return typeof(double);
                case TypeCode.Empty:
                    return null;
                case TypeCode.Int16:
                    return typeof(short);
                case TypeCode.Int32:
                    return typeof(int);
                case TypeCode.Int64:
                    return typeof(long);
                case TypeCode.Object:
                    return typeof(object);
                case TypeCode.SByte:
                    return typeof(sbyte);
                case TypeCode.Single:
                    return typeof(Single);
                case TypeCode.String:
                    return typeof(string);
                case TypeCode.UInt16:
                    return typeof(UInt16);
                case TypeCode.UInt32:
                    return typeof(UInt32);
                case TypeCode.UInt64:
                    return typeof(UInt64);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public class ParsedMethodName
        {
            public string Name { get; }
            public Type[] ArgumentTypes { get; }
            public Type ReturnType { get; }

            public ParsedMethodName(string name, Type[] argumentTypes, Type returnType)
            {
                Name = name;
                ArgumentTypes = argumentTypes;
                ReturnType = returnType;
            }
        }
    }
}

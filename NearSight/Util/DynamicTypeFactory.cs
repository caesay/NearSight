using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace NearSight.Util
{
    public static class DynamicTypeFactory
    {
        public delegate object MethodCallDelegate(MethodBase method, object[] parameters);

        private static AssemblyBuilder asmBuilder;
        private static ModuleBuilder modBuilder;
        private static Random random;
        static DynamicTypeFactory()
        {
            asmBuilder = Thread.GetDomain()
                .DefineDynamicAssembly(new AssemblyName("dynamic_type_factory"), AssemblyBuilderAccess.Run);
            modBuilder = asmBuilder.DefineDynamicModule("dynamic_type_factory_module");
            random = new Random();
        }
        public static Type Merge<T1, T2>()
        {
            return Merge(typeof(T1), typeof(T2));
        }
        public static Type Merge<T1, T2, T3>()
        {
            return Merge(typeof(T1), typeof(T2), typeof(T3));
        }
        public static Type Merge<T1, T2, T3, T4>()
        {
            return Merge(typeof(T1), typeof(T2), typeof(T3), typeof(T4));
        }
        public static Type Merge(params Type[] types)
        {
            if (!types.All(t => t.IsInterface))
                throw new ArgumentException("One or more provided types are not an interface.");

            var name = $"dynMerge({RandomString(4)})_" + String.Join("_", types.Select(t => t.Name));

            var typeBuilder = modBuilder.DefineType(
                name, TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);

            foreach (Type t in types)
                typeBuilder.AddInterfaceImplementation(t);

            return typeBuilder.CreateType();
        }

        public static T Generate<T>(MethodCallDelegate implementation)
        {
            return (T)Activator.CreateInstance(GenerateType(typeof(T)), BindingFlags.Default, null, new[] { implementation });

        }
        public static Type GenerateType(Type interfaceType)
        {
            throw new NotImplementedException("This is currently not working");
            var typeBuilder = modBuilder.DefineType($"dynImpl({RandomString(4)})_{interfaceType.Name}", TypeAttributes.Public,
                typeof(object), new Type[] { interfaceType });

            var implBuilder = typeBuilder.DefineField("_impl", typeof(MethodCallDelegate), FieldAttributes.Private);
            implBuilder.SetOffset(0);

            var ctor = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis,
                new[] { typeof(MethodCallDelegate) });
            var ctorIl = ctor.GetILGenerator();
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Stfld, implBuilder);
            ctorIl.Emit(OpCodes.Ret);

            //var methods = interfaceType.GetMethods();
            var methods = interfaceType.GetInterfaces().SelectMany(i => i.GetMethods()).Concat(interfaceType.GetMethods());
            foreach (var method in methods)
            {
                var methodBuilder = typeBuilder.DefineMethod(method.Name, MethodAttributes.Public,
                    method.ReturnType, method.GetParameters().Select(p => p.ParameterType).ToArray());
                typeBuilder.DefineMethodOverride(methodBuilder, method);
                var parameters = method.GetParameters();

                var il = methodBuilder.GetILGenerator();

                il.Emit(OpCodes.Ldfld, implBuilder);

                il.Emit(OpCodes.Call,
                    typeof(MethodBase).GetMethod("GetCurrentMethod", BindingFlags.Public | BindingFlags.Static));
                // (dele mth)
                il.Emit(OpCodes.Ldc_I4, parameters.Length);
                // (dele mth int)
                il.Emit(OpCodes.Newarr, typeof(object));
                // (dele mth arr)
                for (int i = 0; i < parameters.Length; i++)
                {
                    il.Emit(OpCodes.Dup);
                    // (dele mth arr, arr)
                    il.Emit(OpCodes.Ldc_I4, i);
                    // (dele mth arr, arr int)
                    il.Emit(OpCodes.Ldarg, i + 1);
                    if (parameters[i].ParameterType.IsValueType)
                        il.Emit(OpCodes.Box, parameters[i].ParameterType);
                    // (dele mth arr, arr int obj)
                    il.Emit(OpCodes.Stelem_Ref);
                    // (dele mth arr)
                }
                il.Emit(OpCodes.Callvirt, typeof(MethodCallDelegate).GetMethod("Invoke"));
                // (ret)
                il.Emit(OpCodes.Ret);
            }
            var type = typeBuilder.CreateType();
            return type;
        }

        private static string RandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            return new string(Enumerable.Repeat(chars, length)
              .Select(s => s[random.Next(s.Length)]).ToArray());
        }
    }
}

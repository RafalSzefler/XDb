using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using XDb.Abstractions;

namespace XDb.Core;

internal static class UpdateParametersDelegateBuilder
{
    private static readonly MethodInfo ParametersGetter
        = typeof(DbCommand)
            .GetProperty(nameof(DbCommand.Parameters))
            .GetGetMethod()
            ?? throw new ArgumentNullException(nameof(ParametersGetter));

    private static readonly MethodInfo AddParameter
        = typeof(DbParameterCollection)
            .GetMethod(nameof(DbParameterCollection.Add))
            ?? throw new ArgumentNullException(nameof(AddParameter));

    private static readonly MethodInfo CreateParameter
        = typeof(DbCommand)
            .GetMethod(nameof(DbCommand.CreateParameter))
            ?? throw new ArgumentNullException(nameof(CreateParameter));

    private static readonly MethodInfo SetParameterName
        = typeof(DbParameter)
            .GetProperty(nameof(DbParameter.ParameterName))
            .GetSetMethod()
            ?? throw new ArgumentNullException(nameof(SetParameterName));

    private static readonly MethodInfo SetParameterValue
        = typeof(DbParameter)
            .GetProperty(nameof(DbParameter.Value))
            .GetSetMethod()
            ?? throw new ArgumentNullException(nameof(SetParameterValue));

    public static UpdateParametersDelegate Build(Type parametersType, Dictionary<Type, IValueConverter> valueConverters)
    {
        var dynamicMethod = new DynamicMethod(
            $"{nameof(UpdateParametersDelegate)}_{parametersType.FullName}",
            null,
            new[] { typeof(IValueConverter[]), typeof(DbCommand), typeof(object) },
            typeof(UpdateParametersDelegateBuilder).Module,
            true);

        var il = dynamicMethod.GetILGenerator();

        var currentIndex = 0;
        var convertersMap = new Dictionary<IValueConverter, int>();

        var parametersVar = il.DeclareLocal(typeof(DbParameterCollection));

        // Load DbParameterCollection from DbCommand
        il.Emit(OpCodes.Ldarg_1);
        il.EmitCall(OpCodes.Callvirt, ParametersGetter, null);
        il.Emit(OpCodes.Stloc, parametersVar);

        foreach (var property in parametersType.GetProperties())
        {
            var getter = property.GetGetMethod();
            if (getter == null)
            {
                continue;
            }

            il.Emit(OpCodes.Ldloc, parametersVar);

            // Create new DbParameter.
            il.Emit(OpCodes.Ldarg_1);
            il.EmitCall(OpCodes.Callvirt, CreateParameter, null);

            // Duplicate DbParameter for later SetParameterValue call.
            il.Emit(OpCodes.Dup);

            // Duplicate DbParameter for SetParameterName call.
            il.Emit(OpCodes.Dup);

            // Set ParameterName from this property's name.
            il.Emit(OpCodes.Ldstr, property.Name);
            il.EmitCall(OpCodes.Callvirt, SetParameterName, null);

            MethodInfo? convertMethod = null;

            if (valueConverters.TryGetValue(property.PropertyType, out var converter))
            {
                int converterIndex;

                if (!convertersMap.TryGetValue(converter, out converterIndex))
                {
                    converterIndex = currentIndex;
                    currentIndex++;
                    convertersMap[converter] = converterIndex;
                }

                convertMethod = GetConvertMethod(property.PropertyType, converter);

                // We have a converter that is stored in "this", which is an
                // array of IValueConverter[]. So load the converter at correct
                // index.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, converterIndex);
                il.Emit(OpCodes.Ldelem_Ref);
            }

            // Call this property's getter on our parameters object.
            il.Emit(OpCodes.Ldarg_2);
            il.EmitCall(OpCodes.Callvirt, getter, null);

            var realType = property.PropertyType;

            if (convertMethod != null)
            {
                // We have converter so now convert the retrieved property
                // to correct type.
                il.EmitCall(OpCodes.Callvirt, convertMethod, null);
                realType = convertMethod.ReturnType;
            }

            if (realType.IsValueType)
            {
                // Since DbParameter.Value property is of type object,
                // we have to box the value when needed.
                il.Emit(OpCodes.Box, realType);
            }

            // Finally SetParameterValue...
            il.EmitCall(OpCodes.Callvirt, SetParameterValue, null);

            // ...and add the result to collection.
            il.EmitCall(OpCodes.Callvirt, AddParameter, null);

            // We finally Pop, because DbParametersCollection.Add returns
            // an int that we are not going to use.
            il.Emit(OpCodes.Pop);
        }

        il.Emit(OpCodes.Ret);

        IValueConverter[] thisObject;

        if (convertersMap.Count == 0)
        {
            thisObject = Array.Empty<IValueConverter>();
        }
        else
        {
            thisObject = convertersMap
                .OrderBy(x => x.Value)
                .Select(x => x.Key)
                .ToArray();
        }

        return (UpdateParametersDelegate)dynamicMethod.CreateDelegate(typeof(UpdateParametersDelegate), thisObject);
    }

    private static MethodInfo GetConvertMethod(Type fromType, IValueConverter converter)
    {
        Type? concreteInterface = null;

        foreach (var @interface in converter.GetType().GetInterfaces())
        {
            if (!(@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValueConverter<,>)))
            {
                continue;
            }

            var genericArgs = @interface.GetGenericArguments();
            if (genericArgs[0] == fromType)
            {
                concreteInterface = @interface;
                break;
            }
        }

        if (concreteInterface == null)
        {
            throw new ArgumentException($"Missing {nameof(concreteInterface)}.");
        }

        return concreteInterface
            .GetMethod(nameof(IValueConverter<int, int>.ForwardConvert));
    }
}

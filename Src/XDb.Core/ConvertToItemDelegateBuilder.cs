using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using XDb.Abstractions;

namespace XDb.Core;

internal static class ConvertToItemDelgateBuilder
{
    private static int GetColumnOrder(Dictionary<string, DbColumn> columns, string columnName)
    {
        if (columns.TryGetValue(columnName, out var dbColumn))
        {
            return dbColumn.ColumnOrdinal ?? -1;
        }

        throw new ColumnNotFoundException($"Column [{columnName}] not present in the result set.");
    }

    private static readonly MethodInfo GetColumnOrderMethod
        = typeof(ConvertToItemDelgateBuilder)
            .GetMethod(nameof(ConvertToItemDelgateBuilder.GetColumnOrder), BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new ArgumentNullException(nameof(GetColumnOrderMethod));

    private static readonly Dictionary<Type, MethodInfo> ConcreteTypeRetrieverMap = BuildConcreteTypeRetrieverMap();

    private static Dictionary<Type, MethodInfo> BuildConcreteTypeRetrieverMap()
    {
        var result = new Dictionary<Type, MethodInfo>()
        {
            { typeof(bool), GetMethod(nameof(DbDataReader.GetBoolean)) },
            { typeof(short), GetMethod(nameof(DbDataReader.GetInt16)) },
            { typeof(int), GetMethod(nameof(DbDataReader.GetInt32)) },
            { typeof(long), GetMethod(nameof(DbDataReader.GetInt64)) },
            { typeof(Guid), GetMethod(nameof(DbDataReader.GetGuid)) },
            { typeof(float), GetMethod(nameof(DbDataReader.GetFloat)) },
            { typeof(double), GetMethod(nameof(DbDataReader.GetDouble)) },
            { typeof(decimal), GetMethod(nameof(DbDataReader.GetDecimal)) },
            { typeof(DateTime), GetMethod(nameof(DbDataReader.GetDateTime)) },
            { typeof(byte), GetMethod(nameof(DbDataReader.GetByte)) },
            { typeof(char), GetMethod(nameof(DbDataReader.GetChar)) },
            { typeof(string), GetMethod(nameof(DbDataReader.GetString)) },
        };
        result.TrimExcess();
        return result;
    }

    public static ConvertToItemDelegate<T> Build<T>(
        Dictionary<Type, IValueConverter> valueConverters,
        INamePolicy namePolicy)
    {
        var itemType = typeof(T);
        var ctrs = itemType.GetConstructors();
        if (ctrs.Length == 0)
        {
            throw new ConfigurationException($"No public constructor on {itemType}.");
        }

        if (ctrs.Length > 1)
        {
            throw new ConfigurationException($"{itemType} has more than 1 public constructor.");
        }

        var ctr = ctrs[0];
        var ctrParams = ctr.GetParameters();

        if (ctrParams.Length == 0)
        {
            throw new ConfigurationException($"{itemType} is required to have a non-default constructor.");
        }

        var currentIndex = 0;
        var convertersMap = new Dictionary<IValueConverter, int>();

        var dynamicMethod = new DynamicMethod(
            $"{nameof(ConvertToItemDelegate<T>)}_{itemType}",
            itemType,
            new[] { typeof(IValueConverter[]), typeof(DbDataReader), typeof(Dictionary<string, DbColumn>) },
            typeof(ConvertToItemDelgateBuilder).Module,
            true);

        var il = dynamicMethod.GetILGenerator();

        var concreteTypeRetrieverMap = ConcreteTypeRetrieverMap;
        var getValueMethod = GetMethod(nameof(DbDataReader.GetValue));

        foreach (var ctrParam in ctrParams)
        {
            MethodInfo? convertMethod = null;
            if (valueConverters.TryGetValue(ctrParam.ParameterType, out var converter))
            {
                int converterIndex;

                if (!convertersMap.TryGetValue(converter, out converterIndex))
                {
                    converterIndex = currentIndex;
                    currentIndex++;
                    convertersMap[converter] = converterIndex;
                }

                convertMethod = GetConvertMethod(ctrParam.ParameterType, converter);

                // We have a converter stored in "this" array of type IValueConverter[].
                // Load appropriate converter at correct index.
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Ldc_I4, converterIndex);
                il.Emit(OpCodes.Ldelem_Ref);
            }

            il.Emit(OpCodes.Ldarg_1);

            // Calculate ordinal in the result schema for given property. Meaning,
            // the result is a sequence of values. The information on what value is
            // stored at what index is inside our schema dictionary.
            il.Emit(OpCodes.Ldarg_2);
            il.Emit(OpCodes.Ldstr, namePolicy.Convert(ctrParam.Name));
            il.EmitCall(OpCodes.Call, GetColumnOrderMethod, null);

            var realParamType = ctrParam.ParameterType;
            if (convertMethod != null)
            {
                realParamType = convertMethod.GetParameters()[0].ParameterType;
            }

            if (concreteTypeRetrieverMap.TryGetValue(realParamType, out var concreteTypeRetriever))
            {
                // If we have concrete type retriever then use it. The default DbDataReader.GetValue()
                // may do unnecessary boxing.
                il.EmitCall(OpCodes.Callvirt, concreteTypeRetriever, null);
            }
            else
            {
                // If we don't have concrete retriever, then DbDataReader.GetValue() should do.
                // We only have to remember to unbox the value in case it is value type.
                il.EmitCall(OpCodes.Callvirt, getValueMethod, null);
                var convOpcode = realParamType.IsValueType ? OpCodes.Unbox_Any : OpCodes.Castclass;
                il.Emit(convOpcode, realParamType);
            }

            if (convertMethod != null)
            {
                // Finally if we have converter then call it.
                il.EmitCall(OpCodes.Callvirt, convertMethod, null);
            }
        }

        // We've retrieved values and stored them on stack in the order
        // expected by the constructor. So call the constructor and return
        // new instance.
        il.Emit(OpCodes.Newobj, ctr);
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

        return (ConvertToItemDelegate<T>)dynamicMethod.CreateDelegate(typeof(ConvertToItemDelegate<T>), thisObject);
    }

    private static MethodInfo GetMethod(string name)
    {
        var method = typeof(DbDataReader).GetMethod(name);
        if (method == null)
        {
            throw new ArgumentNullException(name);
        }

        return method;
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
            .GetMethod(nameof(IValueConverter<int, int>.BackwardConvert));
    }
}

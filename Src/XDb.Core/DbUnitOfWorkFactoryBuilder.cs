using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Threading;
using System.Threading.Tasks;
using XDb.Abstractions;

namespace XDb.Core;

public sealed class DbUnitOfWorkFactoryBuilder
{
    private readonly Dictionary<Type, IValueConverter> _forwardValueConverters
        = new Dictionary<Type, IValueConverter>();

    private DbConnection? _connection;
    private INamePolicy? _namePolicy;

    public DbUnitOfWorkFactoryBuilder SetConnection(DbConnection connection)
    {
        if (connection == null)
        {
            throw new ArgumentNullException(nameof(connection));
        }

        _connection = connection;
        return this;
    }

    public DbUnitOfWorkFactoryBuilder SetNamePolicy(INamePolicy policy)
    {
        if (policy == null)
        {
            throw new ArgumentNullException(nameof(policy));
        }

        _namePolicy = policy;
        return this;
    }

    private static List<Type> GetGenericValueConverterTypes(Type valueConverterType)
    {
        var result = new List<Type>(4);
        foreach (var @interface in valueConverterType.GetInterfaces())
        {
            if (@interface.IsGenericType && @interface.GetGenericTypeDefinition() == typeof(IValueConverter<,>))
            {
                result.Add(@interface);
            }
        }

        return result;
    }

    public DbUnitOfWorkFactoryBuilder AddValueConverters(IReadOnlyCollection<IValueConverter> valueConverters)
    {
        if (valueConverters == null)
        {
            throw new ArgumentNullException(nameof(valueConverters));
        }

        foreach (var valueConverter in valueConverters)
        {
            if (valueConverter == null)
            {
                throw new ArgumentException($"{nameof(valueConverters)} contains null item.");
            }

            var interfaces = GetGenericValueConverterTypes(valueConverter.GetType());
            if (interfaces.Count == 0)
            {
                throw new ConfigurationException($"Instances of {typeof(IValueConverter)} actually have to implement {typeof(IValueConverter<,>)}.");
            }

            foreach (var @interface in interfaces)
            {
                var fromType = @interface.GetGenericArguments()[0];

                if (_forwardValueConverters.ContainsKey(fromType))
                {
                    throw new ConfigurationException($"Forward value converter for type {fromType} already registered.");
                }

                _forwardValueConverters[fromType] = valueConverter;
            }
        }

        return this;
    }

    public async Task<IDbUnitOfWorkFactory> Build(CancellationToken ct = default)
    {
        if (_connection == null)
        {
            throw new ConfigurationException($"{nameof(SetConnection)} was not called.");
        }

        await _connection.OpenAsync(ct).ConfigureAwait(false);
        var namePolicy = _namePolicy ?? new PostgresNamePolicy();
        return new DbUnitOfWorkFactory(_connection, _forwardValueConverters, namePolicy);
    }
}

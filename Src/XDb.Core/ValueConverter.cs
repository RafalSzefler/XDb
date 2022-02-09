using System;
using XDb.Abstractions;

namespace XDb.Core;

public sealed class ValueConverter<TFrom, TTo> : IValueConverter<TFrom, TTo>
{
    private readonly Func<TFrom, TTo> _forward;
    private readonly Func<TTo, TFrom> _backward;

    public ValueConverter(Func<TFrom, TTo> forward, Func<TTo, TFrom> backward)
    {
        if (forward == null)
        {
            throw new ArgumentNullException(nameof(forward));
        }

        if (backward == null)
        {
            throw new ArgumentNullException(nameof(backward));
        }

        _forward = forward;
        _backward = backward;
    }

    public TTo ForwardConvert(TFrom item) => _forward(item);

    public TFrom BackwardConvert(TTo item) => _backward(item);
}

public static class ValueConverter
{
    public static ValueConverter<TFrom, TTo> Create<TFrom, TTo>(Func<TFrom, TTo> forward, Func<TTo, TFrom> backward)
        => new ValueConverter<TFrom, TTo>(forward, backward);
}

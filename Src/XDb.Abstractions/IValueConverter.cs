namespace XDb.Abstractions;

public interface IValueConverter
{ }

public interface IValueConverter<TFrom, TTo> : IValueConverter
{
    TTo ForwardConvert(TFrom item);

    TFrom BackwardConvert(TTo item);
}

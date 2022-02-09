using System;

namespace XDb.Abstractions;

public abstract class XDbException : Exception
{
    protected XDbException(string? message = null, Exception? inner = null)
        : base(message, inner)
    { }
}

public sealed class ConfigurationException : XDbException
{
    public ConfigurationException(string message)
        : base(message)
    { }
}

public sealed class NoResultException : XDbException
{
    public NoResultException(string message)
        : base(message)
    { }
}

public sealed class MultipleResultsException : XDbException
{
    public MultipleResultsException(string message)
        : base(message)
    { }
}

public sealed class ColumnNotFoundException : XDbException
{
    public ColumnNotFoundException(string message)
        : base(message)
    { }
}

using System;
using XDb.Abstractions;

namespace XDb.Core;

internal sealed class PostgresNamePolicy : INamePolicy
{
    public string Convert(string text)
    {
        Span<char> data = stackalloc char[2 * text.Length];

        data[0] = char.ToLower(text[0]);
        var index = 1;

        for (var i = 1; i < text.Length; i++)
        {
            var current = text[i];
            var currentIsUpper = char.IsUpper(current);
            var prev = text[i - 1];
            var prevIsUpper = char.IsUpper(prev);

            if (currentIsUpper && !prevIsUpper)
            {
                data[index] = '_';
                index++;
            }

            data[index] = char.ToLower(current);
            index++;
        }

        return new string(data.Slice(0, index));
    }
}

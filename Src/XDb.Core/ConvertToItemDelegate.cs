using System.Collections.Generic;
using System.Data.Common;

namespace XDb.Core;

internal delegate T ConvertToItemDelegate<T>(DbDataReader reader, Dictionary<string, DbColumn> schema);

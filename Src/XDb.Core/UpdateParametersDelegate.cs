using System.Data.Common;

namespace XDb.Core;

internal delegate void UpdateParametersDelegate(DbCommand command, object parameters);

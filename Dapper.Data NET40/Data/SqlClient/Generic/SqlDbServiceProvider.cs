using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Dapper.Data.SqlClient.Generic
{
    public abstract class SqlDbServiceProvider<T> : SqlDbServiceProvider where T : Service.IDbServiceProvider
    {
        protected SqlDbServiceProvider(string connectionString)
            : base(connectionString)
        { }

    }
}

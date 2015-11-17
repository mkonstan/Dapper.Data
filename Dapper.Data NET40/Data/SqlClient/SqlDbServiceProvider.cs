using Dapper.Data.Service;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dapper.Data.SqlClient
{
    public class SqlDbServiceProvider : DbServiceProvider
    {
        protected SqlDbServiceProvider(string connectionString)
            : base(new SqlConnectionFactory(connectionString))
        { }
    }
}

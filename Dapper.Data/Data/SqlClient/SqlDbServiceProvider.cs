using Dapper.Data.Service;

namespace Dapper.Data.SqlClient
{
    public class SqlDbServiceProvider : DbServiceProvider
    {
        protected SqlDbServiceProvider(string connectionString)
            : base(new SqlConnectionFactory(connectionString))
        { }
    }
}

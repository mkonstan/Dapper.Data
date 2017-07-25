using System.Data.Common;

namespace Dapper.Data.SqlClient
{
    public class SqlDbContext : DbContext
    {
        public SqlDbContext(string connectionString)
            : base(new SqlConnectionFactory(connectionString))
        { }

        public SqlDbContext(SqlConnectionFactory connectionFactory)
            : base(connectionFactory)
        { }

        protected SqlDbContext(IDbConnectionFactory connectionFactory)
            : base(connectionFactory)
        { }
    }
}

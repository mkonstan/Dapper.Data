using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text;

namespace Dapper.Data.SqlClient
{
	class SqlDbContext : DbContext
	{
		public SqlDbContext(string serverName, string databaseName)
			: base(new SqlConnectionFactory(serverName, databaseName))
		{ }

		public SqlDbContext(string serverName, string databaseName, string userId, string password)
			: base(new SqlConnectionFactory(serverName, databaseName, userId, password))
		{ }
	}
}

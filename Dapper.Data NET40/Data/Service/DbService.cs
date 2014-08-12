
using System;
using Dapper.Data.Generic.Service;

namespace Dapper.Data.Service
{
	public interface IDbService
	{
	}

	public abstract class DbService : IDbService
	{
		protected DbService(IDbServiceProvider<IDbServiceProviderSession> db)
		{
			Db = db;
		}

		protected IDbServiceProvider<IDbServiceProviderSession> Db { get; private set; }
	}
}

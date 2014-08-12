
using System;
using Dapper.Data.Generic;
using Dapper.Data.Service;

namespace Dapper.Data.Generic.Service
{
	public interface IDbService<out TSession> : IDbService where TSession : ISession
	{
		IDbContext<TSession> Db { get; }
	}

	public abstract class DbService<TSession>: IDbService<TSession> where TSession : ISession
	{
		public IDbContext<TSession> Db
		{ get; private set; }

		protected DbService(IDbContext<TSession> db)
		{ Db = db; }
	}
}

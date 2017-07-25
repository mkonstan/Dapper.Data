
using System;

namespace Dapper.Data.Service
{
    public interface IDbServiceContract : IDbService
    {
        Type Contract { get; }
    }

    public interface IDbService
    {
        IDbContext Db { get; }
    }

    public abstract class DbService : IDbService
    {
        public IDbContext Db
        {
            get;
        }

        protected DbService(IDbContext db)
        { Db = db; }
    }
}

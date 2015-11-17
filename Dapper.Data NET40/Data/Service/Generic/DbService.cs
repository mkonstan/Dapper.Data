using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dapper.Data.Service.Generic
{
	public abstract class DbService<TContract> : DbService, IDbServiceContract where TContract : IDbService
    {
        protected DbService(IDbContext db)
            :base(db)
        { }

        public virtual Type Contract { get { return typeof(TContract); } }
    }
}

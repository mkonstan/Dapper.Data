// ReSharper disable once CheckNamespace
namespace System.Data.Common
{
    public interface IDbConnectionFactory
    {
        IDbConnection Create();
        IDbConnection CreateAndOpen();
    }

    /// <summary>
    /// Default implementation of ConnectionFactory
    /// used by DbContext
    /// </summary>
    public class DbConnectionFactory : IDbConnectionFactory
    {
        private readonly Func<IDbConnection> _body;
        /// <summary>
        /// connectionStringName: 
        ///		connection string name defined under connections section of the config file.
        /// The connection element must have propper DbProviderFactory defined
        /// </summary>
        public DbConnectionFactory(Func<IDbConnection>  body)
        {
            _body = body;
        }

        public virtual IDbConnection Create()
        {
            return _body();
        }

        public virtual IDbConnection CreateAndOpen()
        {
            var con = Create();
            con.Open();
            return con;
        }
    }
}
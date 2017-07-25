using System;
using System.Collections.Generic;
using System.Linq;
using System.Data.Common;
using System.Data;

namespace Dapper.Data
{
    public interface IDbCommand
    {
        int Execute(
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        IEnumerable<T> Query<T>(
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        IEnumerable<dynamic> Query(
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        IEnumerable<dynamic> Query(
            Type type,
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        T Single<T>(
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        dynamic Single(
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );

        dynamic Single(
            Type type,
            string sql,
            object param = null,
            CommandType? commandType = null,
            int? commandTimeout = 0
        );
    }
    /// <summary>
    /// Default behavior exposed by DbContext helps with injection
    /// </summary>
    public interface IDbContext : IDbCommand
    {
        void Batch(Action<ISession> action);
        TResult Batch<TResult>(Func<ISession, TResult> func);
    }

    /// <summary>
    /// Interface to help with transaction managment
    /// </summary>
    public interface ISession : IDbCommand
    {
        void BeginTransaction();
        void BeginTransaction(IsolationLevel il);
        void CommitTransaction();
        void RollbackTransaction();

        IDbConnection Connection { get; }

        int Execute(CommandDefinition definition);

        IEnumerable<T> Query<T>(CommandDefinition definition);

        IEnumerable<TReturn> Query<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0);
        IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TReturn>(string sql, Func<TFirst, TSecond, TThird, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0);
        IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0);
        IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0);

        SqlMapper.GridReader QueryMultiple(CommandDefinition command);
        SqlMapper.GridReader QueryMultiple(string sql, dynamic param = null, CommandType? commandType = null, int? commandTimeout = 0);
    }

    /// <summary>
    /// Light weight DbContext implementation based on dapper
    /// Use it to create your own DbContext
    /// It will help manage connection life time and transactions
    /// </summary>
    public abstract class DbContext : IDbContext
    {
        protected DbContext(IDbConnectionFactory connectionFactory)
        {
            ConnectionFactory = connectionFactory;
        }

        public virtual IDbConnectionFactory ConnectionFactory
        {
            get;
        }

        /// <summary>
        /// Enables execution of multiple statements and helps with
        /// transaction management
        /// </summary>
        public virtual void Batch(Action<ISession> action)
        {
            using (var con = ConnectionFactory.CreateAndOpen())
            {
                try
                {
                    action(new Session(con));
                }
                finally
                {
                    con.Close();
                }
            }
        }

        /// <summary>
        /// Enables execution of multiple statements and helps with
        /// transaction management
        /// </summary>
        public virtual TResult Batch<TResult>(Func<ISession, TResult> func)
        {
            using (var con = ConnectionFactory.CreateAndOpen())
            {
                try
                {
                    return func(new Session(con));
                }
                finally
                {
                    con.Close();
                }
            }
        }

        class Session : ISession
        {
            readonly IDbConnection _connection;
            IDbTransaction _transaction;

            public Session(IDbConnection connection)
            {
                _connection = connection;
                _transaction = null;
            }

            public void BeginTransaction()
            {
                if (_transaction == null)
                { _transaction = _connection.BeginTransaction(); }
            }

            public void BeginTransaction(IsolationLevel il)
            {
                if (_transaction == null)
                { _transaction = _connection.BeginTransaction(il); }
            }

            public void CommitTransaction()
            {
                _transaction?.Commit();
                _transaction = null;
            }

            public void RollbackTransaction()
            {
                _transaction?.Rollback();
                _transaction = null;
            }

            public IDbConnection Connection => _connection;

            public int Execute(CommandDefinition command)
            {
                return _connection.Execute(command);
            }

            public IEnumerable<T> Query<T>(CommandDefinition command)
            {
                return _connection.Query<T>(command);
            }

            public IEnumerable<TReturn> Query<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
            }

            public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TReturn>(string sql, Func<TFirst, TSecond, TThird, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
            }

            public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
            }

            public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
            }

            public int Execute(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Execute(sql, param, _transaction, commandTimeout, commandType);
            }

            public IEnumerable<T> Query<T>(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
            {
                if (typeof(T) == typeof(IDictionary<string, object>))
                {
                    return _connection.Query(sql, param, _transaction, true, commandTimeout, commandType).OfType<T>();
                }
                return _connection.Query<T>(sql, param, _transaction, true, commandTimeout, commandType);
            }

            public IEnumerable<dynamic> Query(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(sql, param, null, true, commandTimeout, commandType);
            }

            public IEnumerable<dynamic> Query(Type type, string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.Query(type, sql, param, _transaction, true, commandTimeout, commandType);
            }

            public SqlMapper.GridReader QueryMultiple(CommandDefinition command)
            {
                return _connection.QueryMultiple(command);
            }

            public SqlMapper.GridReader QueryMultiple(string sql, dynamic param = null, CommandType? commandType = null, int? commandTimeout = 0)
            {
                return _connection.QueryMultiple(new CommandDefinition(sql, param, _transaction, commandTimeout, commandType));
            }

            public T Single<T>(string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
            {
                return Query<T>(sql, param, commandType, commandTimeout).SingleOrDefault();
            }

            public dynamic Single(string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
            {
                return Query(sql, param, commandType, commandTimeout).SingleOrDefault();
            }

            public dynamic Single(Type type, string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
            {
                return Query(type, sql, param, commandType, commandTimeout).SingleOrDefault();
            }
        }

        public int Execute(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
        {
            return Batch(s => s.Execute(sql, param, commandType, commandTimeout));
        }

        public IEnumerable<T> Query<T>(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
        {
            return Batch(s => s.Query<T>(sql, param, commandType, commandTimeout));
        }

        public IEnumerable<dynamic> Query(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
        {
            return Batch(s => s.Query(sql, param, commandType, commandTimeout));
        }

        public IEnumerable<object> Query(Type type, string sql, object param = null, CommandType? commandType = null, int? commandTimeout = 0)
        {
            return Batch(s => s.Query(type, sql, param, commandType, commandTimeout));
        }

        public T Single<T>(string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
        {
            return Batch(s => s.Single<T>(sql, param, commandType, commandTimeout));
        }

        public dynamic Single(string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
        {
            return Batch(s => s.Single(sql, param, commandType, commandTimeout));
        }

        public dynamic Single(Type type, string sql, object param = null, CommandType? commandType = default(CommandType?), int? commandTimeout = 0)
        {
            return Batch(s => s.Single(type, sql, param, commandType, commandTimeout));
        }
    }
}

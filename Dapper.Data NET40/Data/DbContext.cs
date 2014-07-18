using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
			int? commandTimeout = null
        );

        IEnumerable<T> Query<T>(
            string sql,
            object param = null,
            CommandType? commandType = null,
			int? commandTimeout = null
        );

		IEnumerable<dynamic> Query(
            string sql,
            object param = null,
            CommandType? commandType = null,
			int? commandTimeout = null
		);

    	IEnumerable<dynamic> Query(
			System.Type type,
			string sql,
			object param = null,
			System.Data.CommandType? commandType = null,
			int? commandTimeout = null
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

		IEnumerable<TReturn> Query<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null);
		IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TReturn>(string sql, Func<TFirst, TSecond, TThird, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null);
		IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null);
		IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null);

		SqlMapper.GridReader QueryMultiple(CommandDefinition command);
		SqlMapper.GridReader QueryMultiple(string sql, dynamic param = null, System.Data.CommandType? commandType = null, int? commandTimeout = null);
    }

	/// <summary>
	/// Light weight DbContext implementation based on dapper
	/// Use it to create your own DbContext
	/// It will help manage connection life time and transactions
	/// </summary>
	public abstract class DbContext : IDbContext
	{
		protected DbContext(string connectionName)
			: this(new DbConnectionFactory(connectionName))
		{}

		protected DbContext(IDbConnectionFactory connectionFactory)
		{
			ConnectionFactory = connectionFactory;
		}

		public virtual IDbConnectionFactory ConnectionFactory
		{
			get;
			private set;
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
				if (_transaction != null)
				{
					_transaction.Commit();
				}
				_transaction = null;
			}

			public void RollbackTransaction()
			{
				if (_transaction != null)
				{
					_transaction.Rollback();
				}
				_transaction = null;
			}

            public IDbConnection Connection { get { return _connection; } }

			public int Execute(CommandDefinition command)
			{
				return _connection.Execute(command);
			}

			public IEnumerable<T> Query<T>(Dapper.CommandDefinition command)
			{
				return _connection.Query<T>(command);
			}

			public IEnumerable<TReturn> Query<TFirst, TSecond, TReturn>(string sql, Func<TFirst, TSecond, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null)
		    {
				return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
		    }

			public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TReturn>(string sql, Func<TFirst, TSecond, TThird, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null)
		    {
				return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
		    }

			public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null)
		    {
				return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
		    }

			public IEnumerable<TReturn> Query<TFirst, TSecond, TThird, TFourth, TFifth, TReturn>(string sql, Func<TFirst, TSecond, TThird, TFourth, TFifth, TReturn> map, object param = null, bool buffered = true, string splitOn = "Id", CommandType? commandType = null, int? commandTimeout = null)
		    {
                return _connection.Query(sql, map, param, _transaction, buffered, splitOn, commandTimeout, commandType);
		    }

			public int Execute(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
			{
				return _connection.Execute(sql, param, _transaction, commandTimeout,  commandType);
			}

			public IEnumerable<T> Query<T>(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
			{
				if (typeof(T) == typeof(IDictionary<string, object>))
				{
					return _connection.Query(sql, param, _transaction, true, commandTimeout, commandType).OfType<T>();
				}
				return _connection.Query<T>(sql, param, _transaction, true, commandTimeout, commandType);
			}

			public IEnumerable<dynamic> Query(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
			{
				return _connection.Query(sql, param, null, true, commandTimeout, commandType);
			}

			public IEnumerable<dynamic> Query(Type type, string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
			{
				return _connection.Query(type, sql, param, _transaction, true, commandTimeout, commandType);
			}

			public Dapper.SqlMapper.GridReader QueryMultiple(Dapper.CommandDefinition command)
			{
				return _connection.QueryMultiple(command);
			}

			public Dapper.SqlMapper.GridReader QueryMultiple(string sql, dynamic param = null, System.Data.CommandType? commandType = null, int? commandTimeout = null)
			{
				return _connection.QueryMultiple(new CommandDefinition(sql, param, _transaction, commandTimeout, commandType));
			}

		}

		public int Execute(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
		{
			return Batch(s => s.Execute(sql, param, commandType, commandTimeout));
		}

		public IEnumerable<T> Query<T>(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
		{
			return Batch(s => s.Query<T>(sql, param, commandType, commandTimeout));
		}

		public IEnumerable<dynamic> Query(string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
		{
			return Batch(s => s.Query(sql, param, commandType, commandTimeout));
		}

		public IEnumerable<object> Query(Type type, string sql, object param = null, CommandType? commandType = null, int? commandTimeout = null)
		{
			return Batch(s => s.Query(type, sql, param, commandType, commandTimeout));
		}
	}
}

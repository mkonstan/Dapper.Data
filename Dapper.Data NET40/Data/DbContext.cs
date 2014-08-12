using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Data.Common;
using System.Data;
using Dapper.Data.Generic;

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
			System.Type type,
			string sql,
			object param = null,
			System.Data.CommandType? commandType = null,
			int? commandTimeout = 0
		);
    }

	/// <summary>
	/// Interface to help with transaction managment
	/// </summary>
	public interface ISession : IDbCommand, IDisposable
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
		SqlMapper.GridReader QueryMultiple(string sql, dynamic param = null, System.Data.CommandType? commandType = null, int? commandTimeout = 0);
    }

	/// <summary>
	/// Default behavior exposed by DbContext helps with injection
	/// </summary>
	public interface IDbContext : IDbContext<ISession>
	{ }


	/// <summary>
	/// Default Session implementation
	/// </summary>
	class Session : ISession
	{
		IDbConnection _connection;
		IDbTransaction _transaction;

		public Session(IDbConnectionFactory connectionFactory)
			: this(connectionFactory.CreateAndOpen())
		{ }

		private Session(IDbConnection connection)
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
			return typeof(T) == typeof(IDictionary<string, object>) ? _connection.Query(sql, param, _transaction, true, commandTimeout, commandType).OfType<T>() : _connection.Query<T>(sql, param, _transaction, true, commandTimeout, commandType);
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

		[EditorBrowsable(EditorBrowsableState.Never)]
		void IDisposable.Dispose()
		{
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing)
		{
			if (!disposing)
				return;
			try
			{
				if (_transaction == null)
				{ return; }
				_transaction.Rollback();
				_transaction.Dispose();
				_transaction = null;
			}
			catch (Exception ex)
			{
				Trace.TraceError(ex.ToString());
			}
			finally
			{
				if (_connection != null)
				{
					_connection.Close();
					_connection.Dispose();
					_connection = null;
				}
			}
		}
	}

	/// <summary>
	/// Light weight DbContext implementation based on dapper
	/// Use it to create your own DbContext
	/// It will help manage connection life time and transactions
	/// </summary>
	public abstract class DbContext : DbContext<ISession>, IDbContext
	{
		protected DbContext(string connectionName)
			: base(connectionName)
		{ }

		protected DbContext(IDbConnectionFactory connectionFactory)
			: base(connectionFactory)
		{ }

		protected override ISession CreateSession()
		{
			return new Session(ConnectionFactory);
		}
	}
}

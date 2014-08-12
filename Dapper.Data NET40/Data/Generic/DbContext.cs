using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace Dapper.Data.Generic
{
	/// <summary>
	/// Default behavior exposed by DbContext helps with injection
	/// </summary>
	public interface IDbContext<out TSession> : IDbCommand where TSession : ISession, IDisposable
	{
		void Batch(Action<TSession> action);
		TResult Batch<TResult>(Func<TSession, TResult> func);
	}

	public abstract class DbContext<TSession> : IDbContext<TSession> where TSession : ISession, IDisposable
	{
		protected DbContext(string connectionName)
			: this(new DbConnectionFactory(connectionName))
		{ }

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
		public void Batch(Action<TSession> body)
		{
			using (var ctx = CreateSession())
			{
				body(ctx);
			}
		}

		/// <summary>
		/// Enables execution of multiple statements and helps with
		/// transaction management
		/// </summary>
		public TResult Batch<TResult>(Func<TSession, TResult> body)
		{
			using(var ctx = CreateSession())
			{
				return body(ctx);
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

		protected abstract TSession CreateSession();
	}
}

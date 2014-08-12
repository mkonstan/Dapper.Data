using System;
using System.Collections.Concurrent;
using System.Data.Common;
using Dapper.Data.Service;

namespace Dapper.Data.Generic.Service
{
	public interface IDbServiceProvider<TSession> : IServiceProvider, IDbContext<TSession> where TSession : ISession
	{
		T GetService<T>() where T : IDbService;

		Func<IDbServiceProvider<TSession>, IDbService> GetServiceConstructor<T>();
	}

	/// <summary>
	/// Work in progress
	/// I have used old style dataset and templates [DatasetTransformer.tt] to create Poco classes
	/// I would like to update it to read queries from it and build them in to services where
	/// each table in dataset will reperesent a service and queries defined in it will represent
	/// actions that can be performed by the service
	/// Heavy usage of iterfaces enable me to use injection and substitution.
	/// </summary>
	public abstract class DbServiceProvider<TSession> : DbContext<TSession>, IDbServiceProvider<TSession> where TSession : ISession
	{
		private readonly ConcurrentDictionary<Type, Func<IDbServiceProvider<TSession>, IDbService>> _services
			= new ConcurrentDictionary<Type, Func<IDbServiceProvider<TSession>, IDbService>>();

		protected DbServiceProvider(string connectionName)
			: base(connectionName)
		{ }

		protected DbServiceProvider(IDbConnectionFactory connectionFactory)
			: base(connectionFactory)
		{ }

		/// <summary>
		/// registeres new service
		/// </summary>
		[Obsolete("Please use RegisterService that excepts service constructor delegate")]
		protected void RegisterService<T>(Type constract, T service) where T : IDbService
		{
			RegisterService(constract, ctx => service);
		}

		/// <summary>
		/// registeres new service
		/// </summary>
		protected void RegisterService(Type constract, Func<IDbServiceProvider<TSession>, IDbService> constructor)
		{
			_services[constract] = constructor;
		}

		/// <summary>
		/// used to retreave the service instance
		/// </summary>
		public T GetService<T>() where T : IDbService
		{
			return (T)GetService(typeof(T));
		}

		object IServiceProvider.GetService(Type serviceType)
		{
			return GetService(serviceType);
		}

		protected object GetService(Type serviceType)
		{
			return GetService(serviceType, this);
		}

		private object GetService(Type serviceType, IDbServiceProvider<TSession> context)
		{
			return GetServiceConstructor(serviceType)(context);
		}

		public Func<IDbServiceProvider<TSession>, IDbService> GetServiceConstructor<T>()
		{
			return GetServiceConstructor(typeof(T));
		}

		protected Func<IDbServiceProvider<TSession>, IDbService> GetServiceConstructor(Type serviceType)
		{
			return _services[serviceType];
		}
	}
}
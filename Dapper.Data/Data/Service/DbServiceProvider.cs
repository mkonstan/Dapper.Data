using System;
using System.Collections.Concurrent;
using System.Data.Common;

namespace Dapper.Data.Service
{
	public interface IDbServiceProvider
	{
		T GetService<T>() where T : IDbService;
	}

	/// <summary>
	/// Work in progress
	/// I have used old style dataset and templates [DatasetTransformer.tt] to create Poco classes
	/// I would like to update it to read queries from it and build them in to services where
	/// each table in dataset will reperesent a service and queries defined in it will represent
	/// actions that can be performed by the service
	/// Heavy usage of iterfaces enable me to use injection and substitution.
	/// </summary>
	public abstract class DbServiceProvider : DbContext, IDbServiceProvider
	{
		private readonly ConcurrentDictionary<Type, IDbService> _services
			= new ConcurrentDictionary<Type, IDbService>();

		protected DbServiceProvider(IDbConnectionFactory connectionFactory)
			: base(connectionFactory)
		{ }

        /// <summary>
        /// registeres new service
        /// </summary>
        protected void RegisterService(IDbServiceContract service)
        {
            RegisterService(service.Contract, service);
        }

        protected void RegisterService<T>(Type constract, T service) where T : IDbService
		{
			_services[constract] = service;
		}

		/// <summary>
		/// used to retreave the service instance
		/// </summary>
		public T GetService<T>() where T : IDbService
		{
			return (T)GetService(typeof(T));
		}

		private object GetService(Type serviceType)
		{
			return _services[serviceType];
		}
	}
}
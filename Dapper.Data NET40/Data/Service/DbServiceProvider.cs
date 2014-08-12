using System;
using System.Collections.Concurrent;
using System.Data.Common;
using Dapper.Data.Generic;
using Dapper.Data.Generic.Service;

namespace Dapper.Data.Service
{
	public interface IDbServiceProviderSession : ISession, IDbServiceProvider
	{
	}

	public interface IDbServiceProvider : IDbServiceProvider<IDbServiceProviderSession>
	{
	}

	/// <summary>
	/// Work in progress
	/// I have used old style dataset and templates [DatasetTransformer.tt] to create Poco classes
	/// I would like to update it to read queries from it and build them in to services where
	/// each table in dataset will reperesent a service and queries defined in it will represent
	/// actions that can be performed by the service
	/// Heavy usage of iterfaces enable me to use injection and substitution.
	/// </summary>
	public class DbServiceProvider : DbServiceProvider<IDbServiceProviderSession>
	{
		protected DbServiceProvider(string connectionName) : base(connectionName)
		{ }

		protected DbServiceProvider(IDbConnectionFactory connectionFactory) : base(connectionFactory)
		{ }

		protected override IDbServiceProviderSession CreateSession()
		{
			return new ServiceSession(this, ConnectionFactory);
		}

		
		class ServiceSession : Session, IDbServiceProviderSession
		{
			private readonly IDbServiceProvider<IDbServiceProviderSession> _serviceProvider;

			public ServiceSession(
				IDbServiceProvider<IDbServiceProviderSession> serviceProvider,
				IDbConnectionFactory connectionFactory
			) : base(connectionFactory)
			{
				_serviceProvider = serviceProvider;
			}

			public void Batch(Action<IDbServiceProviderSession> body)
			{
				body(this);
			}

			public TResult Batch<TResult>(Func<IDbServiceProviderSession, TResult> body)
			{
				return body(this);
			}

			public void Dispose()
			{
				Dispose(true);
			}

			public T GetService<T>() where T : IDbService
			{
				return (T)GetServiceConstructor<T>()(this);
			}

			public object GetService(Type serviceType)
			{
				return _serviceProvider.GetService(serviceType);
			}

			public Func<IDbServiceProvider<IDbServiceProviderSession>, IDbService> GetServiceConstructor<T>()
			{
				return _serviceProvider.GetServiceConstructor<T>();
			}
		}
	}
}
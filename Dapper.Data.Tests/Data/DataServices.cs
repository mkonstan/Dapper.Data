using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper.Data.Service;

namespace Dapper.Data.Tests.Data
{
	public class CompensationServiceProvider : DbServiceProvider
	{
		public CompensationServiceProvider()
			: base(GetConnectionName())
		{ }

		private static string GetConnectionName()
		{
			return ConfigurationManager
				.AppSettings[typeof(CompensationServiceProvider).Name];
		}
	}
}

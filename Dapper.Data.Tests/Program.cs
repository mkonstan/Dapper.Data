
using System;
using System.Collections.Generic;
using System.Data.SqlServerCe;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Dapper.Data;
using Dapper.Data.Tests.Data;
using Dapper.Data.Tests.Properties;

namespace Dapper.Data.Tests
{
    class Program
    {
	    private static Settings Settings
	    {
		    get { return Dapper.Data.Tests.Properties.Settings.Default; }
	    }

	    static void Main(string[] args)
	    {
			var db = new CompensationServiceProvider();
			db.Batch(ctx =>
			{

			});
        }
    }
}

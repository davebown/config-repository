using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace CloudNative.Tests
{
    [TestClass]
    public class Initialize
    {
        [AssemblyInitialize]
        public static void AssemblyInit(TestContext context)
        {
            //Initialise dependency injection using Startup class
            var services = new ServiceCollection();
            var startup = new Startup();
            startup.ConfigureServices(services);

            //Create service provider and pass to common class for tests to use
            var serviceProvider = services.BuildServiceProvider();
            Common.ServiceProvider = serviceProvider;
        }
    }
}

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.DependencyInjection;
using CloudNative.Configuration;
using System.Threading.Tasks;
using System.Linq;
using System;

namespace CloudNative.Tests
{
    [TestClass]
    public class EtcdReposTests
    {
        IConfigurationRepository<ConfigItem, string> _repos = Common.ServiceProvider.GetRequiredService<IConfigurationRepository<ConfigItem, string>>();

        [TestMethod]
        public async Task SetAndGetConfigItem()
        {
            try
            {
                var onChangeEvent = new TaskCompletionSource<bool>();
                await _repos.WaitTillReady();

                _repos.OnChange += (sender, e) =>
                {
                    if (e.Updated.Any(configItem => configItem.Id == "bob"))
                    {
                        onChangeEvent.TrySetResult(true);
                    }
                };

                await _repos.Set(new ConfigItem { Id = "bob", Name = "Test" });

                //Wait for OnChange event delegate be called or timeout after a second
                await Task.WhenAny(onChangeEvent.Task, Task.Delay(10000));

                var bob = await _repos.Get("bob");

                Assert.AreEqual(true, onChangeEvent.Task.IsCompletedSuccessfully && onChangeEvent.Task.Result, "A valid OnChange event has not fired!");
                Assert.IsNotNull(bob, "Bob ConfigItem should have been retreived and not be null.");
                Assert.AreEqual("Test", bob.Name, "Bob config item should have had a Name of 'Test'");

            }
            finally
            {
                try
                {
                    await _repos.Remove("bob");
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task RemoveConfigItem()
        {

            var onChangeEvent = new TaskCompletionSource<bool>();
            var onDeleteEvent = new TaskCompletionSource<bool>();
            await _repos.WaitTillReady();

            _repos.OnChange += (sender, e) =>
            {
                if (e.Updated.Any(configItem => configItem.Id == "deleteit"))
                {
                    onChangeEvent.TrySetResult(true);
                }
                else if (e.Removed.Any(configItem => configItem.Id == "deleteit"))
                {
                    onDeleteEvent.TrySetResult(true);
                }
            };

            await _repos.Set(new ConfigItem { Id = "deleteit", Name = "Test" });

            //Wait for OnChange event delegate be called or timeout after a few seconds
            await Task.WhenAny(onChangeEvent.Task, Task.Delay(10000));

            //Initially get the item
            var deleteit1 = await _repos.Get("deleteit");

            //Delete the item
            await _repos.Remove("deleteit");

            //Wait for OnChange event delegate be called or timeout after a few seconds
            await Task.WhenAny(onDeleteEvent.Task, Task.Delay(10000));

            //Try and get the item after delete
            var deleteit2 = await _repos.Get("deleteit");

            Assert.AreEqual(true, onChangeEvent.Task.IsCompletedSuccessfully && onChangeEvent.Task.Result, "A valid OnChange event has not fired for the set!");
            Assert.AreEqual(true, onDeleteEvent.Task.IsCompletedSuccessfully && onDeleteEvent.Task.Result, "A valid OnChange event has not fired for the delete!");
            Assert.IsNotNull(deleteit1, "ConfigItem should have been retreived and not be null.");
            Assert.IsNull(deleteit2, "ConfigItem should not have been retreived because it should have been deleted.");
        }


        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task ConflictingVersion()
        {
            try
            {
                await _repos.WaitTillReady();
                var configItem = new ConfigItem { Id = "conflict", Name = "Test" };
                await _repos.Set(configItem);
                Assert.IsTrue(configItem.Version > 0, "Version was not greater than 0 and should be");
                await _repos.Set(new ConfigItem { Id = "conflict", Name = "Updated" });
            }
            finally
            {
                try
                {
                    await _repos.Remove("conflict");
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task NoneConflictingVersion()
        {
            try
            {
                await _repos.WaitTillReady();
                var configItem = new ConfigItem { Id = "nonconflict", Name = "Test" };
                await _repos.Set(configItem);
                var createdVersion = configItem.Version;
                Assert.IsTrue(configItem.Version > 0, "Version was not greater than 0 and should be");
                configItem.Name = "Updated";
                await _repos.Set(configItem);
                var updatedVersion = configItem.Version;
                Assert.IsTrue(createdVersion > 0, "Version was not greater than 0 and should be");
                Assert.IsTrue(updatedVersion > createdVersion, "Updated Version was not greater than the Created Version and should be");
            }
            finally
            {
                try
                {
                    await _repos.Remove("nonconflict");
                }
                catch (Exception) { }
            }
        }
    }
}

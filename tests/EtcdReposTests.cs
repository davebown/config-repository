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
            var configItemId = "config_item";
            try
            {
                var onChangeEvent = new TaskCompletionSource<bool>();
                await _repos.WaitTillReady();

                _repos.OnChange += (sender, e) =>
                {
                    if (e.Updated.Any(configItem => configItem.Id == configItemId))
                    {
                        onChangeEvent.TrySetResult(true);
                    }
                };

                await _repos.Set(new ConfigItem { Id = configItemId, Name = "Test" });

                //Wait for OnChange event delegate be called or timeout after a second
                await Task.WhenAny(onChangeEvent.Task, Task.Delay(10000));

                var bob = await _repos.Get(configItemId);

                Assert.AreEqual(true, onChangeEvent.Task.IsCompletedSuccessfully && onChangeEvent.Task.Result, "A valid OnChange event has not fired!");
                Assert.IsNotNull(bob, "Bob ConfigItem should have been retreived and not be null.");
                Assert.AreEqual(configItemId, bob.Id, "Bob config item should have an id of 'config_item'");
                Assert.AreEqual("Test", bob.Name, "Bob config item should have had a Name of 'Test'");
                Assert.AreNotSame(0, bob.Version, "Version should not be 0");
            }
            finally
            {
                try
                {
                    await _repos.Remove(configItemId);
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task SetAndGetConfigItemWithNamespace()
        {
            var nameSpace = "folder";
            var configItemId = "config_item";
            try
            {
                var onChangeEvent = new TaskCompletionSource<bool>();
                await _repos.WaitTillReady();

                _repos.OnChange += (sender, e) =>
                {
                    if (e.Updated.Any(configItem => configItem.Id == configItemId))
                    {
                        onChangeEvent.TrySetResult(true);
                    }
                };

                await _repos.Set(new ConfigItem { Id = configItemId, Namespace = nameSpace, Name = "Test" });

                //Wait for OnChange event delegate be called or timeout after a second
                await Task.WhenAny(onChangeEvent.Task, Task.Delay(10000));

                var bob = await _repos.Get(nameSpace, configItemId);

                Assert.AreEqual(true, onChangeEvent.Task.IsCompletedSuccessfully && onChangeEvent.Task.Result, "A valid OnChange event has not fired!");
                Assert.IsNotNull(bob, "Bob ConfigItem should have been retreived and not be null.");
                Assert.AreEqual(configItemId, bob.Id, "Bob config item should have an id of 'config_item'");
                Assert.AreEqual($"/{nameSpace}", bob.Namespace, "Bob config item should have a namespace of 'folder'");
                Assert.AreEqual("Test", bob.Name, "Bob config item should have had a Name of 'Test'");
                Assert.AreNotSame(0, bob.Version, "Version should not be 0");
            }
            finally
            {
                try
                {
                    await _repos.Remove(nameSpace, configItemId);
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task RemoveConfigItem()
        {
            var configItemId = "deleteit";
            var onChangeEvent = new TaskCompletionSource<bool>();
            var onDeleteEvent = new TaskCompletionSource<bool>();
            await _repos.WaitTillReady();

            _repos.OnChange += (sender, e) =>
            {
                if (e.Updated.Any(configItem => configItem.Id == configItemId))
                {
                    onChangeEvent.TrySetResult(true);
                }
                else if (e.Removed.Any(configItem => configItem.Id == configItemId))
                {
                    onDeleteEvent.TrySetResult(true);
                }
            };

            await _repos.Set(new ConfigItem { Id = configItemId, Name = "Test" });

            //Wait for OnChange event delegate be called or timeout after a few seconds
            await Task.WhenAny(onChangeEvent.Task, Task.Delay(10000));

            //Initially get the item
            var deleteit1 = await _repos.Get(configItemId);

            //Delete the item
            await _repos.Remove(configItemId);

            //Wait for OnChange event delegate be called or timeout after a few seconds
            await Task.WhenAny(onDeleteEvent.Task, Task.Delay(10000));

            //Try and get the item after delete
            var deleteit2 = await _repos.Get(configItemId);

            Assert.AreEqual(true, onChangeEvent.Task.IsCompletedSuccessfully && onChangeEvent.Task.Result, "A valid OnChange event has not fired for the set!");
            Assert.AreEqual(true, onDeleteEvent.Task.IsCompletedSuccessfully && onDeleteEvent.Task.Result, "A valid OnChange event has not fired for the delete!");
            Assert.IsNotNull(deleteit1, "ConfigItem should have been retreived and not be null.");
            Assert.IsNull(deleteit2, "ConfigItem should not have been retreived because it should have been deleted.");
        }


        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public async Task ConflictingVersion()
        {
            var configItemId = "config_item_with_conflict";
            try
            {
                await _repos.WaitTillReady();
                var configItem = new ConfigItem { Id = configItemId, Name = "Test" };
                await _repos.Set(configItem);
                Assert.IsTrue(configItem.Version > 0, "Version was not greater than 0 and should be");
                await _repos.Set(new ConfigItem { Id = configItemId, Name = "Updated" });
            }
            finally
            {
                try
                {
                    await _repos.Remove(configItemId);
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task NoneConflictingVersion()
        {
            var configItemId = "config_item_without_conflict";
            try
            {
                await _repos.WaitTillReady();
                var configItem = new ConfigItem { Id = configItemId, Name = "Test" };
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
                    await _repos.Remove(configItemId);
                }
                catch (Exception) { }
            }
        }

        [TestMethod]
        public async Task SetAndGetAllConfigItemWithDerivedType()
        {
            var rootItemId = "root";
            var derivedItemId = "dervied";

            try
            {
                var onChangeEvent1 = new TaskCompletionSource<bool>();
                var onChangeEvent2 = new TaskCompletionSource<bool>();

                await _repos.WaitTillReady();

                _repos.OnChange += (sender, e) =>
                {
                    if (e.Updated.Any(configItem => configItem.Id == rootItemId))
                    {
                        onChangeEvent1.TrySetResult(true);
                    }

                    if (e.Updated.Any(configItem => configItem.Id == derivedItemId))
                    {
                        onChangeEvent2.TrySetResult(true);
                    }
                };

                await _repos.Set(new ConfigItem { Id = rootItemId, Name = "Root type" });
                await _repos.Set(new ChildConfigItem { Id = derivedItemId, Name = "Derived type", Description = "A derived entity type"});

                //Wait for OnChange event delegate be called or timeout after a second
                await Task.WhenAny(Task.WhenAll(onChangeEvent1.Task, onChangeEvent2.Task), Task.Delay(10000));

                //Get all the config items
                var configItems = await _repos.GetAll();

                Assert.AreEqual(true, onChangeEvent1.Task.IsCompletedSuccessfully && onChangeEvent1.Task.Result, "A valid OnChange 1 event has not fired!");
                Assert.AreEqual(true, onChangeEvent2.Task.IsCompletedSuccessfully && onChangeEvent1.Task.Result, "A valid OnChange 2 event has not fired!");
                Assert.AreEqual(2, configItems.Count(), "There should be 2 comnfig items.");
                Assert.AreEqual(1, configItems.Count(c => c is ChildConfigItem), "There should be 1 ChildConfigItem retreived");
                Assert.AreEqual(1, configItems.Count(c => !(c is ChildConfigItem)), "There should be 1 ConfigItem retreived");
            }
            finally
            {
                try
                {
                    await _repos.Remove(rootItemId);
                    await _repos.Remove(derivedItemId);
                }
                catch (Exception) { }
            }
        }
    }
}

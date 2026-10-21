using System.Collections.Generic;
using System.Linq;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Common.Options;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Datastore.Extensions;
using NzbDrone.Core.Download;
using NzbDrone.Core.Download.TrackedDownloads;
using NzbDrone.Core.Indexers;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Host;
using NzbDrone.SignalR;
using NzbDrone.Test.Common;
using IServiceProvider = System.IServiceProvider;

namespace NzbDrone.App.Test
{
    [TestFixture]
    public class ContainerFixture : TestBase
    {
        private IServiceProvider _container;

        [SetUp]
        public void SetUp()
        {
            var args = new StartupContext("first", "second");

            var container = new Container(rules => rules.WithNzbDroneRules())
                .AutoAddServices(Bootstrap.ASSEMBLIES)
                .AddNzbDroneLogger()
                .AddDummyDatabase()
                .AddStartupContext(args);

            // set up a dummy broadcaster and lifetime to allow tests to resolve
            container.RegisterInstance<IHostLifetime>(new Mock<IHostLifetime>().Object);
            container.RegisterInstance<IBroadcastSignalRMessage>(new Mock<IBroadcastSignalRMessage>().Object);
            container.RegisterInstance<IOptions<PostgresOptions>>(new Mock<IOptions<PostgresOptions>>().Object);
            container.RegisterInstance<IOptions<AuthOptions>>(new Mock<IOptions<AuthOptions>>().Object);
            container.RegisterInstance<IOptions<AppOptions>>(new Mock<IOptions<AppOptions>>().Object);
            container.RegisterInstance<IOptions<ServerOptions>>(new Mock<IOptions<ServerOptions>>().Object);
            container.RegisterInstance<IOptions<UpdateOptions>>(new Mock<IOptions<UpdateOptions>>().Object);
            container.RegisterInstance<IOptions<LogOptions>>(new Mock<IOptions<LogOptions>>().Object);

            _container = container.GetServiceProvider();
        }

        [Test]
        public void diagnose_how_the_container_answers_is_this_a_service()
        {
            // The provider ASP.NET sees in the real host: DryIocServiceProviderFactory wrapped around a container with the app rules.
            var factory = new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules()));
            var hostProvider = factory.CreateServiceProvider(factory.CreateBuilder(new ServiceCollection()));
            var isService = hostProvider.GetService(typeof(IServiceProviderIsService)) as IServiceProviderIsService;
            var parts = new List<string> { "hostProvider=" + hostProvider.GetType().Name, "isServiceAvailable=" + (isService != null) };

            var probes = new Dictionary<string, System.Type>
            {
                ["CommandResource"] = typeof(Readarr.Api.V1.Commands.CommandResource),
                ["TagResource"] = typeof(Readarr.Api.V1.Tags.TagResource),
                ["ListOfTagResource"] = typeof(List<Readarr.Api.V1.Tags.TagResource>),
                ["ManualImportUpdateList"] = typeof(List<Readarr.Api.V1.ManualImport.ManualImportUpdateResource>),
                ["string"] = typeof(string),
                ["int"] = typeof(int),
                ["unregisteredPlainClass"] = typeof(System.Text.StringBuilder),
                ["IConfigFileProvider"] = typeof(NzbDrone.Core.Configuration.IConfigFileProvider)
            };

            foreach (var probe in probes)
            {
                var answer = isService == null ? "n/a" : isService.IsService(probe.Value).ToString();
                parts.Add(probe.Key + ".IsService=" + answer);
            }

            parts.Add("hostProvider.GetService(CommandResource)=" + (hostProvider.GetService(typeof(Readarr.Api.V1.Commands.CommandResource)) != null ? "non-null" : "null"));

            Assert.Fail("DIAGNOSIS: " + string.Join(" | ", parts));
        }

        [Test]
        public void should_be_able_to_resolve_indexers()
        {
            _container.GetRequiredService<IEnumerable<IIndexer>>().Should().NotBeEmpty();
        }

        [Test]
        public void should_be_able_to_resolve_downloadclients()
        {
            _container.GetRequiredService<IEnumerable<IDownloadClient>>().Should().NotBeEmpty();
        }

        [Test]
        public void container_should_inject_itself()
        {
            var factory = _container.GetRequiredService<IServiceFactory>();

            factory.Build<IIndexerFactory>().Should().NotBeNull();
        }

        [Test]
        public void should_resolve_command_executor_by_name()
        {
            var genericExecutor = typeof(IExecute<>).MakeGenericType(typeof(RssSyncCommand));

            var executor = _container.GetRequiredService(genericExecutor);

            executor.Should().NotBeNull();
            executor.Should().BeAssignableTo<IExecute<RssSyncCommand>>();
        }

        [Test]
        public void should_return_same_instance_via_resolve_and_resolveall()
        {
            var first = (DownloadMonitoringService)_container.GetRequiredService<IHandle<TrackedDownloadsRemovedEvent>>();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_same_interface()
        {
            var first = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = _container.GetServices<IHandle<TrackedDownloadsRemovedEvent>>().OfType<DownloadMonitoringService>().Single();

            first.Should().BeSameAs(second);
        }

        [Test]
        public void should_return_same_instance_of_singletons_by_different_interfaces()
        {
            var first = _container.GetServices<IHandle<BookGrabbedEvent>>().OfType<DownloadMonitoringService>().Single();
            var second = (DownloadMonitoringService)_container.GetRequiredService<IExecute<RefreshMonitoredDownloadsCommand>>();

            first.Should().BeSameAs(second);
        }
    }
}

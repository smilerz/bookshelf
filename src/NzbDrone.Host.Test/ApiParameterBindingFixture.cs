using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Serializer;
using NzbDrone.Host;
using Readarr.Api.V1.Commands;
using Readarr.Http;

namespace NzbDrone.App.Test
{
    // An action parameter with no explicit source, of a type the application's container knows as a service.
    [V1ApiController("bindingprobe")]
    public class BindingProbeController : ControllerBase
    {
        [HttpPost]
        public string Post(CommandResource resource) => resource == null ? "null-resource" : (resource.Name ?? "null-name");
    }

    [TestFixture]
    public class ApiParameterBindingFixture
    {
        // Runs a real web server in the test with the real container (every class in the application's assemblies registered, as at
        // start-up) and posts the body the web page sends for a command.
        private static async Task<string> PostCommand(bool useStartupSetup)
        {
            var container = new Container(rules => rules.WithNzbDroneRules()).AutoAddServices(Bootstrap.ASSEMBLIES);

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(BindingProbeController).Assembly.GetName().Name });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Host.UseServiceProviderFactory(new DryIocServiceProviderFactory(container));

            var mvc = builder.Services.AddControllers()
                .AddApplicationPart(typeof(BindingProbeController).Assembly)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions));

            if (useStartupSetup)
            {
                mvc.ConfigureApiBehaviorOptions(ApiBehaviorSetup.Configure);
            }

            // The controller marker declares a CORS policy, which needs the policy and the middleware.
            builder.Services.AddCors(o => o.AddPolicy(VersionedApiControllerAttribute.API_CORS_POLICY, p => p.AllowAnyOrigin()));

            var app = builder.Build();
            app.UseRouting();
            app.UseCors();
            app.MapControllers();
            await app.StartAsync();

            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                var body = new StringContent("{\"name\":\"RefreshAuthor\",\"authorId\":40}", Encoding.UTF8, "application/json");
                var response = await client.PostAsync("api/v1/bindingprobe", body);
                return (int)response.StatusCode + ":" + await response.Content.ReadAsStringAsync();
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        [Test]
        public async Task should_read_a_resource_parameter_from_the_request_body_with_the_startup_setup()
        {
            (await PostCommand(true)).Should().Be("200:RefreshAuthor");
        }

        [Test]
        public async Task without_the_setup_the_container_supplies_an_empty_resource_and_the_body_is_ignored()
        {
            // Documents the cause. If this stops being true the setup is no longer needed and both tests can go.
            (await PostCommand(false)).Should().Be("200:null-name");
        }
    }
}

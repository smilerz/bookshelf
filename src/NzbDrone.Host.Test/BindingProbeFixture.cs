using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using NzbDrone.Common.Composition.Extensions;
using NzbDrone.Common.Serializer;
using Readarr.Http;
using Readarr.Http.Middleware;

namespace NzbDrone.App.Test
{
    public class ProbeResource
    {
        public string Name { get; set; }
    }

    [ApiController]
    [Route("probe/plain")]
    public class PlainProbeController : ControllerBase
    {
        [HttpPost]
        public string Post(ProbeResource resource) => resource == null ? "null-resource" : (resource.Name ?? "null-name");
    }

    [V1ApiController("probe")]
    public class OurProbeController : ControllerBase
    {
        [HttpPost]
        public string Post(ProbeResource resource) => resource == null ? "null-resource" : (resource.Name ?? "null-name");
    }

    [TestFixture]
    public class BindingProbeFixture
    {
        private static async Task<string> Post(HttpClient client, string path)
        {
            var response = await client.PostAsync(path, new StringContent("{\"name\":\"x\"}", Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            return (int)response.StatusCode + ":" + (text.Length > 40 ? text.Substring(0, 40) : text);
        }

        private static async Task<string> Run(string label, Action<WebApplicationBuilder> configure, bool buffering)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(OurProbeController).Assembly.GetName().Name });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            configure(builder);

            var app = builder.Build();
            if (buffering)
            {
                app.UseMiddleware<BufferingMiddleware>(new List<string> { "/api/v1/probe" });
            }

            app.MapControllers();
            await app.StartAsync();

            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                var plain = await Post(client, "probe/plain");
                var ours = await Post(client, "api/v1/probe");
                return $"{label}: plain=[{plain}] ours=[{ours}]";
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        [Test]
        public async Task diagnose_which_part_of_the_setup_loses_the_body()
        {
            var part = typeof(OurProbeController).Assembly;
            var results = new List<string>();

            results.Add(await Run("H1 defaults", b => b.Services.AddControllers().AddApplicationPart(part), false));

            results.Add(await Run("H2 +our json", b => b.Services.AddControllers().AddApplicationPart(part)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions)), false));

            results.Add(await Run("H3 +buffering", b => b.Services.AddControllers().AddApplicationPart(part)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions)), true));

            results.Add(await Run("H4 +controllersAsServices", b => b.Services.AddControllers().AddApplicationPart(part)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions))
                .AddControllersAsServices(), true));

            results.Add(await Run("H5 +DryIoc", b =>
            {
                b.Host.UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())));
                b.Services.AddControllers().AddApplicationPart(part)
                    .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions))
                    .AddControllersAsServices();
            }, true));

            Assert.Fail("PROBE: " + string.Join(" || ", results));
        }
    }
}

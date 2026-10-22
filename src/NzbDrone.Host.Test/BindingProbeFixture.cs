using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using DryIoc;
using DryIoc.Microsoft.DependencyInjection;
using FluentValidation;
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
using NzbDrone.Http.REST.Attributes;
using Readarr.Api.V1.Commands;
using Readarr.Http;
using Readarr.Http.Middleware;
using Readarr.Http.REST;

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

    [V1ApiController("probecmd")]
    public class CommandInferredProbeController : ControllerBase
    {
        [HttpPost]
        public string Post(CommandResource resource) => resource == null ? "null-resource" : (resource.Name ?? "null-name");
    }

    [V1ApiController("probecmdbody")]
    public class CommandExplicitProbeController : ControllerBase
    {
        [HttpPost]
        public string Post([FromBody] CommandResource resource) => resource == null ? "null-resource" : (resource.Name ?? "null-name");
    }

    // Real base class, plain attribute.
    [V1ApiController("probecmdbase")]
    public class CommandRestBaseProbeController : RestController<CommandResource>
    {
        [HttpPost]
        public IActionResult Post(CommandResource resource) => Content(resource == null ? "null-resource" : (resource.Name ?? "null-name"));

        protected override CommandResource GetResourceById(int id) => null;
    }

    // Plain base class, real action attribute.
    [V1ApiController("probecmdattr")]
    public class CommandRestAttrProbeController : ControllerBase
    {
        [RestPostById]
        public IActionResult Post(CommandResource resource) => Content(resource == null ? "null-resource" : (resource.Name ?? "null-name"));
    }

    // Real base class, real action attribute and the validation the command controller declares.
    [V1ApiController("probecmdfull")]
    public class CommandRestFullProbeController : RestController<CommandResource>
    {
        public CommandRestFullProbeController()
        {
            PostValidator.RuleFor(c => c.Name).NotNull().NotEmpty();
        }

        [RestPostById]
        public IActionResult Post(CommandResource resource) => Content(resource == null ? "null-resource" : (resource.Name ?? "null-name"));

        protected override CommandResource GetResourceById(int id) => null;
    }

    [TestFixture]
    public class BindingProbeFixture
    {
        private static async Task<string> Post(HttpClient client, string path, string json = "{\"name\":\"x\"}")
        {
            var response = await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));
            var text = await response.Content.ReadAsStringAsync();
            return (int)response.StatusCode + ":" + (text.Length > 40 ? text.Substring(0, 40) : text);
        }

        private static async Task<string> Run(string label, Action<WebApplicationBuilder> configure, bool buffering)
        {
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ApplicationName = typeof(OurProbeController).Assembly.GetName().Name });
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            configure(builder);

            // The Bookshelf controller marker declares the CORS policy "ApiCorsPolicy", which needs the policy and the middleware.
            builder.Services.AddCors(o => o.AddPolicy(VersionedApiControllerAttribute.API_CORS_POLICY, p => p.AllowAnyOrigin()));

            var app = builder.Build();
            app.UseRouting();
            app.UseCors();

            if (buffering)
            {
                app.UseMiddleware<BufferingMiddleware>(new List<string> { "/api/v1/probe", "/api/v1/probecmd", "/api/v1/probecmdbody", "/api/v1/probecmdbase", "/api/v1/probecmdattr", "/api/v1/probecmdfull" });
            }

            app.MapControllers();
            await app.StartAsync();

            try
            {
                var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First();
                using var client = new HttpClient { BaseAddress = new Uri(address) };
                var plain = await Post(client, "probe/plain");
                var ours = await Post(client, "api/v1/probe");
                var command = "{\"name\":\"RefreshAuthor\",\"authorId\":40}";
                var cmdInferred = await Post(client, "api/v1/probecmd", command);
                var cmdExplicit = await Post(client, "api/v1/probecmdbody", command);
                var restBase = await Post(client, "api/v1/probecmdbase", command);
                var restAttr = await Post(client, "api/v1/probecmdattr", command);
                var restFull = await Post(client, "api/v1/probecmdfull", command);
                return $"{label}: inferred=[{cmdInferred}] explicit=[{cmdExplicit}] restBase=[{restBase}] restAttr=[{restAttr}] restFull=[{restFull}]";
            }
            finally
            {
                await app.StopAsync();
                await app.DisposeAsync();
            }
        }

        private static void UseOurJson(WebApplicationBuilder builder)
        {
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(OurProbeController).Assembly)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions));
        }

        private static void UseControllersAsServices(WebApplicationBuilder builder)
        {
            builder.Services.AddControllers()
                .AddApplicationPart(typeof(OurProbeController).Assembly)
                .AddJsonOptions(o => STJson.ApplySerializerSettings(o.JsonSerializerOptions))
                .AddControllersAsServices();
        }

        private static void UseDryIoc(WebApplicationBuilder builder)
        {
            builder.Host.UseServiceProviderFactory(new DryIocServiceProviderFactory(new Container(rules => rules.WithNzbDroneRules())));
            UseControllersAsServices(builder);
        }

        [Test]
        public async Task diagnose_which_part_of_the_setup_loses_the_body()
        {
            var results = new List<string>();

            results.Add(await Run("H1 defaults", b => b.Services.AddControllers().AddApplicationPart(typeof(OurProbeController).Assembly), false));
            results.Add(await Run("H2 +our json", UseOurJson, false));
            results.Add(await Run("H3 +buffering", UseOurJson, true));
            results.Add(await Run("H4 +controllersAsServices", UseControllersAsServices, true));
            results.Add(await Run("H5 +DryIoc", UseDryIoc, true));

            Assert.Fail("PROBE: " + string.Join(" || ", results));
        }
    }
}

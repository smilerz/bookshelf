using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;
using Readarr.Api.V1.Commands;
using Readarr.Http.REST;

namespace NzbDrone.Host.Test
{
    [TestFixture]
    public class ApiBodyBindingFixture
    {
        // A resource parameter without an explicit source is bound by inference. On .NET 10 that left the resource empty, so every
        // command sent from the web UI was rejected with "Name must not be empty". Naming the body as the source avoids the inference.
        [Test]
        public void every_write_action_reads_its_resource_from_the_body()
        {
            var missing = typeof(CommandController).Assembly.GetTypes()
                .Where(t => t.IsClass && typeof(ControllerBase).IsAssignableFrom(t))
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                .Where(m => m.GetCustomAttributes(true).Any(a => a is HttpPostAttribute || a is HttpPutAttribute || a is HttpPatchAttribute))
                .SelectMany(m => m.GetParameters()
                    .Where(p => IsResource(p.ParameterType) && p.GetCustomAttribute<FromBodyAttribute>() == null)
                    .Select(p => $"{m.DeclaringType.Name}.{m.Name}({p.Name})"))
                .OrderBy(x => x)
                .ToList();

            missing.Should().BeEmpty("these write actions take a resource without [FromBody]");
        }

        [Test]
        public void the_check_recognises_resources_lists_of_resources_and_generic_resources()
        {
            IsResource(typeof(CommandResource)).Should().BeTrue();
            IsResource(typeof(List<CommandResource>)).Should().BeTrue();
            IsResource(typeof(string)).Should().BeFalse();
            IsResource(typeof(int)).Should().BeFalse();
        }

        private static bool IsResource(Type type)
        {
            if (type.IsGenericParameter)
            {
                return type.GetGenericParameterConstraints().Any(IsResource);
            }

            if (typeof(RestResource).IsAssignableFrom(type))
            {
                return true;
            }

            return type.IsGenericType && type.GetGenericArguments().Any(IsResource);
        }
    }
}

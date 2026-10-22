using Microsoft.AspNetCore.Mvc;

namespace NzbDrone.Host
{
    // The container registers every concrete class in the application's assemblies as a service, including every API resource
    // (CommandResource, TagResource and so on). Since .NET 7 ASP.NET Core treats an action parameter whose type is a registered service
    // as [FromServices], so such a resource was taken from the container, new and empty, and the request body was never read: every
    // command sent from the web page was rejected with "Name must not be empty". Turning that inference off restores the .NET 6
    // behaviour (a complex parameter comes from the request body). Used by Startup, and by the test that guards it.
    public static class ApiBehaviorSetup
    {
        public static void Configure(ApiBehaviorOptions options)
        {
            options.DisableImplicitFromServicesParameters = true;
        }
    }
}

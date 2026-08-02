using System;

namespace Flowboard.Functions.Middleware
{
    /// <summary>
    /// Marks an Azure Function method as exempt from <see cref="JwtAuthMiddleware"/>'s
    /// default-deny JWT check. Deliberately our own attribute (not
    /// Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute) so the intent is
    /// unambiguous in a hosting model that does not run the ASP.NET Core auth pipeline.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public sealed class AllowAnonymousAttribute : Attribute
    {
    }
}

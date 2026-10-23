using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Themis.Kernel.Pipeline.Http;

/// <summary>
/// The order every host applies to a request before its endpoints (RR-72): the correlation id first, so its logging
/// scope surrounds the exception handler, then the exception handler. The log line of an unhandled exception and the
/// 500 problem body therefore both carry the request's correlation id (D-25), as every other answer does. Then
/// authentication and authorization, in this fixed place for every host (RR-71): the fallback policy answers an
/// anonymous request with the shared 401 before the endpoint runs.
/// </summary>
internal static class KernelRequestPipeline
{
    /// <summary>Problem details written by the framework (the 500 of an unhandled exception among them) carry correlationId like the pipeline's own (R-API-03).</summary>
    public static IServiceCollection AddKernelProblemDetails(this IServiceCollection services) =>
        services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
            context.ProblemDetails.Extensions["correlationId"] = CorrelationIdMiddleware.Of(context.HttpContext).Value);

    public static IApplicationBuilder UseKernelRequestPipeline(this IApplicationBuilder app)
    {
        app.UseCorrelationId();
        app.UseExceptionHandler();
        app.UseAuthentication();
        app.UseAuthorization();
        return app;
    }
}

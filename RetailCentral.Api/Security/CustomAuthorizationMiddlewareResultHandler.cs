using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace RetailCentral.Api.Security
{
    public class CustomAuthorizationMiddlewareResultHandler : IAuthorizationMiddlewareResultHandler
    {
        private readonly AuthorizationMiddlewareResultHandler _defaultHandler = new();

        public async Task HandleAsync(
            RequestDelegate next,
            HttpContext context,
            AuthorizationPolicy policy,
            PolicyAuthorizationResult authorizeResult)
        {
            // Only redirect true authorization failures.
            // Do NOT redirect Challenged requests, because auth handshakes /
            // Negotiate hiccups can otherwise look like false "Access Denied" pages.
            if (authorizeResult.Forbidden)
            {
                if (IsUiRequest(context))
                {
                    context.Response.Redirect($"/Home/AccessDenied?path={context.Request.Path}");
                    return;
                }
            }

            await _defaultHandler.HandleAsync(next, context, policy, authorizeResult);
        }

        private static bool IsUiRequest(HttpContext context)
        {
            return context.Request.Path.StartsWithSegments("/Dashboard") ||
                   context.Request.Path.StartsWithSegments("/Home");
        }
    }
}
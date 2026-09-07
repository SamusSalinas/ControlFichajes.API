namespace ControlFichajes.API.Security;

public sealed class EmpresaContextMiddleware
{
    private readonly RequestDelegate _next;

    public EmpresaContextMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        EmpresaAccess.ApplyEmpresaContext(context.User, context.Request.Headers);
        await _next(context);
    }
}
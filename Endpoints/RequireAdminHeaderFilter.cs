namespace ShopAuth.Endpoints;

// Defense-in-depth CSRF, en plus du cookie SameSite=Lax : exige un en-tete
// personnalise sur toute requete qui modifie des donnees (POST/PUT/DELETE).
// Un formulaire HTML classique ne sait pas ajouter d'en-tete custom, et un
// fetch() cross-origin qui essaierait declencherait un preflight CORS que
// le serveur rejetterait (l'origine de l'attaquant n'est pas autorisee).
// Protege contre une attaque passant par un navigateur - pas contre un appel
// direct via un outil comme Postman, qui suppose deja que l'attaquant a le
// cookie, donc un tout autre probleme.
public class RequireAdminHeaderFilter : IEndpointFilter
{
    private const string HeaderName = "X-Sso-Admin";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var method = context.HttpContext.Request.Method;
        var isMutation = !HttpMethods.IsGet(method) && !HttpMethods.IsHead(method) && !HttpMethods.IsOptions(method);

        if (isMutation && !context.HttpContext.Request.Headers.ContainsKey(HeaderName))
            return Results.Json(new { error = $"En-tête {HeaderName} requis." }, statusCode: StatusCodes.Status403Forbidden);

        return await next(context);
    }
}

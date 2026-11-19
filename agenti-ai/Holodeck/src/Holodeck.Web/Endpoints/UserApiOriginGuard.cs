namespace Holodeck.Web.Endpoints;

// Opaque applet frames and external sites cannot use browser form requests to mutate the user API.
public static class UserApiOriginGuard
{
  public static bool Allows(HttpRequest request)
  {
    if (!request.Path.StartsWithSegments("/api") || HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method) || HttpMethods.IsOptions(request.Method))
    {
      return true;
    }

    string origin = request.Headers.Origin.ToString();
    if (origin.Length == 0)
    {
      return request.Headers["Sec-Fetch-Site"] != "cross-site";
    }

    if (!Uri.TryCreate(origin, UriKind.Absolute, out Uri? source))
    {
      return false;
    }

    return source.Scheme == request.Scheme && string.Equals(source.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
  }
}

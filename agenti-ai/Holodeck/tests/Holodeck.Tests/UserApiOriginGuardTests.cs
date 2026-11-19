using Holodeck.Web.Endpoints;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Holodeck.Tests;

public sealed class UserApiOriginGuardTests
{
  [Theory]
  [InlineData("POST", "null", "cross-site", false)]
  [InlineData("POST", "https://holodeck.test:7077", "same-site", false)]
  [InlineData("DELETE", "http://other.test", "cross-site", false)]
  [InlineData("POST", "http://holodeck.test:7077", "same-origin", true)]
  [InlineData("POST", "", "", true)]
  [InlineData("POST", "", "cross-site", false)]
  [InlineData("GET", "null", "cross-site", true)]
  public void BrowserMutationsRequireSameOriginWhileCliAndReadOnlyAssetsRemainAvailable(string method, string origin, string fetchSite, bool expected)
  {
    DefaultHttpContext context = new();
    context.Request.Method = method;
    context.Request.Path = "/api/projects/example/stop";
    context.Request.Scheme = "http";
    context.Request.Host = new HostString("holodeck.test:7077");
    context.Request.Headers.Origin = origin;
    context.Request.Headers["Sec-Fetch-Site"] = fetchSite;

    Assert.Equal(expected, UserApiOriginGuard.Allows(context.Request));
  }
}

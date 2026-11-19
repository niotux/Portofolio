using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PiSharp.CodeIntel;

public static class CodeIntelOptionsRegistration
{
    public static IServiceCollection AddCodeIntelOptions(
        this IServiceCollection services, IConfiguration configuration)
    {
        return services.Configure<CodeIntelOptions>(configuration.GetSection("CodeIntel"));
    }
}

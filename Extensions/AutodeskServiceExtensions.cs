using ApsSamples.Models;
using ApsSamples.Services;
using Autodesk.DataManagement;
using Autodesk.Forge.DesignAutomation;

namespace ApsSamples.Extensions;

public static class AutodeskServiceExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDistributedMemoryCache();
        services.AddSession(options =>
        {
            options.IdleTimeout = TimeSpan.FromMinutes(30);
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
        });

        services.Configure<AgentOptions>(configuration.GetSection("Agent"));

        services.AddAutodeskServices(configuration);
        services.AddDesignAutomation(configuration);

        services.AddScoped<IAccLinkedFilesService, AccLinkedFilesService>();
        services.AddScoped<INamingStandardsService, NamingStandardsService>();
        services.AddScoped<IRevitAutomationService, RevitAutomationService>();
        services.AddScoped<RevitAutomationTools>();
        services.AddScoped<IProjectRoleService, ProjectRoleService>();

        services.AddSingleton<ICreationJobStatusService, CreationJobStatusService>();
        services.AddSingleton<ILinkingJobStatusService, LinkingJobStatusService>();
        services.AddSingleton<ISheetCreationJobStatusService, SheetCreationJobStatusService>();
        services.AddSingleton<IAgentTaskService, AgentTaskService>();
        services.AddSingleton<IAgentConversationService, AgentConversationService>();

        return services;
    }

    public static IServiceCollection AddAutodeskServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind APS configuration from appsettings.json
        var apsConfig = configuration.GetSection("Forge");
        var clientId = apsConfig["ClientId"] ?? throw new InvalidOperationException("Forge:ClientId is required");
        var clientSecret = apsConfig["ClientSecret"] ?? throw new InvalidOperationException("Forge:ClientSecret is required");
        var callbackUrl = apsConfig["CallbackUrl"];

        // Register user session service (scoped to maintain state per user)
        services.AddScoped<IUserSessionService, UserSessionService>();

        // Register a service to handle authentication token management
        services.AddScoped<IAPSAuthenticationService, APSAuthenticationService>(sp =>
        {
            return new APSAuthenticationService(clientId, clientSecret, callbackUrl);
        });

        // Register Data Management API client
        services.AddScoped<DataManagementClient>();

        return services;
    }
}

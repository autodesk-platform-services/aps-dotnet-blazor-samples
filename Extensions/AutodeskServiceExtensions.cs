using ApsSamples.Models;
using ApsSamples.Services;
using ApsSamples.Tools;
using Autodesk.Authentication;
using Autodesk.Construction.AccountAdmin;
using Autodesk.DataManagement;
using Autodesk.Forge.DesignAutomation;
using Autodesk.SDKManager;
using Autodesk.SecureServiceAccount.Http;
using Autodesk.Webhooks;
using Microsoft.Extensions.AI;
using OllamaSharp;

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
        services.AddConstructionAccountAdmin(configuration);

        services.AddScoped<IAccLinkedFilesService, AccLinkedFilesService>();
        services.AddScoped<INamingStandardsService, NamingStandardsService>();
        services.AddScoped<IRevitAutomationService, RevitAutomationService>();
        services.AddScoped<RevitAutomationTools>();
        services.AddScoped<BimManagerAssistantTools>();
        services.AddScoped<IProjectRoleService, ProjectRoleService>();
        services.AddScoped<ICompanyService, CompanyService>();

        services.AddSingleton<ICreationJobStatusService, CreationJobStatusService>();
        services.AddSingleton<ILinkingJobStatusService, LinkingJobStatusService>();
        services.AddSingleton<ISheetCreationJobStatusService, SheetCreationJobStatusService>();
        services.AddSingleton<IAgentTaskService, AgentTaskService>();
        services.AddSingleton<IAgentConversationService, AgentConversationService>();
        services.AddSingleton<IAgentRegistryService, AgentRegistryService>();
        services.AddSingleton<IToolCatalogService, ToolCatalogService>();
        // SsaService depends on scoped IAPSAuthenticationService + AdminClient, so it must be scoped too.
        services.AddScoped<ISsaService, SsaService>();

        var agentOptions = configuration.GetSection("Agent").Get<AgentOptions>() ?? new AgentOptions();
        services.AddSingleton<IChatClient>(_ => new OllamaApiClient(new Uri(agentOptions.OllamaEndpoint), agentOptions.ModelName));
        services.AddScoped<IAgentChatService, AgentChatService>();

        return services;
    }

    public static IServiceCollection AddAutodeskServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Bind APS configuration from appsettings.json
        var apsConfig = configuration.GetSection("Forge");
        var clientId = apsConfig["ClientId"] ?? throw new InvalidOperationException("Forge:ClientId is required");
        var clientSecret = apsConfig["ClientSecret"] ?? throw new InvalidOperationException("Forge:ClientSecret is required");
        var callbackUrl = apsConfig["CallbackUrl"];

        services.AddSingleton(_ =>
            SdkManagerBuilder
                .Create()
                .Add(new ApsConfiguration())
                .Add(ResiliencyConfiguration.CreateDefault())
                .Build());

        services.AddSingleton(sp => new AuthenticationClient(sp.GetRequiredService<SDKManager>()));

        // Register user session service (scoped to maintain state per user)
        services.AddScoped<IUserSessionService, UserSessionService>();

        // Register a service to handle authentication token management
        services.AddScoped<IAPSAuthenticationService, APSAuthenticationService>(sp =>
        {
            return new APSAuthenticationService(
                sp.GetRequiredService<AuthenticationClient>(),
                sp.GetRequiredService<IConfiguration>(),
                clientId,
                clientSecret,
                callbackUrl);
        });

        // Register Data Management API client
        services.AddScoped<DataManagementClient>();
        services.AddScoped<AdminClient>();
        services.AddScoped<WebhooksClient>();

        // SSA management API clients. Credentials for the SSA App are validated here to fail
        // fast on misconfiguration; token minting still happens lazily in APSAuthenticationService.
        var ssaConfig = configuration.GetSection("SsaApp");
        _ = ssaConfig["ClientId"] ?? throw new InvalidOperationException("SsaApp:ClientId is required");
        _ = ssaConfig["ClientSecret"] ?? throw new InvalidOperationException("SsaApp:ClientSecret is required");

        services.AddSingleton(sp => new AccountManagementApi(sp.GetRequiredService<SDKManager>()));
        services.AddSingleton(sp => new KeyManagementApi(sp.GetRequiredService<SDKManager>()));

        return services;
    }
}

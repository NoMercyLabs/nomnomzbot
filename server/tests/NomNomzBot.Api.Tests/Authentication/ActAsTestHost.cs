// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Authentication;
using NomNomzBot.Api.Authorization;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Hubs;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Api.Middleware;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Notifications;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Authentication;

/// <summary>
/// A TestServer running the dashboard's REAL identity path end to end: the production JwtBearer setup
/// (<see cref="DashboardJwtBearer"/>, incl. the revoked-session and closed-grant checks), tenant resolution,
/// the Gate-2 and Plane-C authorization handlers, the real auth / channels / roles / notifications /
/// platform-admin controllers and the dashboard hub, over the real <see cref="AppDbContext"/> on in-memory
/// SQLite. Real services wherever the answer depends on WHO is calling (users, channels, sessions, roles,
/// action authorization, impersonation); substitutes only for the outside world (Twitch, chat, event bus)
/// and for the IAM permission store, which is not what these tests are about.
/// </summary>
internal sealed class ActAsTestHost : IAsyncDisposable
{
    private readonly IHost _host;
    private readonly SqliteConnection _connection;

    private ActAsTestHost(
        IHost host,
        SqliteConnection connection,
        IEventBus eventBus,
        ITwitchModeratorsApi moderators
    )
    {
        _host = host;
        _connection = connection;
        EventBus = eventBus;
        Moderators = moderators;
    }

    public TestServer Server => _host.GetTestServer();
    public HttpClient Client { get; private set; } = null!;
    public IEventBus EventBus { get; }

    /// <summary>Twitch's "channels I moderate" answer. Offline (a failure) unless a test says otherwise.</summary>
    public ITwitchModeratorsApi Moderators { get; }

    /// <summary>The IAM principal the admin operator acts through.</summary>
    public static readonly Guid AdminPrincipalId = Guid.Parse(
        "0192b000-0000-7000-8000-00000000a0a0"
    );

    public static async Task<ActAsTestHost> StartAsync(Guid adminUserId)
    {
        SqliteConnection connection = new("Data Source=:memory:");
        connection.Open();

        IConfiguration config = new ConfigurationBuilder()
            .AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Jwt:Secret"] = "act-as-identity-swap-tests-signing-secret-0123456789abcdef",
                    ["Jwt:ExpiryMinutes"] = "60",
                }
            )
            .Build();
        JwtTokenService jwt = new(config, TimeProvider.System);
        IEventBus eventBus = Substitute.For<IEventBus>();
        IAuthService authService = Substitute.For<IAuthService>();
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        moderators
            .GetModeratedChannelsAsync(
                Arg.Any<Guid>(),
                Arg.Any<TwitchPageRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Failure<TwitchPage<TwitchModeratedChannel>>("offline", "NOT_FOUND"));

        IHostBuilder builder = new HostBuilder().ConfigureWebHost(web =>
            web.UseTestServer()
                .ConfigureServices(services =>
                {
                    Register(services, config, jwt, eventBus, authService, adminUserId, connection);
                    services.AddSingleton(moderators);
                })
                .Configure(Pipeline)
        );

        IHost host = await builder.StartAsync();
        SessionBackedAuth.Wire(authService, host.Services);

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .Database.EnsureCreatedAsync();

        ActAsTestHost testHost = new(host, connection, eventBus, moderators);
        testHost.Client = host.GetTestClient();
        return testHost;
    }

    public async Task SeedAsync(Func<AppDbContext, Task> seed)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await seed(db);
        await db.SaveChangesAsync();
    }

    public async Task<T> ReadAsync<T>(Func<AppDbContext, Task<T>> read)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await read(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    /// <summary>Opens a real login session (the same service a Twitch login ends in) for <paramref name="userId"/>.</summary>
    public async Task<SessionTokensDto> LoginAsync(Guid userId, Guid? channelId)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        Result<SessionTokensDto> session = await scope
            .ServiceProvider.GetRequiredService<ISessionService>()
            .CreateSessionAsync(userId, channelId, new("web", null, null));
        return session.Value;
    }

    public async Task<bool> IsSessionLiveAsync(string rawRefreshToken)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        Result<AuthSessionDto> session = await scope
            .ServiceProvider.GetRequiredService<ISessionService>()
            .PeekSessionAsync(rawRefreshToken);
        return session.IsSuccess;
    }

    public async ValueTask DisposeAsync()
    {
        Client.Dispose();
        await _host.StopAsync();
        _host.Dispose();
        await _connection.DisposeAsync();
    }

    private static void Pipeline(IApplicationBuilder app)
    {
        // Same order as Program.cs: authenticate, resolve the tenant from the authenticated caller, authorize.
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseAuthorization();
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllers();
            endpoints.MapHub<DashboardHub>("/hubs/dashboard");
        });
    }

    private static void Register(
        IServiceCollection services,
        IConfiguration config,
        JwtTokenService jwt,
        IEventBus eventBus,
        IAuthService authService,
        Guid adminUserId,
        SqliteConnection connection
    )
    {
        services.AddRouting();
        services.AddLogging();
        services.AddSignalR();
        services
            .AddControllers()
            .AddApplicationPart(typeof(AuthController).Assembly)
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.Converters.Add(new UlidGuidJsonConverter());
            });
        services.Configure<MvcOptions>(o =>
            o.ModelBinderProviders.Insert(0, new UlidGuidModelBinderProvider())
        );
        services.Configure<RouteOptions>(o =>
            o.ConstraintMap["guid"] = typeof(UlidOrGuidRouteConstraint)
        );
        services.AddSingleton<
            Asp.Versioning.IApiVersionReader,
            Asp.Versioning.QueryStringApiVersionReader
        >();
        services
            .AddApiVersioning(o =>
            {
                o.DefaultApiVersion = new(1, 0);
                o.AssumeDefaultVersionWhenUnspecified = true;
            })
            .AddMvc();

        // Identity: the production bearer pipeline over the real token service.
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenService>(jwt);
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o => DashboardJwtBearer.Configure(o, jwt.GetValidationParameters()));
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, ActionAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, ActionAuthorizationHandler>();
        services.AddScoped<IAuthorizationHandler, PlatformIamAuthorizationHandler>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentTenantService, CurrentTenantService>();
        services.AddSingleton<ICacheService>(
            new MemoryCacheService(
                new MemoryCache(new MemoryCacheOptions()),
                NullLogger<MemoryCacheService>.Instance
            )
        );
        services.AddSingleton<IMemoryCache>(new MemoryCache(new MemoryCacheOptions()));
        services.AddSingleton<ISessionRevocationService, SessionRevocationService>();

        // Persistence: the real context, tenant filters included.
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Everything that answers "what does THIS caller see".
        services.AddScoped<ISessionService, SessionService>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IChannelService, ChannelService>();
        services.AddScoped<IChannelAccessService, ChannelAccessService>();
        services.AddScoped<IRoleResolver, RoleResolver>();
        services.AddScoped<IActAsMembershipOverlay, ActAsMembershipOverlay>();
        services.AddScoped<IMembershipService, MembershipService>();
        services.AddScoped<IActionAuthorizationService, ActionAuthorizationService>();
        services.AddScoped<IActionRequiredInboxService, ActionRequiredInboxService>();
        services.AddScoped<ITenantMemberDirectoryService, TenantMemberDirectoryService>();
        services.AddScoped<IImpersonationSessionService, ImpersonationSessionService>();
        services.AddScoped<IPlatformAdminService, PlatformAdminService>();
        services.AddSingleton(authService);
        services.AddSingleton<IEnumerable<IActionRequiredSource>>([new ChannelInboxProbe()]);

        // The IAM permission store: the admin operator holds every Plane-C permission; nobody else is a
        // principal. The real PlatformIamAuthorizationHandler still demands the `admin` role FIRST.
        IPlatformIamService iam = Substitute.For<IPlatformIamService>();
        iam.ResolvePrincipalAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
                Result.Success<IamPrincipalDto?>(
                    ci.ArgAt<Guid>(0) == adminUserId
                        ? new(
                            AdminPrincipalId,
                            IamPrincipalType.Employee,
                            adminUserId,
                            "ops",
                            true,
                            null
                        )
                        : null
                )
            );
        iam.IsSaasDeploymentAsync(Arg.Any<CancellationToken>()).Returns(true);
        iam.AuthorizePlatformAsync(
                AdminPrincipalId,
                Arg.Any<string>(),
                Arg.Any<Guid?>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<CancellationToken>(),
                Arg.Any<string?>()
            )
            .Returns(Result.Success(true));
        services.AddSingleton(iam);
        IIamCallerPrincipalResolverService principals =
            Substitute.For<IIamCallerPrincipalResolverService>();
        principals
            .ResolveActingPrincipalIdAsync(adminUserId.ToString(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(AdminPrincipalId));
        services.AddSingleton(principals);

        // The outside world (Twitch's moderators API is registered by StartAsync, which keeps it for the tests).
        services.AddSingleton(eventBus);
        services.AddSingleton(Substitute.For<IChannelRegistry>());
        services.AddSingleton(Substitute.For<ITwitchEventSubService>());
        services.AddSingleton(Substitute.For<IChatProvider>());
        services.AddSingleton(Substitute.For<IBuiltinResponseComposer>());
        services.AddSingleton(Substitute.For<IChannelDeletePreviewService>());
        services.AddSingleton(Substitute.For<IDatabaseMigrator>());
        services.AddSingleton(Substitute.For<IUserIdentityService>());
        services.AddSingleton(Substitute.For<IActionRequiredChangeNotifier>());
        services.AddSingleton(Substitute.For<IOperatorChatSender>());
        services.AddSingleton(Substitute.For<ITwitchOAuthStateService>());
        services.AddSingleton(Substitute.For<ILoginProviderRegistry>());
        services.AddSingleton(Substitute.For<IExternalLoginService>());
        services.AddSingleton(Substitute.For<ISystemCredentialsProvider>());
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Extensions;
using NomNomzBot.Api.Tests.Controllers;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Caching;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Builtin;
using NomNomzBot.Application.Common.Interfaces;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Authorization;
using NomNomzBot.Application.Contracts.Security;
using NomNomzBot.Application.Contracts.Twitch;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Enums.Deployment;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure;
using NomNomzBot.Infrastructure.Identity;
using NomNomzBot.Infrastructure.Platform.Auth;
using NomNomzBot.Infrastructure.Platform.Caching;
using NomNomzBot.Infrastructure.Platform.Deployment;
using NomNomzBot.Infrastructure.Platform.Persistence;
using NomNomzBot.Infrastructure.Platform.Security;
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
        ITwitchModeratorsApi moderators,
        IOwnerActionService ownerActions
    )
    {
        _host = host;
        _connection = connection;
        EventBus = eventBus;
        Moderators = moderators;
        OwnerActions = ownerActions;
    }

    public TestServer Server => _host.GetTestServer();
    public HttpClient Client { get; private set; } = null!;
    public IEventBus EventBus { get; }

    /// <summary>Twitch's "channels I moderate" answer. Offline (a failure) unless a test says otherwise.</summary>
    public ITwitchModeratorsApi Moderators { get; }

    /// <summary>The owner-action executor the widget preview endpoint calls; a substitute, so a test sees what reached it.</summary>
    public IOwnerActionService OwnerActions { get; }

    /// <summary>The IAM principal the admin operator acts through.</summary>
    public static readonly Guid AdminPrincipalId = Guid.Parse(
        "0192b000-0000-7000-8000-00000000a0a0"
    );

    public static async Task<ActAsTestHost> StartAsync(
        Guid adminUserId,
        Action<IServiceCollection>? configureServices = null
    )
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
        ITwitchModeratorsApi moderators = Substitute.For<ITwitchModeratorsApi>();
        IOwnerActionService ownerActions = Substitute.For<IOwnerActionService>();
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
                    Register(services, config, jwt, eventBus, adminUserId, connection);
                    configureServices?.Invoke(services);
                    services.AddSingleton(moderators);
                    services.AddSingleton(ownerActions);
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseApiIdentity();
                    app.UseEndpoints(endpoints =>
                    {
                        endpoints.MapControllers();
                        endpoints.MapApiHubs();
                    });
                })
        );

        IHost host = await builder.StartAsync();

        await using (AsyncServiceScope scope = host.Services.CreateAsyncScope())
            await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .Database.EnsureCreatedAsync();

        ActAsTestHost testHost = new(host, connection, eventBus, moderators, ownerActions);
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

    private static void Register(
        IServiceCollection services,
        IConfiguration config,
        JwtTokenService jwt,
        IEventBus eventBus,
        Guid adminUserId,
        SqliteConnection connection
    )
    {
        services.AddRouting();
        services.AddLogging();
        services.AddHttpClient();
        services.AddApiSignalR(enableDetailedErrors: true);
        services.AddApiControllers().AddApplicationPart(typeof(AuthController).Assembly);

        // Identity: the production bearer pipeline over the real token service.
        services.AddSingleton(config);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IJwtTokenService>(jwt);
        services.AddApiIdentity(jwt.GetValidationParameters());
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

        // Persistence: the real context, tenant filters included.
        services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        // Everything that answers "what does THIS caller see": the production service bindings (the same
        // convention scan the deployed API runs), then the few production singletons the scan leaves out.
        services.AddInfrastructureServicesByConvention();
        services.AddSingleton<ISessionRevocationService, SessionRevocationService>();
        services.AddSingleton<IOutboundSanctionAccessor, OutboundSanctionAccessor>();
        services.AddSingleton<TwitchScopeRegistry>();
        services.AddSingleton(TestUnsubscribedTier.Default());
        services.AddSingleton(new DeploymentContext(DeploymentMode.SelfHostLite));
        services.AddScoped<IRoleResolver, RoleResolver>();
        services.AddScoped<IActAsMembershipOverlay, ActAsMembershipOverlay>();
        services.AddScoped<IViewAsScope, HttpViewAsScope>();
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
        services.AddSingleton(Substitute.For<IBotModeratorStatusService>());
        services.AddSingleton(Substitute.For<IChannelDeletePreviewService>());
        services.AddSingleton(Substitute.For<IDatabaseMigrator>());
        services.AddSingleton(Substitute.For<IUserIdentityService>());
        services.AddSingleton(Substitute.For<IActionRequiredChangeNotifier>());
        services.AddSingleton(Substitute.For<IOperatorChatSender>());
        services.AddSingleton(Substitute.For<IExternalChatRelay>());
        services.AddSingleton(Substitute.For<ITwitchOAuthStateService>());
        services.AddSingleton(Substitute.For<ILoginProviderRegistry>());
        services.AddSingleton(Substitute.For<IExternalLoginService>());
        services.AddSingleton(Substitute.For<ISystemCredentialsProvider>());
        services.AddSingleton(Substitute.For<ISupportContactService>());
        services.AddSingleton(Substitute.For<ITwitchAuthService>());
        services.AddSingleton(Substitute.For<ITwitchDeviceCodeService>());
        services.AddSingleton(Substitute.For<IIntegrationTokenVault>());
        services.AddSingleton(Substitute.For<IPlatformOwnerPrincipalMinter>());
    }
}

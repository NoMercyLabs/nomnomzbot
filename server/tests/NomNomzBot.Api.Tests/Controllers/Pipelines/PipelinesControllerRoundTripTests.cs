// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Identifiers;
using NomNomzBot.Application.Abstractions.Localization;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Domain.Commands.Entities;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.Platform.Pipeline;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers.Pipelines;

/// <summary>
/// S-PIPE-BLANK-b: an HTTP-level round-trip through the REAL <see cref="PipelinesController"/> — real MVC
/// routing, real System.Text.Json model binding/serialization (including the production
/// <see cref="UlidGuidJsonConverter"/>), and the real <see cref="PipelineService"/> writing into a real
/// (in-memory) <see cref="IApplicationDbContext"/>. The prior proof of "create/update write BOTH
/// GraphJsonCache and the normalized PipelineStep/PipelineStepCondition rows" (S-PIPE-WRITE-SYMMETRY,
/// <c>PipelineServiceLegacyStepsTests</c>) is unit-level only — it calls <see cref="PipelineService"/>
/// directly with a hand-built DTO, never through the controller's JSON body binding. A future rename of
/// <see cref="NomNomzBot.Application.Commands.Dtos.CreatePipelineDto.GraphJsonCache"/>'s wire name (the
/// exact class of bug that caused UNRECOVERABLE loss of dashboard-saved pipelines: a wire-name mismatch
/// meant CreateAsync/UpdateAsync silently wrote nothing) would sail through that unit test untouched, because
/// unit tests construct the DTO in code, bypassing JSON binding entirely. This test posts a raw JSON HTTP
/// body — the same shape the dashboard sends — through the real binder, so it dies for exactly that reason
/// if the wire name ever drifts.
/// </summary>
public sealed class PipelinesControllerRoundTripTests : IAsyncDisposable
{
    private const string ChannelId = "0192a000-0000-7000-8000-00000000c1a1";

    private IHost? _host;
    private PipelineGraphRoundTripDbContext? _db;

    private async Task<HttpClient> StartAsync()
    {
        PipelineGraphRoundTripDbContext db = PipelineGraphRoundTripDbContext.New();
        _db = db;

        List<ICommandAction> actions =
        [
            new FakeAction { ActionType = "send_message" },
            new FakeAction { ActionType = "timeout_user" },
        ];

        IHostBuilder builder = new HostBuilder().ConfigureWebHost(webHost =>
        {
            webHost
                .UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();

                    services
                        .AddControllers()
                        .AddApplicationPart(typeof(PipelinesController).Assembly)
                        .AddJsonOptions(o =>
                        {
                            o.JsonSerializerOptions.PropertyNamingPolicy =
                                JsonNamingPolicy.CamelCase;
                            o.JsonSerializerOptions.DefaultIgnoreCondition = System
                                .Text
                                .Json
                                .Serialization
                                .JsonIgnoreCondition
                                .WhenWritingNull;
                            o.JsonSerializerOptions.Converters.Add(new UlidGuidJsonConverter());
                        });

                    // Same ULID-or-Guid route binding as production (Program.cs), so a pipeline id round-trips
                    // through the {id:guid} route the same way the real dashboard's GET does.
                    services.Configure<Microsoft.AspNetCore.Mvc.MvcOptions>(o =>
                        o.ModelBinderProviders.Insert(0, new UlidGuidModelBinderProvider())
                    );
                    services.Configure<RouteOptions>(o =>
                        o.ConstraintMap["guid"] = typeof(UlidOrGuidRouteConstraint)
                    );

                    // Auth: a real [Authorize]/[RequireAction] gate is Gate-1/Gate-2 tenant+role enforcement,
                    // already covered elsewhere — this slice proves the wire seam, so authentication/authorization
                    // are stubbed to always admit a signed-in caller rather than re-building the full IAM stack.
                    services
                        .AddAuthentication("Test")
                        .AddScheme<AuthenticationSchemeOptions, AlwaysAuthenticatedHandler>(
                            "Test",
                            _ => { }
                        );
                    services.AddAuthorization();
                    services.AddSingleton<
                        IAuthorizationPolicyProvider,
                        AlwaysAllowPolicyProvider
                    >();

                    services.AddSingleton<IApplicationDbContext>(db);
                    services.AddSingleton<IUnitOfWork, PassThroughUnitOfWorkDouble>();
                    services.AddSingleton<IEventBus>(Substitute.For<IEventBus>());
                    services.AddSingleton<IChannelRegistry>(Substitute.For<IChannelRegistry>());
                    services.AddSingleton<IEnumerable<ICommandAction>>(actions);
                    services.AddSingleton<IEnumerable<ICommandCondition>>([]);
                    services.AddSingleton<
                        Application.Abstractions.Templating.ITemplateHelperValidator,
                        Infrastructure.Platform.Templating.TemplateHelperValidator
                    >();
                    services.AddSingleton<ICommandConfigValidator, CommandConfigValidator>();
                    services.AddSingleton<IPipelineService, PipelineService>();
                    services.AddSingleton(Substitute.For<IPipelineTestRunService>());

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
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                });
        });

        IHost host = await builder.StartAsync();
        _host = host;
        return host.GetTestClient();
    }

    private sealed class FakeAction : ICommandAction
    {
        public required string ActionType { get; init; }

        public LocalizedText Category => new("pipeline.category.test_fixture");
        public LocalizedText Description => new("pipeline.test_fixture.description");

        public Task<ActionResult> ExecuteAsync(
            PipelineExecutionContext ctx,
            ActionDefinition action
        ) => Task.FromResult(ActionResult.Success());
    }

    private sealed class AlwaysAuthenticatedHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder
    ) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            ClaimsIdentity identity = new(
                [new(ClaimTypes.NameIdentifier, "0192a000-0000-7000-8000-00000000d00d")],
                "Test"
            );
            AuthenticationTicket ticket = new(new(identity), "Test");
            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    private sealed class AlwaysAllowPolicyProvider : IAuthorizationPolicyProvider
    {
        private static readonly AuthorizationPolicy Permissive = new AuthorizationPolicyBuilder()
            .RequireAssertion(_ => true)
            .Build();

        public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => Task.FromResult(Permissive);

        public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
            Task.FromResult<AuthorizationPolicy?>(Permissive);

        public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName) =>
            Task.FromResult<AuthorizationPolicy?>(Permissive);
    }

    private static StringContent JsonBody(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private const string CreateGraphJson =
        /*lang=json,strict*/
        """
            {
              "name": "Raid Greeting",
              "description": "S-PIPE-BLANK-b round trip",
              "isEnabled": true,
              "triggerKind": "manual",
              "graph": {
                "steps": [
                  {
                    "action": { "type": "send_message", "message": "Welcome {{user.name}}!" }
                  },
                  {
                    "action": { "type": "timeout_user", "seconds": 60 },
                    "condition": {
                      "type": "user_role",
                      "operator": "eq",
                      "left": "role",
                      "right": "moderator",
                      "negate": false
                    }
                  }
                ]
              }
            }
            """;

    [Fact]
    public async Task CreateThenGet_RoundTripsEveryStepTypeOrderFieldAndCondition_ThroughRealHttpBinding()
    {
        HttpClient client = await StartAsync();

        HttpResponseMessage createResponse = await client.PostAsync(
            $"/api/v1/channels/{ChannelId}/pipelines",
            JsonBody(CreateGraphJson)
        );

        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);

        string createBody = await createResponse.Content.ReadAsStringAsync();
        using JsonDocument createDoc = JsonDocument.Parse(createBody);
        JsonElement createData = createDoc.RootElement.GetProperty("data");

        string encodedId = createData.GetProperty("id").GetString()!;
        AssertGraphMatchesPostedShape(createData.GetProperty("graph"));

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/v1/channels/{ChannelId}/pipelines/{encodedId}"
        );
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        string getBody = await getResponse.Content.ReadAsStringAsync();
        using JsonDocument getDoc = JsonDocument.Parse(getBody);
        JsonElement getData = getDoc.RootElement.GetProperty("data");

        getData.GetProperty("name").GetString().Should().Be("Raid Greeting");
        AssertGraphMatchesPostedShape(getData.GetProperty("graph"));

        // The wire response is only half the proof — S-PIPE-WRITE-SYMMETRY's whole point is that the
        // normalized PipelineStep/PipelineStepCondition rows (what PipelineEngine actually executes) are
        // ALSO populated, not just the GraphJsonCache the DTO happens to echo back.
        Guid pipelineId = GuidUlidCodec.TryDecode(encodedId, out Guid decoded)
            ? decoded
            : Guid.Parse(encodedId);

        List<PipelineStep> persistedSteps =
        [
            .. _db!
                .PipelineSteps.Where(s => s.PipelineId == pipelineId)
                .OrderBy(s => s.Order)
                .Select(s => s),
        ];
        persistedSteps.Should().HaveCount(2);
        persistedSteps[0].ActionType.Should().Be("send_message");
        persistedSteps[0].Order.Should().Be(0);
        persistedSteps[0].ConfigJson.Should().Contain("Welcome {{user.name}}!");
        persistedSteps[1].ActionType.Should().Be("timeout_user");
        persistedSteps[1].Order.Should().Be(1);
        persistedSteps[1].ConfigJson.Should().Contain("60");

        List<PipelineStepCondition> persistedConditions =
        [
            .. _db.PipelineStepConditions.Where(c => c.PipelineStepId == persistedSteps[1].Id),
        ];
        persistedConditions.Should().ContainSingle();
        persistedConditions[0].ConditionType.Should().Be("user_role");
        persistedConditions[0].Operator.Should().Be("eq");
        persistedConditions[0].LeftOperand.Should().Be("role");
        persistedConditions[0].RightOperand.Should().Be("moderator");
        persistedConditions[0].Negate.Should().BeFalse();

        List<PipelineStepCondition> conditionsOnFirstStep =
        [
            .. _db.PipelineStepConditions.Where(c => c.PipelineStepId == persistedSteps[0].Id),
        ];
        conditionsOnFirstStep.Should().BeEmpty("the first step was posted with no condition");
    }

    private static void AssertGraphMatchesPostedShape(JsonElement graph)
    {
        JsonElement steps = graph.GetProperty("steps");
        steps.GetArrayLength().Should().Be(2);

        JsonElement step0 = steps[0];
        step0.GetProperty("action").GetProperty("type").GetString().Should().Be("send_message");
        step0
            .GetProperty("action")
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Welcome {{user.name}}!");
        bool hasCondition = step0.TryGetProperty("condition", out JsonElement step0Condition);
        (!hasCondition || step0Condition.ValueKind is JsonValueKind.Null)
            .Should()
            .BeTrue("the first step was posted with no condition");

        JsonElement step1 = steps[1];
        step1.GetProperty("action").GetProperty("type").GetString().Should().Be("timeout_user");
        step1.GetProperty("action").GetProperty("seconds").GetInt32().Should().Be(60);

        JsonElement condition = step1.GetProperty("condition");
        condition.GetProperty("type").GetString().Should().Be("user_role");
        condition.GetProperty("operator").GetString().Should().Be("eq");
        condition.GetProperty("left").GetString().Should().Be("role");
        condition.GetProperty("right").GetString().Should().Be("moderator");
        condition.GetProperty("negate").GetBoolean().Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }
}

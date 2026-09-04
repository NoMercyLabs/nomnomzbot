// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Newtonsoft.Json.Linq;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Abstractions.Persistence;
using NomNomzBot.Application.Abstractions.Pipeline;
using NomNomzBot.Application.Assets.Services;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Analytics;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.CustomEvents.Services;
using NomNomzBot.Application.Economy.Services;
using NomNomzBot.Application.Marketplace.Services;
using NomNomzBot.Application.Music.Services;
using NomNomzBot.Application.PickLists.Services;
using NomNomzBot.Application.Rewards.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Application.Widgets.Dtos;
using NomNomzBot.Application.Widgets.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Domain.Chat.ValueObjects;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Domain.CustomCode.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Infrastructure.Commands;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Marketplace;
using NomNomzBot.Infrastructure.Marketplace.FirstPartyBundles;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tests.Persistence;
using NSubstitute;
using PipelineEntity = NomNomzBot.Domain.Commands.Entities.Pipeline;

namespace NomNomzBot.Infrastructure.Tests.Marketplace;

/// <summary>
/// SLICE S-FEATHER — proves the "Lucky Feather" preset (the chest-steal game composed of existing generic
/// primitives: <c>IScriptStorageService</c>, a <c>run_code</c> script, <c>IScheduledPipelineService</c>, and a
/// Vue widget) round-trips through the SAME generic bundle import surface every other preset uses, and that
/// running its content changes real persisted state — not merely "the pipeline returned success".
/// </summary>
public sealed class LuckyFeatherBundleTests
{
    private static readonly Guid Channel = Guid.Parse("0192f000-0000-7000-8000-00000000f001");
    private static readonly Guid Actor = Guid.Parse("0192f000-0000-7000-8000-00000000f0aa");
    private static readonly Guid Thief = Guid.Parse("0192f000-0000-7000-8000-00000000f0bb");
    private static readonly DateTimeOffset Start = new(2026, 6, 22, 12, 0, 0, TimeSpan.Zero);

    private sealed record Harness(
        AuthDbContext Db,
        BundleImportService Import,
        IWidgetService Widgets,
        FakeTimeProvider Clock,
        IScheduledPipelineService Scheduler,
        IScriptStorageService Storage,
        IPipelineEngine Engine
    );

    private static Harness Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        db.Users.Add(
            new()
            {
                Id = Thief,
                Username = "featherthief",
                UsernameNormalized = "FEATHERTHIEF",
                DisplayName = "FeatherThief",
                TwitchUserId = "555920",
                ProfileImageUrl = "https://cdn.twitch.tv/featherthief.png",
            }
        );
        db.SaveChanges();

        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);

        // Real pipeline + code-script services (the two module surfaces the bundle actually installs into) —
        // everything else the generic BundleImportService constructor needs but this bundle never touches is
        // substituted, exactly like the parity round-trip tests.
        ICommandConfigValidator permissiveValidator = Substitute.For<ICommandConfigValidator>();
        permissiveValidator
            .ValidatePipelineAsync(Arg.Any<PipelineGraphInput>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(PipelineValidationResult.Valid()));
        PipelineService pipelines = new(
            db,
            new PassThroughUnitOfWork(),
            Substitute.For<IEventBus>(),
            permissiveValidator,
            Substitute.For<IChannelRegistry>()
        );

        IWidgetDependencyAllowlist allowlist = Substitute.For<IWidgetDependencyAllowlist>();
        allowlist.IsAllowed(Arg.Any<string>()).Returns(true);
        CodeScriptService codeScripts = new(
            db,
            tenant,
            new JintScriptExecutor(),
            Substitute.For<IEventBus>(),
            TimeProvider.System,
            allowlist
        );

        // A stateful IWidgetService stand-in: CreateAsync records the widget so ListAsync/GetAsync can serve
        // it back — the steal script's widget.emit call resolves this same "Lucky Feather" widget by name
        // (ScriptHostBridge.ResolveWidget), so it must actually be findable, not just accepted at create time.
        List<WidgetDetail> createdWidgets = [];
        IWidgetService widgets = Substitute.For<IWidgetService>();
        widgets
            .CreateAsync(
                Channel.ToString(),
                Arg.Any<CreateWidgetRequest>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                WidgetDetail created = new(
                    Id: Guid.NewGuid(),
                    Name: call.Arg<CreateWidgetRequest>().Name,
                    Description: call.Arg<CreateWidgetRequest>().Description,
                    Framework: call.Arg<CreateWidgetRequest>().Framework,
                    Source: string.Empty,
                    IsEnabled: true,
                    OverlayUrl: null,
                    ActiveVersionId: null,
                    GalleryItemId: null,
                    Settings: call.Arg<CreateWidgetRequest>().Settings ?? [],
                    EventSubscriptions: call.Arg<CreateWidgetRequest>().EventSubscriptions ?? [],
                    LastRuntimeError: null,
                    LastRanAt: null,
                    CreatedAt: Start.UtcDateTime,
                    UpdatedAt: Start.UtcDateTime,
                    GalleryUpdateAvailable: false,
                    IsAttached: false
                );
                createdWidgets.Add(created);
                return Result.Success(created);
            });
        widgets
            .ListAsync(
                Channel.ToString(),
                Arg.Any<PaginationParams>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
                Result.Success(
                    new PagedList<WidgetDetail>(createdWidgets, 1, 100, createdWidgets.Count)
                )
            );

        BundleImportService import = new(
            db,
            Substitute.For<ICommandService>(),
            pipelines,
            widgets,
            Substitute.For<ISoundClipService>(),
            Substitute.For<IChannelAssetService>(),
            Substitute.For<ICustomDataSourceService>(),
            Substitute.For<IEventResponseService>(),
            Substitute.For<IRewardService>(),
            Substitute.For<ITimerManagementService>(),
            Substitute.For<IChatTriggerService>(),
            Substitute.For<IPickListService>(),
            codeScripts,
            tenant,
            Substitute.For<IEventBus>()
        );

        FakeTimeProvider clock = new(Start);
        IPipelineEngine engine = Substitute.For<IPipelineEngine>();
        // ScheduledPipelineService resolves IPipelineEngine through a fresh DI scope per dispatch (mirroring
        // ScheduledPipelineExpiryServiceTests' own harness), so a minimal service collection stands in for
        // the app's real container.
        ServiceCollection services = new();
        services.AddSingleton<IApplicationDbContext>(db);
        services.AddSingleton(engine);
        services.AddSingleton<ILogger<ScheduledPipelineService>>(
            NullLogger<ScheduledPipelineService>.Instance
        );
        ServiceProvider provider = services.BuildServiceProvider();
        IScheduledPipelineService scheduler = new ScheduledPipelineService(
            db,
            provider.GetRequiredService<IServiceScopeFactory>(),
            clock,
            NullLogger<ScheduledPipelineService>.Instance
        );
        IScriptStorageService storage = new ScriptStorageService(db);

        return new(db, import, widgets, clock, scheduler, storage, engine);
    }

    // The real ScriptRunner (JintScriptExecutor + real ScriptHostBridgeFactory over the real storage +
    // scheduler + a stubbed 7TV paint resolver) — proves the SAME executor the pipeline engine's `run_code`
    // action would call actually runs the bundled JS and touches persisted state.
    private static ScriptRunner BuildRunner(Harness h, ISevenTvUserPaintResolver? paint = null)
    {
        IScriptCapabilityBroker broker = Substitute.For<IScriptCapabilityBroker>();
        broker
            .BuildGrantAsync(
                Arg.Any<Guid>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(ci =>
                // Grant exactly what the script's own declared capabilities ask for — the real broker
                // validates declared keys against the catalogue/feature-flag gates; this permissive stand-in
                // just proves the bundle's scripts don't call anything they never declared.
                Result.Success(
                    new ScriptCapabilityGrant(
                        Channel,
                        [
                            .. ci.Arg<IReadOnlyList<string>>()
                                .Select(key => new ScriptCapabilityDescriptor(
                                    key,
                                    "low",
                                    "",
                                    true
                                )),
                        ]
                    )
                )
            );
        IScriptExecutionMeter meter = Substitute.For<IScriptExecutionMeter>();
        meter
            .CheckSandboxBudgetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Success(new QuotaCheck(true, -1, 0, default, default)));
        meter
            .RecordSandboxUsageAsync(
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Result.Success());

        ScriptHostBridgeFactory bridgeFactory = new(
            Substitute.For<IChatProvider>(),
            Substitute.For<ICurrencyAccountService>(),
            Substitute.For<IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            h.Storage,
            Substitute.For<ITtsDispatchService>(),
            h.Widgets,
            Substitute.For<IWidgetEventNotifier>(),
            Substitute.For<IRewardService>(),
            Substitute.For<IViewerAnalyticsService>(),
            Substitute.For<ITtsConfigService>(),
            h.Scheduler,
            h.Db,
            paint ?? Substitute.For<ISevenTvUserPaintResolver>()
        );

        return new(h.Db, new JintScriptExecutor(), broker, meter, bridgeFactory, h.Clock);
    }

    private static async Task<Guid> ImportAsync(Harness h)
    {
        await using System.IO.Stream zip = await LuckyFeatherBundle.BuildZipAsync();
        Result<InstalledBundleDto> installed = await h.Import.ImportAsync(
            Channel,
            Actor,
            zip,
            ImportConflictPolicy.Rename
        );
        installed.IsSuccess.Should().BeTrue(installed.ErrorMessage);
        return installed.Value.Id;
    }

    [Fact]
    public async Task Importing_the_bundle_creates_both_scripts_disabled_and_both_pipelines_disabled()
    {
        Harness h = Build();

        await ImportAsync(h);

        List<CodeScript> scripts = h
            .Db.CodeScripts.Where(s => s.BroadcasterId == Channel)
            .OrderBy(s => s.Name)
            .ToList();
        scripts.Should().HaveCount(2);
        scripts
            .Select(s => s.Name)
            .Should()
            .BeEquivalentTo([
                LuckyFeatherBundle.StealScriptName,
                LuckyFeatherBundle.ExpiryScriptName,
            ]);
        // D4: imported custom code always lands disabled — an explicit owner action is required to run it.
        scripts.Should().OnlyContain(s => !s.IsEnabled);

        List<PipelineEntity> pipelines = h
            .Db.Pipelines.Where(p => p.BroadcasterId == Channel)
            .OrderBy(p => p.Name)
            .ToList();
        pipelines.Should().HaveCount(2);
        pipelines
            .Select(p => p.Name)
            .Should()
            .BeEquivalentTo([
                LuckyFeatherBundle.StealPipelineName,
                LuckyFeatherBundle.ExpiryPipelineName,
            ]);
        // D4: a run_code-bearing pipeline always lands disabled too, whatever the export said.
        pipelines.Should().OnlyContain(p => !p.IsEnabled);

        await h
            .Widgets.Received(1)
            .CreateAsync(
                Channel.ToString(),
                Arg.Is<CreateWidgetRequest>(r =>
                    r.Name == LuckyFeatherBundle.WidgetName && r.Framework == "vue"
                ),
                Arg.Any<CancellationToken>()
            );
        await h
            .Widgets.Received(1)
            .CompileAsync(
                Channel.ToString(),
                Arg.Any<string>(),
                Arg.Is<CompileWidgetRequest>(r => !string.IsNullOrWhiteSpace(r.SourceCode)),
                Arg.Any<CancellationToken>()
            );
    }

    [Fact]
    public async Task Running_the_steal_script_persists_the_new_holder_and_schedules_the_auto_hide()
    {
        Harness h = Build();
        await ImportAsync(h);
        Guid stealScriptId = h
            .Db.CodeScripts.Single(s =>
                s.BroadcasterId == Channel && s.Name == LuckyFeatherBundle.StealScriptName
            )
            .Id;
        // D4 lands the script AND the expiry pipeline (it carries a run_code step) disabled — enable both
        // the same way an owner would before the game goes live, so the steal script's schedule.pipeline
        // call can actually resolve the expiry pipeline by name.
        h.Db.CodeScripts.Single(s => s.Id == stealScriptId).IsEnabled = true;
        h
            .Db.Pipelines.Single(p =>
                p.BroadcasterId == Channel && p.Name == LuckyFeatherBundle.ExpiryPipelineName
            )
            .IsEnabled = true;
        await h.Db.SaveChangesAsync();

        ScriptRunner runner = BuildRunner(h);
        ScriptInvocation invocation = new(
            "exec-1",
            Thief.ToString(),
            "FeatherThief",
            [],
            new Dictionary<string, string>()
        );

        // Warm the Jint engine once (cold-start JIT would otherwise eat into the 2s production wall-clock
        // budget on a loaded CI box) — the SAME deflake ScriptRunnerTests uses; the asserted run below hits a
        // hot engine.
        await runner.RunAsync(stealScriptId, invocation);
        Result<ScriptRunResult> result = await runner.RunAsync(stealScriptId, invocation);

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        result.Value.Outcome.Should().Be(ScriptExecutionOutcome.Success);

        // The persisted state actually changed — the stored holder is now the trigger user, not a claim that
        // the script "ran successfully".
        string? storedRaw = await h.Storage.GetAsync(Channel, LuckyFeatherBundle.HolderStorageKey);
        storedRaw.Should().NotBeNull();
        JObject holder = JObject.Parse(storedRaw!);
        holder["id"]!.Value<string>().Should().Be(Thief.ToString());
        holder["displayName"]!.Value<string>().Should().Be("FeatherThief");

        // The expiry pipeline was actually scheduled through the real IScheduledPipelineService — not merely
        // that the call "returned ok" to the script, but that a due ScheduledPipelineTask row now exists,
        // bound to THIS channel's Lucky Feather Expiry pipeline.
        Guid expiryPipelineId = h
            .Db.Pipelines.Single(p =>
                p.BroadcasterId == Channel && p.Name == LuckyFeatherBundle.ExpiryPipelineName
            )
            .Id;
        List<NomNomzBot.Domain.Commands.Entities.ScheduledPipelineTask> pending = h
            .Db.ScheduledPipelineTasks.Where(t => t.BroadcasterId == Channel)
            .ToList();
        pending.Should().ContainSingle(t => t.PipelineId == expiryPipelineId);
    }

    [Fact]
    public async Task Stealing_from_a_holder_who_wears_a_7tv_paint_carries_it_onto_the_new_holder()
    {
        // Proves piece 4 end to end: the SCRIPT (not a central broadcast handler) enriches the widget.emit
        // payload using ScriptHostBridge's folded user.get().paint field.
        Harness h = Build();
        await ImportAsync(h);
        Guid stealScriptId = h
            .Db.CodeScripts.Single(s =>
                s.BroadcasterId == Channel && s.Name == LuckyFeatherBundle.StealScriptName
            )
            .Id;
        h.Db.CodeScripts.Single(s => s.Id == stealScriptId).IsEnabled = true;
        await h.Db.SaveChangesAsync();

        ChatPaint paint = new()
        {
            Id = "01JEY00EDNVW20AWX2NPG4HTNF",
            Name = "Sora's Image Paint",
            BackgroundImage = "url(https://cdn.7tv.app/paint/sora.png)",
            Color = "#ff66aa",
            TextShadow = null,
            IsImageOnly = true,
        };
        ISevenTvUserPaintResolver resolver = Substitute.For<ISevenTvUserPaintResolver>();
        resolver.ResolveAsync("555920", Arg.Any<CancellationToken>()).Returns(paint);

        ScriptRunner runner = BuildRunner(h, resolver);
        ScriptInvocation invocation = new(
            "exec-1",
            Thief.ToString(),
            "FeatherThief",
            [],
            new Dictionary<string, string>()
        );
        await runner.RunAsync(stealScriptId, invocation); // warm-up
        await runner.RunAsync(stealScriptId, invocation);

        string? storedRaw = await h.Storage.GetAsync(Channel, LuckyFeatherBundle.HolderStorageKey);
        JObject holder = JObject.Parse(storedRaw!);
        holder["paint"].Should().NotBeNull();
        holder["paint"]!["backgroundImage"]!
            .Value<string>()
            .Should()
            .Be("url(https://cdn.7tv.app/paint/sora.png)");
    }

    [Fact]
    public async Task Firing_the_due_expiry_task_clears_the_stored_holder()
    {
        // The durable side of the primitive: the scheduler fires the due task and dispatches it through the
        // pipeline engine for the Expiry pipeline. Rather than re-testing the run_code<->pipeline-engine
        // wiring itself (RunCodeAction/PipelineEngineTests already cover that generically), the stubbed
        // engine here plays exactly the role RunCodeAction would: on dispatch to the Expiry pipeline, it runs
        // THIS bundle's expiry script for real — proving the schedule -> fire -> script -> persisted-state
        // chain end to end.
        Harness h = Build();
        await ImportAsync(h);
        Guid stealScriptId = h
            .Db.CodeScripts.Single(s =>
                s.BroadcasterId == Channel && s.Name == LuckyFeatherBundle.StealScriptName
            )
            .Id;
        Guid expiryScriptId = h
            .Db.CodeScripts.Single(s =>
                s.BroadcasterId == Channel && s.Name == LuckyFeatherBundle.ExpiryScriptName
            )
            .Id;
        Guid expiryPipelineId = h
            .Db.Pipelines.Single(p =>
                p.BroadcasterId == Channel && p.Name == LuckyFeatherBundle.ExpiryPipelineName
            )
            .Id;
        h.Db.CodeScripts.Single(s => s.Id == stealScriptId).IsEnabled = true;
        h.Db.CodeScripts.Single(s => s.Id == expiryScriptId).IsEnabled = true;
        h.Db.Pipelines.Single(p => p.Id == expiryPipelineId).IsEnabled = true;
        await h.Db.SaveChangesAsync();

        ScriptRunner runner = BuildRunner(h);
        ScriptInvocation invocation = new(
            "exec-1",
            Thief.ToString(),
            "FeatherThief",
            [],
            new Dictionary<string, string>()
        );
        await runner.RunAsync(stealScriptId, invocation); // warm-up
        await runner.RunAsync(stealScriptId, invocation); // steals + schedules the expiry
        (await h.Storage.GetAsync(Channel, LuckyFeatherBundle.HolderStorageKey))
            .Should()
            .NotBeNull("the steal must have persisted a holder before expiry can clear it");

        h.Engine.ExecuteAsync(
                Arg.Is<PipelineRequest>(r =>
                    r.BroadcasterId == Channel && r.PipelineId == expiryPipelineId
                ),
                Arg.Any<CancellationToken>()
            )
            .Returns(async ci =>
            {
                await runner.RunAsync(
                    expiryScriptId,
                    new ScriptInvocation(
                        "exec-2",
                        "0",
                        "system",
                        [],
                        new Dictionary<string, string>()
                    )
                );
                return new PipelineExecutionResult
                {
                    ExecutionId = "exec-2",
                    Outcome = PipelineOutcome.Completed,
                    Duration = TimeSpan.Zero,
                };
            });

        h.Clock.Advance(TimeSpan.FromSeconds(LuckyFeatherBundle.HoldDurationSeconds + 5));
        int fired = await h.Scheduler.FireDueAsync();

        fired.Should().Be(1);
        await h
            .Engine.Received(1)
            .ExecuteAsync(
                Arg.Is<PipelineRequest>(r => r.PipelineId == expiryPipelineId),
                Arg.Any<CancellationToken>()
            );
        (await h.Storage.GetAsync(Channel, LuckyFeatherBundle.HolderStorageKey))
            .Should()
            .BeNull("the expiry script must clear the holder, not merely report success");
    }
}

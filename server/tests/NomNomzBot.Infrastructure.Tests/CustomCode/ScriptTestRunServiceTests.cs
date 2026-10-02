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
using Newtonsoft.Json;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Contracts.Tts;
using NomNomzBot.Application.Platform.Services;
using NomNomzBot.Domain.CustomCode.Entities;
using NomNomzBot.Infrastructure.CustomCode;
using NomNomzBot.Infrastructure.CustomCode.Jint;
using NomNomzBot.Infrastructure.Tests.Identity;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.CustomCode;

/// <summary>
/// Proves the code-script DRY-RUN (custom-code.md §6): the real sandbox runs the real logic, but side-effecting
/// capabilities are CAPTURED (recorded, never dispatched) while reads run live; a throwing script fails without
/// leaving effects; a run-time capability denial still denies; and a captured storage write leaves the store
/// untouched.
/// </summary>
public sealed class ScriptTestRunServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-00000000f001");

    private static (ScriptTestRunService Sut, AuthDbContext Db, ScriptStorageService Storage) Build(
        bool flagsEnabled = true,
        ITtsDispatchService? tts = null
    )
    {
        tts ??= Substitute.For<ITtsDispatchService>();
        AuthDbContext db = AuthTestBuilder.NewContext();
        ICurrentTenantService tenant = Substitute.For<ICurrentTenantService>();
        tenant.BroadcasterId.Returns(Channel);

        IFeatureService features = Substitute.For<IFeatureService>();
        features
            .IsFeatureEnabledAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(flagsEnabled);
        ScriptCapabilityBroker broker = new(features);

        ScriptStorageService storage = new(db);
        ScriptHostBridgeFactory bridgeFactory = new(
            Substitute.For<NomNomzBot.Domain.Chat.Interfaces.IChatProvider>(),
            Substitute.For<NomNomzBot.Application.Economy.Services.ICurrencyAccountService>(),
            Substitute.For<NomNomzBot.Application.Music.Services.IMusicService>(),
            Substitute.For<IHttpClientFactory>(),
            storage,
            tts,
            Substitute.For<NomNomzBot.Application.Widgets.Services.IWidgetService>(),
            Substitute.For<NomNomzBot.Application.Widgets.Services.IWidgetEventNotifier>(),
            Substitute.For<NomNomzBot.Application.Rewards.Services.IRewardService>(),
            Substitute.For<NomNomzBot.Application.Contracts.Analytics.IViewerAnalyticsService>(),
            Substitute.For<NomNomzBot.Application.Tts.Services.ITtsConfigService>(),
            Substitute.For<NomNomzBot.Application.Commands.Services.IScheduledPipelineService>(),
            db,
            Substitute.For<NomNomzBot.Application.Chat.Services.ISevenTvUserPaintResolver>()
        );

        return (new(db, tenant, new JintScriptExecutor(), broker, bridgeFactory, tts), db, storage);
    }

    private static async Task<Guid> SeedAsync(
        AuthDbContext db,
        string js,
        IReadOnlyList<string> declaredCapabilities
    )
    {
        CodeScript script = new()
        {
            BroadcasterId = Channel,
            Name = "s",
            Language = "typescript",
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.CodeScripts.Add(script);
        CodeScriptVersion version = new()
        {
            CodeScriptId = script.Id,
            BroadcasterId = Channel,
            Version = 1,
            SourceCode = js,
            CompiledJs = js,
            CompiledHash = "h",
            ValidationStatus = "valid",
            DeclaredCapabilitiesJson = JsonConvert.SerializeObject(declaredCapabilities),
            CreatedAt = DateTime.UtcNow,
        };
        db.CodeScriptVersions.Add(version);
        script.CurrentVersionId = version.Id;
        await db.SaveChangesAsync();
        return script.Id;
    }

    private static ScriptTestRunRequest Request() => new(new Dictionary<string, string>(), []);

    [Fact]
    public async Task Captures_side_effects_runs_reads_real_and_leaves_the_store_untouched()
    {
        (ScriptTestRunService sut, AuthDbContext db, ScriptStorageService storage) = Build();
        (await storage.SetAsync(Channel, "seeded", "hello")).IsSuccess.Should().BeTrue();

        const string js = """
            var v = nnz.api.storage.get('seeded');
            nnz.api.chat.send('read=' + v);
            nnz.api.storage.set('written', 'x');
            """;
        Guid id = await SeedAsync(db, js, ["storage.get", "chat.send", "storage.set"]);

        await sut.RunAsync(id, Request()); // warm Jint (cold-start deflake, see ScriptRunnerTests)
        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.Success.Should().BeTrue();
        // The real read reached the live store and its value flowed into the (captured) chat text.
        result.ChatOutput.Should().ContainSingle().Which.Should().Be("read=hello");
        // chat.send and storage.set are captured; storage.get (a read) ran real and is NOT an effect.
        result
            .CapturedEffects.Select(e => e.Name)
            .Should()
            .BeEquivalentTo("chat.send", "storage.set");
        result
            .CapturedEffects.Single(e => e.Name == "storage.set")
            .ArgsPreview.Should()
            .Contain("written");

        // The captured write never persisted; the pre-seeded key is intact.
        (await storage.GetAsync(Channel, "written"))
            .Should()
            .BeNull();
        (await storage.GetAsync(Channel, "seeded")).Should().Be("hello");
    }

    [Fact]
    public async Task A_test_run_shows_the_variables_the_script_changed_and_what_it_logged()
    {
        (ScriptTestRunService sut, AuthDbContext db, _) = Build();
        const string js = """
            bot.setVar('mood', 'happy');
            bot.setVar('count', '3');
            bot.setVar('keep', 'same');
            console.log('mood is', bot.getVar('mood'));
            """;
        Guid id = await SeedAsync(db, js, []);
        ScriptTestRunRequest request = new(
            new Dictionary<string, string> { ["mood"] = "calm", ["keep"] = "same" },
            []
        );

        await sut.RunAsync(id, request); // warm Jint
        TestRunResultDto result = (await sut.RunAsync(id, request)).Value;

        result.Success.Should().BeTrue(result.Error);
        result
            .VariablesSet.Should()
            .BeEquivalentTo(new Dictionary<string, string> { ["mood"] = "happy", ["count"] = "3" });
        result.Console.Should().Equal("mood is happy");
    }

    [Fact]
    public async Task A_throwing_script_fails_with_an_error_and_no_effects()
    {
        (ScriptTestRunService sut, AuthDbContext db, _) = Build();
        Guid id = await SeedAsync(db, "throw new Error('boom');", []);

        await sut.RunAsync(id, Request()); // warm Jint
        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.CapturedEffects.Should().BeEmpty();
        result.ChatOutput.Should().BeEmpty();
    }

    [Fact]
    public async Task A_capability_the_grant_did_not_include_is_denied_and_never_captured()
    {
        (ScriptTestRunService sut, AuthDbContext db, _) = Build();
        // The script calls chat.send but declares nothing → the grant is empty → the executor denies at run time.
        Guid id = await SeedAsync(db, "nnz.api.chat.send('hi');", []);

        await sut.RunAsync(id, Request()); // warm Jint
        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("chat.send");
        result.CapturedEffects.Should().BeEmpty();
    }

    [Fact]
    public async Task A_grant_forbidden_by_a_disabled_feature_flag_fails_closed()
    {
        (ScriptTestRunService sut, AuthDbContext db, _) = Build(flagsEnabled: false);
        Guid id = await SeedAsync(db, "nnz.api.chat.send('hi');", ["chat.send"]);

        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        result.CapturedEffects.Should().BeEmpty();
    }

    // A test run speaks nothing, but the script must get the same result shape a live run gets: the voice the
    // channel would really use, the length, and a duration (0, nothing was synthesized). A null voice made
    // `t.voiceId.length` throw only in the preview.
    [Fact]
    public async Task A_captured_tts_speak_returns_the_voice_a_live_run_would_use()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.ResolveVoiceAsync(
                Channel,
                Arg.Any<string>(),
                Arg.Is<string?>(v => v == null),
                Arg.Any<CancellationToken>()
            )
            .Returns("en-GB-Sonia");
        (ScriptTestRunService sut, AuthDbContext db, _) = Build(tts: tts);
        const string js = """
            var t = nnz.api.tts.speak('hello world');
            nnz.api.chat.send(t.voiceId + '|' + t.characterCount + '|' + t.durationMs);
            """;
        Guid id = await SeedAsync(db, js, ["tts.speak", "chat.send"]);

        await sut.RunAsync(id, Request()); // warm Jint
        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.Success.Should().BeTrue();
        result.ChatOutput.Should().ContainSingle().Which.Should().Be("en-GB-Sonia|11|0");
        result.CapturedEffects.Select(e => e.Name).Should().Contain("tts.speak");
        await tts.DidNotReceiveWithAnyArgs().RequestSpeakAsync(default!);
    }

    // A live run refuses a voice the catalogue does not know, and the script sees null. The preview must too.
    [Fact]
    public async Task A_captured_tts_speak_with_an_unknown_voice_returns_null_like_a_live_run()
    {
        ITtsDispatchService tts = Substitute.For<ITtsDispatchService>();
        tts.ResolveVoiceAsync(
                Channel,
                Arg.Any<string>(),
                "no-such-voice",
                Arg.Any<CancellationToken>()
            )
            .Returns((string?)null);
        (ScriptTestRunService sut, AuthDbContext db, _) = Build(tts: tts);
        const string js = """
            var t = nnz.api.tts.speak('hello', 'no-such-voice');
            nnz.api.chat.send(t === null ? 'refused' : 'spoke');
            """;
        Guid id = await SeedAsync(db, js, ["tts.speak", "chat.send"]);

        await sut.RunAsync(id, Request()); // warm Jint
        TestRunResultDto result = (await sut.RunAsync(id, Request())).Value;

        result.ChatOutput.Should().ContainSingle().Which.Should().Be("refused");
    }
}

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
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NomNomzBot.Api.Controllers.V1;
using NomNomzBot.Api.Models;
using NomNomzBot.Application.Abstractions.Auth;
using NomNomzBot.Application.Services;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Identity.Entities;
using NomNomzBot.Domain.Identity.Enums;
using NomNomzBot.Domain.Platform.Interfaces;
using NomNomzBot.Domain.Tts.Entities;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Api.Tests.Controllers;

/// <summary>
/// Proves the viewer self-service <c>PUT /channels/{id}/tts/me/voice</c> route maps the authenticated dashboard
/// User (JWT sub = internal <see cref="Guid"/>) to their PLATFORM external id under the channel's provider, then
/// persists the voice against THAT id — the id the TTS dispatch resolver reads — through the real
/// <see cref="TtsConfigService"/>. A caller with no linked identity on the channel's provider is rejected cleanly,
/// with nothing written.
/// </summary>
public sealed class TtsConfigControllerOwnVoiceTests
{
    private static readonly Guid Broadcaster = Guid.CreateVersion7();
    private static readonly Guid CallerUserId = Guid.CreateVersion7();
    private const string CallerExternalId = "twitch-777";
    private const string VoiceId = "en-US-JennyNeural";

    private static TtsConfigController Build(
        TtsConfigControllerOwnVoiceTestDbContext db,
        Guid? callerUserId
    )
    {
        ICurrentUserService currentUser = Substitute.For<ICurrentUserService>();
        currentUser.UserId.Returns(callerUserId?.ToString());

        TtsConfigService service = new(
            db,
            Substitute.For<ITtsService>(),
            Substitute.For<IEventBus>(),
            Substitute.For<ISubjectKeyService>()
        );

        return new TtsConfigController(
            service,
            Substitute.For<ITtsLexiconService>(),
            db,
            currentUser
        );
    }

    private static void SeedChannel(TtsConfigControllerOwnVoiceTestDbContext db) =>
        db.Channels.Add(
            new Channel
            {
                Id = Broadcaster,
                OwnerUserId = Guid.CreateVersion7(),
                TwitchChannelId = "998877",
                Name = "stoney_eagle",
                NameNormalized = "stoney_eagle",
                Provider = AuthEnums.Platform.Twitch,
            }
        );

    private static void SeedVoiceCatalogue(TtsConfigControllerOwnVoiceTestDbContext db) =>
        db.TtsVoices.Add(
            new TtsVoice
            {
                Id = VoiceId,
                Name = "Jenny",
                DisplayName = "Jenny (US)",
                Locale = "en-US",
                Gender = "female",
                Provider = "edge",
            }
        );

    [Fact]
    public async Task SetOwnVoice_writes_the_voice_against_the_callers_on_provider_external_id()
    {
        TtsConfigControllerOwnVoiceTestDbContext db =
            TtsConfigControllerOwnVoiceTestDbContext.New();
        SeedChannel(db);
        SeedVoiceCatalogue(db);
        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = CallerUserId,
                Provider = AuthEnums.Platform.Twitch,
                ProviderUserId = CallerExternalId,
                ProviderUsername = "caller",
                LinkedAt = DateTime.UtcNow,
            }
        );
        await db.SaveChangesAsync();

        TtsConfigController controller = Build(db, CallerUserId);

        IActionResult result = await controller.SetOwnVoice(
            Broadcaster.ToString(),
            new SetUserVoiceDto { VoiceId = VoiceId },
            CancellationToken.None
        );

        result.Should().BeOfType<OkObjectResult>();

        // The row is keyed on the PLATFORM external id (twitch-777) — NOT the internal User.Id — so the dispatch
        // resolver, which only knows the external id, will actually find and use this voice.
        UserTtsVoice row = await db.UserTtsVoices.SingleAsync();
        row.BroadcasterId.Should().Be(Broadcaster);
        row.UserId.Should().Be(CallerExternalId);
        row.VoiceId.Should().Be(VoiceId);
    }

    [Fact]
    public async Task SetOwnVoice_rejects_a_caller_with_no_identity_on_the_channel_provider_and_writes_nothing()
    {
        TtsConfigControllerOwnVoiceTestDbContext db =
            TtsConfigControllerOwnVoiceTestDbContext.New();
        SeedChannel(db);
        SeedVoiceCatalogue(db);
        // No UserIdentity for the caller under the channel's provider — they have never spoken on this platform.
        await db.SaveChangesAsync();

        TtsConfigController controller = Build(db, CallerUserId);

        IActionResult result = await controller.SetOwnVoice(
            Broadcaster.ToString(),
            new SetUserVoiceDto { VoiceId = VoiceId },
            CancellationToken.None
        );

        result.Should().BeOfType<NotFoundObjectResult>();
        // Nothing is written against an id the dispatcher could never resolve.
        (await db.UserTtsVoices.AnyAsync())
            .Should()
            .BeFalse();
    }

    [Fact]
    public async Task GetOwnVoice_returns_the_callers_assigned_voice_after_they_set_it()
    {
        TtsConfigControllerOwnVoiceTestDbContext db =
            TtsConfigControllerOwnVoiceTestDbContext.New();
        SeedChannel(db);
        db.UserIdentities.Add(
            new UserIdentity
            {
                UserId = CallerUserId,
                Provider = AuthEnums.Platform.Twitch,
                ProviderUserId = CallerExternalId,
                ProviderUsername = "caller",
                LinkedAt = DateTime.UtcNow,
            }
        );
        db.UserTtsVoices.Add(
            new UserTtsVoice
            {
                BroadcasterId = Broadcaster,
                UserId = CallerExternalId,
                VoiceId = VoiceId,
            }
        );
        await db.SaveChangesAsync();

        TtsConfigController controller = Build(db, CallerUserId);

        IActionResult result = await controller.GetOwnVoice(
            Broadcaster.ToString(),
            CancellationToken.None
        );

        OkObjectResult ok = result.Should().BeOfType<OkObjectResult>().Subject;
        StatusResponseDto<UserTtsVoiceDto?> body = (StatusResponseDto<UserTtsVoiceDto?>)ok.Value!;
        body.Data!.UserId.Should().Be(CallerExternalId);
        body.Data!.VoiceId.Should().Be(VoiceId);
    }
}

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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Tts.Dtos;
using NomNomzBot.Application.Tts.Services;
using NomNomzBot.Domain.Platform.Events;
using NomNomzBot.Infrastructure.Tests.Identity;
using NomNomzBot.Infrastructure.Tts;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Tts;

/// <summary>
/// Proves <see cref="TtsConfigService.UpdateConfigAsync"/> publishes the E5 dashboard live-sync event so a second
/// open dashboard's TTS settings page refetches after a change, and that an unresolvable channel id (no real
/// tenant) never reaches a hub group (the broadcast handler's <c>Guid.Empty</c> guard).
/// </summary>
public sealed class TtsConfigServiceTests
{
    private static readonly Guid Channel = Guid.Parse("0192a000-0000-7000-8000-000000000d01");

    private static (TtsConfigService Sut, RecordingEventBus Bus) Build()
    {
        AuthDbContext db = AuthTestBuilder.NewContext();
        ITtsService ttsService = Substitute.For<ITtsService>();
        RecordingEventBus bus = new();
        return (new TtsConfigService(db, ttsService, bus), bus);
    }

    [Fact]
    public async Task Update_publishes_ChannelConfigChangedEvent_for_the_tts_config_domain()
    {
        (TtsConfigService sut, RecordingEventBus bus) = Build();

        Result<TtsConfigDto> result = await sut.UpdateConfigAsync(
            Channel.ToString(),
            new UpdateTtsConfigDto { IsEnabled = false, MaxLength = 120 }
        );

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEnabled.Should().BeFalse();
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e =>
                e.BroadcasterId == Channel && e.Domain == "tts-config" && e.Action == "updated"
            );
    }

    [Fact]
    public async Task Update_with_an_unresolvable_channel_id_never_reaches_a_real_hub_group()
    {
        (TtsConfigService sut, RecordingEventBus bus) = Build();

        await sut.UpdateConfigAsync("not-a-guid", new UpdateTtsConfigDto { IsEnabled = true });

        // The service still fires the generic event, but with the Guid.Empty sentinel — the
        // ChannelConfigChangedBroadcastHandler's platform-level guard is what stops it from ever
        // reaching a dashboard group, so no channel is falsely notified.
        bus.Published.OfType<ChannelConfigChangedEvent>()
            .Should()
            .ContainSingle(e => e.BroadcasterId == Guid.Empty);
    }
}

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
using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.Federation;
using NomNomzBot.Application.Moderation.Dtos;
using NomNomzBot.Application.Moderation.Services;
using NomNomzBot.Domain.Federation.Enums;
using NomNomzBot.Domain.Moderation.Events;
using NomNomzBot.Infrastructure.Moderation.Federation;

namespace NomNomzBot.Infrastructure.Tests.Federation;

/// <summary>
/// Proves the moderation-owned inbound handler for <c>moderation.ban.shared</c> (federation-oidc.md §3.7):
/// it declares the type + gating opt-in that form the accept-set, deserializes the envelope payload into
/// <see cref="SharedChatBanIssuedEvent"/> and applies it through the FEDERATED apply path for the resolved
/// target, and fails closed (<c>schema_invalid</c>) on a payload it cannot read — never touching the service.
/// </summary>
public sealed class SharedChatBanInboundHandlerTests
{
    private static readonly Guid Peer = Guid.Parse("0192a000-0000-7000-8000-00000000cc01");
    private static readonly Guid Target = Guid.Parse("0192a000-0000-7000-8000-00000000cc02");
    private static readonly Guid OriginChannel = Guid.Parse("0192a000-0000-7000-8000-00000000cc03");

    private static FederationEventEnvelope EnvelopeWith(string payloadJson) =>
        new(
            Guid.CreateVersion7(),
            "peer-1",
            OriginBroadcasterId: null,
            TargetBroadcasterId: Target,
            "moderation.ban.shared",
            SchemaVersion: 1,
            PayloadJson: payloadJson,
            OccurredAt: DateTimeOffset.UnixEpoch
        );

    [Fact]
    public void It_declares_the_type_and_gating_opt_in_that_form_the_accept_set()
    {
        SharedChatBanInboundHandler handler = new(new RecordingSharedBans());
        handler.Type.Should().Be("moderation.ban.shared");
        handler.GatingOptInType.Should().Be(FederationOptInType.SharedChatBans);
    }

    [Fact]
    public async Task It_deserializes_the_payload_and_applies_it_to_the_target_via_the_federated_path()
    {
        RecordingSharedBans bans = new();
        SharedChatBanInboundHandler handler = new(bans);
        string payload = JsonConvert.SerializeObject(
            new SharedChatBanIssuedEvent
            {
                SharedChatSessionId = "n/a",
                OriginChannelId = OriginChannel,
                TargetTwitchUserId = "troll-42",
                TargetDisplayName = "Troll",
                Reason = "spam",
            }
        );

        Result result = await handler.ApplyAsync(Peer, Target, EnvelopeWith(payload));

        result.IsSuccess.Should().BeTrue(result.ErrorMessage);
        bans.Applied.Should().ContainSingle();
        (Guid Target, SharedChatBanIssuedEvent Event) call = bans.Applied[0];
        call.Target.Should().Be(Target);
        call.Event.TargetTwitchUserId.Should().Be("troll-42");
        call.Event.Reason.Should().Be("spam");
        call.Event.OriginChannelId.Should().Be(OriginChannel);
    }

    [Fact]
    public async Task A_malformed_payload_fails_closed_and_never_reaches_the_service()
    {
        RecordingSharedBans bans = new();
        SharedChatBanInboundHandler handler = new(bans);

        Result result = await handler.ApplyAsync(Peer, Target, EnvelopeWith("{ not valid json"));

        result.IsFailure.Should().BeTrue();
        result.ErrorCode.Should().Be("schema_invalid");
        bans.Applied.Should().BeEmpty();
    }

    [Fact]
    public async Task A_payload_missing_the_target_user_id_fails_closed()
    {
        RecordingSharedBans bans = new();
        SharedChatBanInboundHandler handler = new(bans);
        string payload = JsonConvert.SerializeObject(new { originChannelId = OriginChannel });

        Result result = await handler.ApplyAsync(Peer, Target, EnvelopeWith(payload));

        result.ErrorCode.Should().Be("schema_invalid");
        bans.Applied.Should().BeEmpty();
    }

    /// <summary>Records every federated apply the handler routes, so a test asserts the exact call it made.</summary>
    private sealed class RecordingSharedBans : ISharedBanService
    {
        public List<(Guid Target, SharedChatBanIssuedEvent Event)> Applied { get; } = [];

        public Task<Result<SharedBanApplicationResult>> ApplyInboundFederatedBanAsync(
            Guid targetBroadcasterId,
            SharedChatBanIssuedEvent inbound,
            CancellationToken ct = default
        )
        {
            Applied.Add((targetBroadcasterId, inbound));
            return Task.FromResult(Result.Success(new SharedBanApplicationResult(true, null, 1)));
        }

        public Task<Result<SharedBanSettingsDto>> GetSettingsAsync(
            Guid broadcasterId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<SharedBanSettingsDto>> SaveSettingsAsync(
            Guid broadcasterId,
            Guid actorUserId,
            SaveSharedBanSettingsRequest request,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<SharedBanTrustedChannelDto>> AddTrustedChannelAsync(
            Guid broadcasterId,
            Guid actorUserId,
            Guid trustedChannelId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result> RemoveTrustedChannelAsync(
            Guid broadcasterId,
            Guid actorUserId,
            Guid trustedChannelId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<Result<SharedBanApplicationResult>> ApplyInboundSharedBanAsync(
            Guid partnerBroadcasterId,
            SharedChatBanIssuedEvent inbound,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging.Abstractions;
using NomNomzBot.Application.Abstractions.Templating;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Identity.Dtos;
using NomNomzBot.Application.Identity.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Domain.Chat.Interfaces;
using NomNomzBot.Infrastructure.Chat;
using NSubstitute;

namespace NomNomzBot.Infrastructure.Tests.Chat;

/// <summary>Builds a real <see cref="ReplyDispatcher"/> over a test's chat sender, for the handler and invoker tests.</summary>
internal static class TestReplyDispatcher
{
    /// <summary>
    /// A real dispatcher whose threaded chat leg is <paramref name="chat"/>, whose channel-provider leg accepts
    /// every send, and whose overlay is <paramref name="overlay"/> (default: no chat overlay open).
    /// </summary>
    public static ReplyDispatcher Over(
        IInboundOriginChatSender chat,
        IChatOverlayLineSender? overlay = null,
        IChatProvider? provider = null,
        IObsBrowserSourceRecoveryScheduler? recovery = null,
        IChannelTwitchBotResolver? bots = null
    ) =>
        new(
            chat,
            provider ?? AcceptingProvider(),
            overlay ?? new NullChatOverlayLineSender(),
            bots ?? new NoDedicatedBot(),
            recovery ?? Substitute.For<IObsBrowserSourceRecoveryScheduler>(),
            NullLogger<ReplyDispatcher>.Instance
        );

    private static IChatProvider AcceptingProvider()
    {
        IChatProvider provider = Substitute.For<IChatProvider>();
        provider
            .SendMessageAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(true);
        return provider;
    }

    private sealed class NoDedicatedBot : IChannelTwitchBotResolver
    {
        public Task<ChannelTwitchBot?> ResolveAsync(
            Guid broadcasterId,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<ChannelTwitchBot?>(null);
    }
}

/// <summary>Records each line pushed to the chat overlay and answers with a fixed delivery.</summary>
internal sealed class RecordingChatOverlayLineSender : IChatOverlayLineSender
{
    public List<ChatOverlayLine> Lines { get; } = [];

    public ChatOverlayDelivery Result { get; set; } = new(1);

    public Task<ChatOverlayDelivery> SendAsync(
        ChatOverlayLine line,
        CancellationToken cancellationToken = default
    )
    {
        Lines.Add(line);
        return Task.FromResult(Result);
    }
}

/// <summary>Replaces <c>{key}</c> with the seed value and records each seed bag it was given.</summary>
internal sealed class SubstitutingTemplateResolver : ITemplateResolver
{
    public List<Dictionary<string, string>> Seeds { get; } = [];

    public Task<string> ResolveAsync(
        string template,
        IDictionary<string, string> seedVariables,
        Guid? broadcasterId,
        CancellationToken cancellationToken = default
    )
    {
        Seeds.Add(new Dictionary<string, string>(seedVariables, StringComparer.OrdinalIgnoreCase));
        return Task.FromResult(Resolve(template, seedVariables));
    }

    public string Resolve(string template, IDictionary<string, string> variables)
    {
        string text = template;
        foreach (KeyValuePair<string, string> pair in variables)
            text = text.Replace("{" + pair.Key + "}", pair.Value, StringComparison.Ordinal);
        return text;
    }
}

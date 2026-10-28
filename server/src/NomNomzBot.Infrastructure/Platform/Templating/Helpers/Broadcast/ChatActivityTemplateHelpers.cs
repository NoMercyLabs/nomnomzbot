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

using Microsoft.Extensions.DependencyInjection;
using NomNomzBot.Application.Abstractions.Templating;

namespace NomNomzBot.Infrastructure.Platform.Templating.Helpers.Broadcast;

/// <summary>
/// The <c>activity</c> namespace (catalog section 7.12): chat speed, who is active, the top chatters and when chat
/// is busy. It owns the <see cref="Ring"/> that <see cref="ChatActivityRecorder"/> feeds.
/// </summary>
internal sealed class ChatActivityTemplateHelpers : BroadcastHelperProvider<ActivityData>
{
    private readonly IServiceScopeFactory _scopes;
    private readonly Random _random;

    public ChatActivityTemplateHelpers(IServiceScopeFactory scopes)
        : this(scopes, new ChatActivityRing(TimeProvider.System), Random.Shared) { }

    /// <summary>The second constructor lets a test hand in a ring on a fake clock and a seeded random.</summary>
    internal ChatActivityTemplateHelpers(
        IServiceScopeFactory scopes,
        ChatActivityRing ring,
        Random random
    )
        : base([new ActivityRingHelpers(), new ActivityStreamHelpers(), new ActivityDayHelpers()])
    {
        _scopes = scopes;
        _random = random;
        Ring = ring;
    }

    public ChatActivityRing Ring { get; }

    public override string Namespace => "activity";

    protected override ActivityData? DataFor(TemplateRenderScope scope) =>
        ActivityData.For(scope, _scopes, Ring, _random);
}

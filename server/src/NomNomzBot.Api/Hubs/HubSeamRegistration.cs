// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using Microsoft.Extensions.DependencyInjection.Extensions;
using NomNomzBot.Api.Hubs.Broadcasters;
using NomNomzBot.Api.Hubs.Overlay;
using NomNomzBot.Application.Chat.Services;
using NomNomzBot.Application.Commands.Services;
using NomNomzBot.Application.Contracts.EventStore;
using NomNomzBot.Application.Notifications.Services;
using NomNomzBot.Application.Obs.Services;
using NomNomzBot.Application.Overlays.Services;
using NomNomzBot.Application.Sound.Services;
using NomNomzBot.Application.Widgets.Services;

namespace NomNomzBot.Api.Hubs;

/// <summary>
/// The one place where the hub-backed adapters take over from the silent stand-ins that Infrastructure
/// registers (its <c>Null*</c>, <c>Unavailable*</c> and <c>NoOp*</c> classes swallow their output). Every
/// swap uses <c>Replace</c>, so the result never depends on which descriptor was added last, and
/// <c>HubSeamRegistrationTests</c> fails when a stand-in is left in place.
/// </summary>
public static class HubSeamRegistration
{
    /// <summary>Call once, after <c>AddInfrastructure</c>, so the adapters replace the stand-ins.</summary>
    public static IServiceCollection AddHubBackedSeams(this IServiceCollection services)
    {
        // One instance: the hub writes attachments, TTS and sound (any overlay-only output) read them through the
        // interface to tell whether a browser source is listening instead of reporting silence as success.
        services.AddSingleton<OverlayPresenceRegistry>();
        services.Replace(
            ServiceDescriptor.Singleton<IOverlayPresenceRegistry>(sp =>
                sp.GetRequiredService<OverlayPresenceRegistry>()
            )
        );

        // Pushes to the leader OBS bridge over the relay hub (obs-control.md §3.2).
        services.Replace(ServiceDescriptor.Singleton<IObsBridgePusher, ObsBridgePusher>());

        // Chat, journal and action-required changes pushed to the dashboard and external chat consumers.
        services.Replace(ServiceDescriptor.Singleton<IExternalChatPush, ExternalChatPush>());
        services.Replace(
            ServiceDescriptor.Singleton<
                IActionRequiredChangeNotifier,
                ActionRequiredChangeNotifier
            >()
        );
        services.Replace(
            ServiceDescriptor.Singleton<IEventJournalChangeNotifier, EventJournalChangeNotifier>()
        );

        // Overlay output: sounds, widget events, retractions, the generic event feed, event-response overlays
        // and the replay of a captured alert.
        services.Replace(ServiceDescriptor.Scoped<IRenderedAlertReplayer, RenderedAlertReplayer>());
        services.Replace(
            ServiceDescriptor.Scoped<ISoundClipOverlayNotifier, SoundClipOverlayNotifierAdapter>()
        );
        services.Replace(
            ServiceDescriptor.Scoped<IWidgetEventNotifier, WidgetEventNotifierAdapter>()
        );
        services.Replace(
            ServiceDescriptor.Scoped<IOverlayRetractionNotifier, OverlayRetractionNotifierAdapter>()
        );
        services.Replace(ServiceDescriptor.Scoped<IOverlayEventFeed, OverlayEventFeedAdapter>());
        services.Replace(ServiceDescriptor.Scoped<IChatOverlayLineSender, ChatOverlayLineSender>());
        services.Replace(
            ServiceDescriptor.Scoped<
                IEventResponseOverlayNotifier,
                EventResponseOverlayNotifierAdapter
            >()
        );

        return services;
    }
}

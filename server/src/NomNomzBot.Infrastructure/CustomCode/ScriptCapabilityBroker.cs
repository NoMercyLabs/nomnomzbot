// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using NomNomzBot.Application.Common.Models;
using NomNomzBot.Application.Contracts.CustomCode;
using NomNomzBot.Application.Platform.Services;

namespace NomNomzBot.Infrastructure.CustomCode;

/// <summary>
/// Per-tenant capability grant assembly (custom-code.md §3.2, catalogue §6.2). Deny-by-default: a script gets only
/// the capabilities it declared, each validated against the catalogue (must exist, must not be <c>critical</c>, its
/// gating feature must be enabled for the channel). Any disallowed declared capability fails the whole grant
/// FORBIDDEN — fail-closed.
///
/// The gate is the per-channel <b>Custom Code</b> feature toggle (<see cref="IFeatureService"/>) — the one an
/// owner flips on in Settings → Features. It is NOT a platform rollout <c>FeatureFlag</c>: gating on that
/// (never-seeded) flag left the toggle inert and every script denied at run time regardless of the switch.
/// </summary>
public sealed class ScriptCapabilityBroker(IFeatureService features) : IScriptCapabilityBroker
{
    private const string FeatureGate = "custom_code";

    // The §6.2 host-call surface. low/tos only — no `critical` capability is ever exposed to run_code.
    private static readonly ScriptCapabilityDescriptor[] CatalogEntries =
    [
        new("vars.read", "low", FeatureGate, SideEffecting: false),
        new("vars.write", "low", FeatureGate, SideEffecting: true),
        new("args.get", "low", FeatureGate, SideEffecting: false),
        new("user.get", "low", FeatureGate, SideEffecting: false),
        new("chat.send", "tos", FeatureGate, SideEffecting: true),
        new("chat.reply", "tos", FeatureGate, SideEffecting: true),
        new("music.queue", "tos", FeatureGate, SideEffecting: true),
        new("music.nowPlaying", "low", FeatureGate, SideEffecting: false),
        new("economy.read", "low", FeatureGate, SideEffecting: false),
        new("http.fetch", "tos", FeatureGate, SideEffecting: true),
        // Per-channel bounded script KV store (64 KB values, 200 keys/channel) — no external surface.
        new("storage.get", "low", FeatureGate, SideEffecting: false),
        new("storage.set", "low", FeatureGate, SideEffecting: true),
        new("storage.delete", "low", FeatureGate, SideEffecting: true),
        new("storage.list", "low", FeatureGate, SideEffecting: false),
        // Routed through the gated TTS dispatcher (channel enable + caps + censor run host-side).
        new("tts.speak", "low", FeatureGate, SideEffecting: true),
        // Pushes an event to one of THIS channel's enabled widgets (overlay-only; no Twitch surface).
        new("widget.emit", "low", FeatureGate, SideEffecting: true),
        // Channel-point reward read + patch; update mutates the reward on Twitch via Helix → tos tier.
        new("reward.get", "low", FeatureGate, SideEffecting: false),
        new("reward.update", "tos", FeatureGate, SideEffecting: true),
        // Per-viewer M.1 analytics snapshot (same profile source the {viewer.*} template stats read).
        new("stats.viewer", "low", FeatureGate, SideEffecting: false),
        // Per-viewer TTS voice assignment (the !voice self-service surface) — set validates against the
        // voice catalogue and stays tenant-scoped; side-effecting low, no Twitch surface.
        new("tts.voice.get", "low", FeatureGate, SideEffecting: false),
        new("tts.voice.set", "low", FeatureGate, SideEffecting: true),
        // Schedules a saved pipeline to run once after a delay (the deferred-execution primitive — e.g. a
        // voice-swap script scheduling its own revert). Persists a task; no external/Twitch surface → low tier.
        new("schedule.pipeline", "low", FeatureGate, SideEffecting: true),
    ];

    public IReadOnlyList<ScriptCapabilityDescriptor> Catalog => CatalogEntries;

    public async Task<Result<ScriptCapabilityGrant>> BuildGrantAsync(
        Guid broadcasterId,
        IReadOnlyList<string> declaredCapabilities,
        CancellationToken cancellationToken = default
    )
    {
        List<ScriptCapabilityDescriptor> granted = [];
        foreach (string key in declaredCapabilities.Distinct(StringComparer.Ordinal))
        {
            ScriptCapabilityDescriptor? descriptor = CatalogEntries.FirstOrDefault(c =>
                c.Key == key
            );
            if (descriptor is null)
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Unknown capability: {key}.",
                    "FORBIDDEN"
                );
            if (descriptor.FloorTier == "critical")
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Capability not permitted in scripts: {key}.",
                    "FORBIDDEN"
                );
            if (
                !await features.IsFeatureEnabledAsync(
                    broadcasterId.ToString(),
                    descriptor.FeatureFlagKey,
                    cancellationToken
                )
            )
                return Result.Failure<ScriptCapabilityGrant>(
                    $"Capability is not enabled: {key}.",
                    "FORBIDDEN"
                );
            granted.Add(descriptor);
        }
        return Result.Success(new ScriptCapabilityGrant(broadcasterId, granted));
    }
}

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

namespace NomNomzBot.Domain.Enums.Deployment;

/// <summary>
/// The deployment-profile axis (schema P.12, deployment-profile design). One boot-time decision selects every
/// swappable adapter. <c>SelfHostLite</c> = a single self-contained binary (SQLite + in-process cache/bus +
/// WebSocket EventSub, no external services); <c>SelfHostFull</c> = the Docker image with Postgres + Redis;
/// <c>Saas</c> = the same image operated as a stateless fleet.
/// </summary>
public enum DeploymentMode
{
    SelfHostLite,
    SelfHostFull,
    Saas,
}

/// <summary>The relational provider the <c>AppDbContext</c> binds at runtime: SQLite (lite) or Postgres (full/SaaS).</summary>
public enum DbProviderKind
{
    Sqlite,
    Postgres,
}

/// <summary>Cache + pub/sub backing: an in-process implementation (lite) or Redis (full/SaaS).</summary>
public enum CacheProviderKind
{
    InMemory,
    Redis,
}

/// <summary>
/// Per-instance EventSub delivery strategy. <c>WebSocket</c> on self-host, <c>ConduitWebhook</c> on SaaS. This is
/// the profile mode, distinct from the per-subscription wire transport enum (<c>EventSubTransportKind</c>).
/// </summary>
public enum EventSubTransportMode
{
    WebSocket,
    ConduitWebhook,
}

/// <summary>The custom-code sandbox runtime: Jint (lite) or Wasmtime (SaaS).</summary>
public enum CodeExecutorKind
{
    Jint,
    Wasmtime,
}

/// <summary>Root-KEK custody: a local-AES key in the OS-native secure store (self-host/SaaS-VMs) or cloud KMS envelope (SaaS Phase 3).</summary>
public enum TokenVaultKind
{
    LocalAes,
    KmsEnvelope,
}

/// <summary>How the bot is reached from the internet: an operator-opted-in tunnel (self-host) or a managed edge (SaaS).</summary>
public enum ExposureModelKind
{
    OptInTunnel,
    ManagedEdge,
}

/// <summary>
/// The first-run "Simple vs Advanced" wizard answer — the seed default for new users' <c>UserPreferences.GuidanceLevel</c>.
/// Simple ⇒ <c>Novice</c>, Advanced ⇒ <c>Expert</c>; a bypassed/non-interactive setup falls back to <c>Novice</c>.
/// </summary>
public enum GuidanceLevel
{
    Novice,
    Expert,
}

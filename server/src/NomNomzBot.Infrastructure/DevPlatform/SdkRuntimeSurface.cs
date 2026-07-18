// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

using System.Text;
using NomNomzBot.Application.DevPlatform;

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The single authored source-of-truth for the SDK's <b>fixed</b> runtime surface (dev-platform.md §3.1) — the
/// pure-JS batteries (<c>nnz.units/time/math/str/json/random</c>) and the <c>nnz.api.*</c> wrappers over the
/// capability broker. Unlike the event map (which is 100%-reflected from the C# event records and must never be
/// hand-typed), this surface is a <b>bounded, one-time library</b>: its shape is fixed by the JS implementation in
/// <c>JintScriptExecutor</c>'s bootstrap, so declaring its TypeScript from one authored descriptor here — rather
/// than reflecting it — is the correct, drift-free choice (there is nothing to reflect; the JS is the contract).
/// The event codegen is untouched by this class. Per-context: batteries + the read-mostly api appear everywhere;
/// the write/privileged api (<c>chat</c>, <c>http</c>, <c>music.queue</c>) is <see cref="SdkContext.Script"/>-only,
/// mirroring which capabilities each context can be granted.
/// </summary>
internal static class SdkRuntimeSurface
{
    /// <summary>
    /// The supporting payload interfaces the api methods return (the public projections the host bridge emits —
    /// no PII). Always emitted (both contexts reference them via the read-mostly api). Named <c>NnzApi*</c> so
    /// they never collide with a reflected event payload interface.
    /// </summary>
    public static string Interfaces()
    {
        StringBuilder sb = new();
        sb.AppendLine("interface NnzApiUser {");
        sb.AppendLine("  id: string;");
        sb.AppendLine("  username: string;");
        sb.AppendLine("  displayName: string;");
        sb.AppendLine("  avatarUrl: string | null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiTrack {");
        sb.AppendLine("  track: string;");
        sb.AppendLine("  artist: string;");
        sb.AppendLine("  album: string | null;");
        sb.AppendLine("  durationMs: number;");
        sb.AppendLine("  progressMs: number;");
        sb.AppendLine("  isPlaying: boolean;");
        sb.AppendLine("  requestedBy: string | null;");
        sb.AppendLine("  provider: string;");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// The <c>nnz</c> member lines (batteries + api) inserted inside the generated <c>declare const nnz: {</c>
    /// block, after the reflected <c>on/once/off</c> event surface. Indented two spaces to sit at member level.
    /// <paramref name="context"/> selects the api subset: <see cref="SdkContext.Widget"/> gets the read-mostly
    /// set only; <see cref="SdkContext.Script"/> gets the full set.
    /// </summary>
    public static string Members(SdkContext context)
    {
        StringBuilder sb = new();
        AppendBatteries(sb);
        AppendApi(sb, context);
        return sb.ToString().TrimEnd('\r', '\n');
    }

    private static void AppendBatteries(StringBuilder sb)
    {
        sb.AppendLine("  units: {");
        sb.AppendLine("    convert(value: number, from: string, to: string): number;");
        sb.AppendLine("  };");
        sb.AppendLine("  time: {");
        sb.AppendLine("    now(): string;");
        sb.AppendLine("    parse(iso: string): number;");
        sb.AppendLine("    format(epochMs: number): string;");
        sb.AppendLine("    add(iso: string, ms: number): string;");
        sb.AppendLine("    diff(a: string, b: string): number;");
        sb.AppendLine("  };");
        sb.AppendLine("  math: {");
        sb.AppendLine("    clamp(value: number, min: number, max: number): number;");
        sb.AppendLine("    round(value: number, digits?: number): number;");
        sb.AppendLine("    lerp(a: number, b: number, t: number): number;");
        sb.AppendLine("    sum(values: number[]): number;");
        sb.AppendLine("    avg(values: number[]): number;");
        sb.AppendLine("    min(values: number[]): number;");
        sb.AppendLine("    max(values: number[]): number;");
        sb.AppendLine("    randomInt(min: number, max: number): number;");
        sb.AppendLine("  };");
        sb.AppendLine("  str: {");
        sb.AppendLine("    padStart(value: string, length: number, pad?: string): string;");
        sb.AppendLine("    padEnd(value: string, length: number, pad?: string): string;");
        sb.AppendLine("    trim(value: string): string;");
        sb.AppendLine("    upper(value: string): string;");
        sb.AppendLine("    lower(value: string): string;");
        sb.AppendLine("    title(value: string): string;");
        sb.AppendLine("    truncate(value: string, length: number, ellipsis?: string): string;");
        sb.AppendLine("    slugify(value: string): string;");
        sb.AppendLine("    format(template: string, values: Record<string, unknown>): string;");
        sb.AppendLine("  };");
        sb.AppendLine("  json: {");
        sb.AppendLine("    parse(text: string): unknown;");
        sb.AppendLine("    stringify(value: unknown): string;");
        sb.AppendLine("  };");
        sb.AppendLine("  random: {");
        sb.AppendLine("    int(min: number, max: number): number;");
        sb.AppendLine("    pick<T>(items: T[]): T;");
        sb.AppendLine("    shuffle<T>(items: T[]): T[];");
        sb.AppendLine("    uuid(): string;");
        sb.AppendLine("  };");
    }

    private static void AppendApi(StringBuilder sb, SdkContext context)
    {
        sb.AppendLine("  api: {");
        // Read-mostly surface — present in every context (the widget's safe read set + the full script set).
        sb.AppendLine("    user: { get(id?: string): NnzApiUser | null };");
        sb.AppendLine("    economy: { balance(userId?: string): number };");
        if (context == SdkContext.Script)
        {
            // Write/privileged surface — script context only (server-side, broker-gated at grant time).
            sb.AppendLine("    chat: { send(text: string): void; reply(text: string): void };");
            sb.AppendLine(
                "    music: { nowPlaying(): NnzApiTrack | null; queue(uri: string): boolean };"
            );
            sb.AppendLine("    http: { fetch(url: string): string | null };");
        }
        else
        {
            sb.AppendLine("    music: { nowPlaying(): NnzApiTrack | null };");
        }
        sb.AppendLine("  };");
    }
}

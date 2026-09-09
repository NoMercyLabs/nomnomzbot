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

namespace NomNomzBot.Infrastructure.DevPlatform;

/// <summary>
/// The single authored source-of-truth for the SDK's <b>fixed</b> runtime surface (dev-platform.md §3.1) — the
/// globals each context actually has. Script: the <c>bot</c> facade plus the <c>nnz</c> batteries and
/// <c>nnz.api.*</c> wrappers that <c>JintScriptExecutor</c>'s bootstrap builds. Widget: <c>window.NomNomz</c> from
/// <c>OverlaySdkController</c>'s served SDK plus the <c>WIDGET_*</c> config globals <c>OverlayHostController</c>
/// injects into the page. Unlike the event map (100%-reflected from the C# event records) there is nothing here to
/// reflect — the JS is the contract — so this surface is declared by hand.
/// <para>
/// Hand-authoring is only drift-free when it is <b>enforced</b>, and for a long time it was not: the script context
/// declared an <c>nnz.on/once/off</c> that never existed and omitted the <c>bot</c> global entirely. Two tests now
/// hold the claim up. <c>SdkScriptSurfaceDriftTests</c> runs the real bootstrap in the real hardened Jint engine and
/// fails on any global or top-level member this file declares-but-the-runtime-lacks, or the runtime-has-but-this-file
/// omits. <c>OverlaySdkSurfaceDriftTests</c> (Api.Tests) does the same against the served overlay SDK and the
/// injected page config. Change the JS and those tests name the member to change here.
/// </para>
/// The event codegen is untouched by this class. The write/privileged api (<c>chat</c>, <c>http</c>,
/// <c>music.queue</c>, <c>storage</c>, <c>tts</c>, <c>widget</c>, <c>reward</c>, <c>schedule</c>) exists only in the
/// script sandbox; a widget has no host bridge at all, so it gets no <c>nnz</c>.
/// </summary>
internal static class SdkRuntimeSurface
{
    /// <summary>
    /// The supporting payload interfaces the <c>nnz.api.*</c> methods return (the public projections the host
    /// bridge emits — no PII). Script-only, like the api itself. Named <c>NnzApi*</c> so they never collide with a
    /// reflected event payload interface.
    /// </summary>
    public static string ScriptApiInterfaces()
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
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** What nnz.api.tts.speak returns on a dispatched utterance. */");
        sb.AppendLine("interface NnzApiTtsResult {");
        sb.AppendLine("  voiceId: string;");
        sb.AppendLine("  characterCount: number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A channel-point reward as nnz.api.reward.get returns it. */");
        sb.AppendLine("interface NnzApiReward {");
        sb.AppendLine("  id: string;");
        sb.AppendLine("  title: string;");
        sb.AppendLine("  cost: number;");
        sb.AppendLine("  prompt: string | null;");
        sb.AppendLine("  isEnabled: boolean;");
        sb.AppendLine("  isPaused: boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** The patch nnz.api.reward.update applies — only the fields you set change. */"
        );
        sb.AppendLine("interface NnzApiRewardPatch {");
        sb.AppendLine("  title?: string;");
        sb.AppendLine("  cost?: number;");
        sb.AppendLine("  prompt?: string;");
        sb.AppendLine("  isEnabled?: boolean;");
        sb.AppendLine("  isPaused?: boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** A viewer's channel stats as nnz.api.stats.viewer returns them (zeros for a never-seen viewer). */"
        );
        sb.AppendLine("interface NnzApiViewerStats {");
        sb.AppendLine("  messages: number;");
        sb.AppendLine("  watchtimeSeconds: number;");
        sb.AppendLine("  firstSeen: string | null;");
        sb.AppendLine("  redemptions: number;");
        sb.AppendLine("  songRequests: number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A viewer's assigned TTS voice as nnz.api.tts.getVoice returns it. */");
        sb.AppendLine("interface NnzApiTtsVoice {");
        sb.AppendLine("  voiceId: string;");
        sb.AppendLine("  displayName: string;");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Every global the Jint sandbox actually exposes to a user script: the <c>bot</c> facade and the <c>nnz</c>
    /// SDK, in the order the bootstrap defines them. There is no event bus in the sandbox — a script is invoked by
    /// the <c>run_code</c> pipeline action with args + variables — so no <c>on/once/off</c> is declared.
    /// </summary>
    public static string ScriptGlobals()
    {
        StringBuilder sb = new();
        AppendBotFacade(sb);
        sb.AppendLine();
        AppendBatteryInterfaces(sb);
        AppendApiInterfaces(sb);
        sb.AppendLine("declare const nnz: {");
        sb.AppendLine("  units: NnzUnits;");
        sb.AppendLine("  time: NnzTime;");
        sb.AppendLine("  math: NnzMath;");
        sb.AppendLine("  str: NnzStr;");
        sb.AppendLine("  json: NnzJson;");
        sb.AppendLine("  random: NnzRandom;");
        sb.AppendLine("  api: NnzApi;");
        sb.Append("};");
        return sb.ToString();
    }

    /// <summary>
    /// Every global a widget page actually has: <c>window.NomNomz</c> (the overlay SDK) and the five
    /// <c>WIDGET_*</c> values the host page injects before the bundle runs. A widget has no capability broker, so
    /// none of the <c>nnz</c> batteries or <c>nnz.api.*</c> wrappers exist here.
    /// </summary>
    public static string WidgetGlobals()
    {
        StringBuilder sb = new();
        sb.AppendLine("/**");
        sb.AppendLine(
            " * The overlay SDK global, served as /overlay/sdk.js and installed before the widget bundle"
        );
        sb.AppendLine(" * runs. Event names are this widget's OWN subscription keys (see");
        sb.AppendLine(
            " * WIDGET_EVENT_SUBSCRIPTIONS) — e.g. 'follow', 'tts_speak' — not the NnzEventMap wire names."
        );
        sb.AppendLine(" * Every registration returns the SDK, so calls chain.");
        sb.AppendLine(" */");
        sb.AppendLine("interface NnzOverlaySdk {");
        sb.AppendLine(
            "  on(eventType: string, handler: (data: any, eventType: string) => void): NnzOverlaySdk;"
        );
        sb.AppendLine(
            "  /** Removes a handler registered with on() — pass the SAME function reference. */"
        );
        sb.AppendLine(
            "  off(eventType: string, handler: (data: any, eventType: string) => void): NnzOverlaySdk;"
        );
        sb.AppendLine("  onAny(handler: (eventType: string, data: any) => void): NnzOverlaySdk;");
        sb.AppendLine(
            "  /** Fires immediately with the injected settings, then again on every dashboard change. */"
        );
        sb.AppendLine(
            "  onSettings(handler: (settings: Record<string, any>) => void): NnzOverlaySdk;"
        );
        sb.AppendLine(
            "  /** Logs the message and reports it to the server as a widget runtime error. */"
        );
        sb.AppendLine("  reportError(message: string): void;");
        sb.AppendLine("  readonly settings: Record<string, any>;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("declare const NomNomz: NnzOverlaySdk;");
        sb.AppendLine();
        sb.AppendLine("declare const WIDGET_ID: string;");
        sb.AppendLine("declare const WIDGET_TOKEN: string;");
        sb.AppendLine("declare const WIDGET_NAME: string;");
        sb.AppendLine("declare const WIDGET_SETTINGS: Record<string, any>;");
        sb.Append("declare const WIDGET_EVENT_SUBSCRIPTIONS: string[];");
        return sb.ToString();
    }

    private static void AppendBotFacade(StringBuilder sb)
    {
        sb.AppendLine(
            "/** The primitive-in/primitive-out host facade every script runs against. */"
        );
        sb.AppendLine("declare const bot: {");
        sb.AppendLine("  /** The arguments the trigger passed in ('!roll 20' -> ['20']). */");
        sb.AppendLine("  args: string[];");
        sb.AppendLine("  getVar(key: string): string | null;");
        sb.AppendLine("  setVar(key: string, value: string): void;");
        sb.AppendLine("  /** Appends to the script's output (capped by the execution budget). */");
        sb.AppendLine("  send(message: string): void;");
        sb.AppendLine(
            "  /** The raw capability bridge every nnz.api.* wrapper goes through; an ungranted key is denied. */"
        );
        sb.AppendLine("  call(key: string, ...args: string[]): string | null;");
        sb.AppendLine("};");
    }

    // One named interface per nnz.<namespace> — hovering `nnz` itself now shows six short type references
    // instead of the whole batteries tree inline (owner: "a better type definition than it being a big
    // object"), and hovering e.g. `nnz.time` shows the named NnzTime signature directly.
    private static void AppendBatteryInterfaces(StringBuilder sb)
    {
        sb.AppendLine("interface NnzUnits {");
        sb.AppendLine("  convert(value: number, from: string, to: string): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzTime {");
        sb.AppendLine("  now(): string;");
        sb.AppendLine("  parse(iso: string): number;");
        sb.AppendLine("  format(epochMs: number): string;");
        sb.AppendLine("  add(iso: string, ms: number): string;");
        sb.AppendLine("  diff(a: string, b: string): number;");
        sb.AppendLine(
            "  /** Blocks up to 5000ms (clamped) before the script's NEXT statement runs — there is no "
        );
        sb.AppendLine(
            "   * event loop to resume a callback on, so this is a synchronous pause, not setTimeout. Counts "
        );
        sb.AppendLine(
            "   * against the run's own wall-clock budget. Typical use: nnz.time.sleep(nnz.api.tts.speak(line).durationMs) "
        );
        sb.AppendLine(
            "   * before nnz.api.chat.send(line), so the message lands once the line is actually spoken. */"
        );
        sb.AppendLine("  sleep(ms: number): void;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzMath {");
        sb.AppendLine("  clamp(value: number, min: number, max: number): number;");
        sb.AppendLine("  round(value: number, digits?: number): number;");
        sb.AppendLine("  lerp(a: number, b: number, t: number): number;");
        sb.AppendLine("  sum(values: number[]): number;");
        sb.AppendLine("  avg(values: number[]): number;");
        sb.AppendLine("  min(values: number[]): number;");
        sb.AppendLine("  max(values: number[]): number;");
        sb.AppendLine("  randomInt(min: number, max: number): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzStr {");
        sb.AppendLine("  padStart(value: string, length: number, pad?: string): string;");
        sb.AppendLine("  padEnd(value: string, length: number, pad?: string): string;");
        sb.AppendLine("  trim(value: string): string;");
        sb.AppendLine("  upper(value: string): string;");
        sb.AppendLine("  lower(value: string): string;");
        sb.AppendLine("  title(value: string): string;");
        sb.AppendLine("  truncate(value: string, length: number, ellipsis?: string): string;");
        sb.AppendLine("  slugify(value: string): string;");
        sb.AppendLine("  format(template: string, values: Record<string, unknown>): string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzJson {");
        sb.AppendLine("  parse(text: string): unknown;");
        sb.AppendLine("  stringify(value: unknown): string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzRandom {");
        sb.AppendLine("  int(min: number, max: number): number;");
        sb.AppendLine("  pick<T>(items: T[]): T;");
        sb.AppendLine("  shuffle<T>(items: T[]): T[];");
        sb.AppendLine("  uuid(): string;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // One named interface per nnz.api.<namespace>, plus the NnzApi interface that groups them — same
    // readability goal as AppendBatteryInterfaces, one level deeper.
    private static void AppendApiInterfaces(StringBuilder sb)
    {
        sb.AppendLine("interface NnzApiUserNamespace {");
        sb.AppendLine("  get(id?: string): NnzApiUser | null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiEconomyNamespace {");
        sb.AppendLine("  balance(userId?: string): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiChatNamespace {");
        sb.AppendLine("  send(text: string): void;");
        sb.AppendLine("  reply(text: string): void;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiMusicNamespace {");
        sb.AppendLine("  nowPlaying(): NnzApiTrack | null;");
        sb.AppendLine("  queue(uri: string): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiHttpNamespace {");
        sb.AppendLine("  fetch(url: string): string | null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Per-channel key/value state that persists between runs (64 KB per value, 200 keys). */"
        );
        sb.AppendLine("interface NnzApiStorageNamespace {");
        sb.AppendLine("  get(key: string): string | null;");
        sb.AppendLine("  set(key: string, value: string): boolean;");
        sb.AppendLine("  delete(key: string): boolean;");
        sb.AppendLine("  list(prefix?: string): string[];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Speak text on the overlay; read/assign a viewer's per-channel voice (setVoice with no voiceId clears to the channel default). */"
        );
        sb.AppendLine("interface NnzApiTtsNamespace {");
        sb.AppendLine("  speak(text: string, voiceId?: string): NnzApiTtsResult | null;");
        sb.AppendLine("  getVoice(userIdOrLogin: string): NnzApiTtsVoice | null;");
        sb.AppendLine("  setVoice(userIdOrLogin: string, voiceId?: string): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** A viewer's channel stats (messages/watchtime/first-seen/redemptions/song requests); the triggering user when no arg. */"
        );
        sb.AppendLine("interface NnzApiStatsNamespace {");
        sb.AppendLine("  viewer(userIdOrLogin?: string): NnzApiViewerStats;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Push an event to one of this channel's enabled widgets (by id or name). */"
        );
        sb.AppendLine("interface NnzApiWidgetNamespace {");
        sb.AppendLine(
            "  emit(widgetIdOrName: string, eventType: string, data?: unknown): boolean;"
        );
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Read / patch a channel-point reward (by id or title); update needs a bot-manageable reward. */"
        );
        sb.AppendLine("interface NnzApiRewardNamespace {");
        sb.AppendLine("  get(rewardIdOrTitle: string): NnzApiReward | null;");
        sb.AppendLine("  update(rewardIdOrTitle: string, patch: NnzApiRewardPatch): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Schedule a saved pipeline to run once after a delay in seconds (survives restarts); optional variables + dedupeKey (re-scheduling with the same key replaces the pending run). */"
        );
        sb.AppendLine("interface NnzApiScheduleNamespace {");
        sb.AppendLine(
            "  pipeline(pipelineName: string, delaySeconds: number, variables?: Record<string, string>, dedupeKey?: string): boolean;"
        );
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApi {");
        sb.AppendLine("  user: NnzApiUserNamespace;");
        sb.AppendLine("  economy: NnzApiEconomyNamespace;");
        sb.AppendLine("  chat: NnzApiChatNamespace;");
        sb.AppendLine("  music: NnzApiMusicNamespace;");
        sb.AppendLine("  http: NnzApiHttpNamespace;");
        sb.AppendLine("  storage: NnzApiStorageNamespace;");
        sb.AppendLine("  tts: NnzApiTtsNamespace;");
        sb.AppendLine("  stats: NnzApiStatsNamespace;");
        sb.AppendLine("  widget: NnzApiWidgetNamespace;");
        sb.AppendLine("  reward: NnzApiRewardNamespace;");
        sb.AppendLine("  schedule: NnzApiScheduleNamespace;");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}

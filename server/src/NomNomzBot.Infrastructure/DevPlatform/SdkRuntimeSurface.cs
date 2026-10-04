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
        sb.AppendLine("  /** The viewer's internal id. */");
        sb.AppendLine("  id: string;");
        sb.AppendLine("  /** The viewer's login name. */");
        sb.AppendLine("  username: string;");
        sb.AppendLine("  /** The viewer's display name as shown in chat. */");
        sb.AppendLine("  displayName: string;");
        sb.AppendLine(
            "  /** The URL of the viewer's profile picture. Absent when they have none. */"
        );
        sb.AppendLine("  avatarUrl?: string;");
        sb.AppendLine("  /** The 7TV paint this viewer wears; absent when they wear none. */");
        sb.AppendLine("  paint?: NnzApiPaint;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A 7TV name paint, ready to apply as CSS. */");
        sb.AppendLine("interface NnzApiPaint {");
        sb.AppendLine(
            "  /** The CSS background image of the paint. Absent when the paint sets none. */"
        );
        sb.AppendLine("  backgroundImage?: string;");
        sb.AppendLine("  /** The CSS text color of the paint. Absent when the paint sets none. */");
        sb.AppendLine("  color?: string;");
        sb.AppendLine(
            "  /** The CSS text shadow of the paint. Absent when the paint sets none. */"
        );
        sb.AppendLine("  textShadow?: string;");
        sb.AppendLine("  /** True when the paint is an image only. */");
        sb.AppendLine("  isImageOnly: boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiTrack {");
        sb.AppendLine("  /** The track title. */");
        sb.AppendLine("  track: string;");
        sb.AppendLine("  /** The artist name. */");
        sb.AppendLine("  artist: string;");
        sb.AppendLine("  /** The album name. null when it is not known. */");
        sb.AppendLine("  album: string | null;");
        sb.AppendLine("  /** The length of the track, in ms. */");
        sb.AppendLine("  durationMs: number;");
        sb.AppendLine("  /** How far into the track the playback is, in ms. */");
        sb.AppendLine("  progressMs: number;");
        sb.AppendLine("  /** True while the track plays. False when it is paused. */");
        sb.AppendLine("  isPlaying: boolean;");
        sb.AppendLine("  /** Who requested the track. null when nobody requested it. */");
        sb.AppendLine("  requestedBy: string | null;");
        sb.AppendLine("  /** The music service that plays the track. */");
        sb.AppendLine("  provider: string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** What nnz.api.tts.speak returns on a dispatched utterance. */");
        sb.AppendLine("interface NnzApiTtsResult {");
        sb.AppendLine("  /** The id of the voice that speaks the line. */");
        sb.AppendLine("  voiceId: string;");
        sb.AppendLine("  /** How many characters TTS accepted for the line. */");
        sb.AppendLine("  characterCount: number;");
        sb.AppendLine(
            "  /** How long the line takes to play, in ms. 0 when it was not measured (browser voice, test run). */"
        );
        sb.AppendLine("  durationMs: number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A channel-point reward as nnz.api.reward.get returns it. */");
        sb.AppendLine("interface NnzApiReward {");
        sb.AppendLine("  /** The reward's id. */");
        sb.AppendLine("  id: string;");
        sb.AppendLine("  /** The reward's title as viewers see it. */");
        sb.AppendLine("  title: string;");
        sb.AppendLine("  /** The cost in channel points. */");
        sb.AppendLine("  cost: number;");
        sb.AppendLine(
            "  /** The text viewers see when they redeem it. null when the reward has none. */"
        );
        sb.AppendLine("  prompt: string | null;");
        sb.AppendLine("  /** True when viewers can see the reward. */");
        sb.AppendLine("  isEnabled: boolean;");
        sb.AppendLine("  /** True when the reward is paused and cannot be redeemed. */");
        sb.AppendLine("  isPaused: boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** The patch nnz.api.reward.update applies — only the fields you set change. */"
        );
        sb.AppendLine("interface NnzApiRewardPatch {");
        sb.AppendLine("  /** A new title. Leave it out to keep the current one. */");
        sb.AppendLine("  title?: string;");
        sb.AppendLine(
            "  /** A new cost in channel points. Leave it out to keep the current one. */"
        );
        sb.AppendLine("  cost?: number;");
        sb.AppendLine("  /** A new prompt text. Leave it out to keep the current one. */");
        sb.AppendLine("  prompt?: string;");
        sb.AppendLine("  /** Show or hide the reward. Leave it out to keep the current state. */");
        sb.AppendLine("  isEnabled?: boolean;");
        sb.AppendLine(
            "  /** Pause or resume the reward. Leave it out to keep the current state. */"
        );
        sb.AppendLine("  isPaused?: boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** A viewer's channel stats as nnz.api.stats.viewer returns them (zeros for a never-seen viewer). */"
        );
        sb.AppendLine("interface NnzApiViewerStats {");
        sb.AppendLine(
            "  /** How many chat messages the viewer sent. 0 for a viewer never seen. */"
        );
        sb.AppendLine("  messages: number;");
        sb.AppendLine(
            "  /** How long the viewer watched, in seconds. 0 for a viewer never seen. */"
        );
        sb.AppendLine("  watchtimeSeconds: number;");
        sb.AppendLine(
            "  /** The date the viewer was first seen, as yyyy-MM-dd. null for a viewer never seen. */"
        );
        sb.AppendLine("  firstSeen: string | null;");
        sb.AppendLine("  /** How many rewards the viewer redeemed. 0 for a viewer never seen. */");
        sb.AppendLine("  redemptions: number;");
        sb.AppendLine("  /** How many songs the viewer requested. 0 for a viewer never seen. */");
        sb.AppendLine("  songRequests: number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A viewer's assigned TTS voice as nnz.api.tts.getVoice returns it. */");
        sb.AppendLine("interface NnzApiTtsVoice {");
        sb.AppendLine("  /** The id of the viewer's assigned voice. */");
        sb.AppendLine("  voiceId: string;");
        sb.AppendLine("  /** The name of the assigned voice as shown in the dashboard. */");
        sb.AppendLine("  displayName: string;");
        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Every global the Jint sandbox actually exposes to a user script: the <c>bot</c> facade and the <c>nnz</c>
    /// SDK, in the order the bootstrap defines them. There is no event bus in the sandbox — a script is invoked by
    /// the <c>run_code</c> pipeline action with args + variables — so no <c>on/once/off</c> is declared.
    /// </summary>
    public static string ScriptGlobals(IReadOnlyList<string>? triggerKeys = null)
    {
        StringBuilder sb = new();
        AppendBotFacade(sb, triggerKeys);
        sb.AppendLine();
        AppendBatteryInterfaces(sb);
        AppendApiInterfaces(sb);
        sb.AppendLine("declare const nnz: {");
        sb.AppendLine(
            "  /** The error of this script's last failed host call. null when it did not fail. Needs no grant. */"
        );
        sb.AppendLine("  readonly lastError: NnzApiError | null;");
        sb.AppendLine("  /** Unit conversion. */");
        sb.AppendLine("  units: NnzUnits;");
        sb.AppendLine("  /** Date and time helpers. */");
        sb.AppendLine("  time: NnzTime;");
        sb.AppendLine("  /** Number helpers. */");
        sb.AppendLine("  math: NnzMath;");
        sb.AppendLine("  /** Text helpers. */");
        sb.AppendLine("  str: NnzStr;");
        sb.AppendLine("  /** JSON helpers that never throw. */");
        sb.AppendLine("  json: NnzJson;");
        sb.AppendLine("  /** Random value helpers. */");
        sb.AppendLine("  random: NnzRandom;");
        sb.AppendLine(
            "  /** The calls to the bot: chat, music, storage, TTS, rewards and more. Each call needs its grant. */"
        );
        sb.AppendLine("  api: NnzApi;");
        sb.Append("};");
        return sb.ToString();
    }

    /// <summary>
    /// Every global a widget page actually has: <c>window.NomNomz</c> (the overlay SDK) and the five
    /// <c>WIDGET_*</c> values the host page injects before the bundle runs. A widget has no capability broker, so
    /// none of the <c>nnz</c> batteries or <c>nnz.api.*</c> wrappers exist here.
    /// </summary>
    public static string WidgetGlobals(
        string? customPayloadName = null,
        string settingsType = "Record<string, unknown>"
    )
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
        AppendEventMethods(sb, customPayloadName);
        sb.AppendLine(
            "  /** Fires immediately with the injected settings, then again on every dashboard change. */"
        );
        sb.AppendLine($"  onSettings(handler: (settings: {settingsType}) => void): NnzOverlaySdk;");
        sb.AppendLine(
            "  /** Logs the message and reports it to the server as a widget runtime error. */"
        );
        sb.AppendLine("  reportError(message: string): void;");
        sb.AppendLine($"  readonly settings: {settingsType};");
        sb.AppendLine("  readonly actions: NnzOverlayActions;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Pipeline actions the widget runs as the channel owner, within the owner's own permissions. */"
        );
        sb.AppendLine("interface NnzOverlayActions {");
        sb.AppendLine(
            "  /** Runs one action (e.g. 'tts_synthesize', 'song_pause') with a pipeline step's parameters. Resolves with"
        );
        sb.AppendLine(
            "   *  the outcome, also when the bot refuses or the action fails; rejects only when the overlay is offline. */"
        );
        sb.AppendLine(
            "  invoke<T extends keyof NnzActionParams>(actionType: T, params: NnzActionParams[T], variables?: Record<string, string | number>): Promise<NnzActionResult>;"
        );
        sb.AppendLine(
            "  /** Same call for an action with no required field: params may be left out. */"
        );
        sb.AppendLine(
            "  invoke<T extends NnzActionsWithOptionalParams>(actionType: T, params?: NnzActionParams[T], variables?: Record<string, string | number>): Promise<NnzActionResult>;"
        );
        sb.AppendLine(
            "  /** Resolves true only for the first open copy of this widget to claim the key (e.g. a redemption id), for"
        );
        sb.AppendLine(
            "   *  ten minutes; false when another copy already claimed it. Claim an event before acting on it, so two OBS"
        );
        sb.AppendLine("   *  sources never both act.");
        sb.AppendLine(
            "   *  REJECTS (it never resolves false for these) when the overlay is not connected to the bot or the connection"
        );
        sb.AppendLine(
            "   *  drops mid-call, and when the bot refuses the claim: not authenticated, a widget that is not this channel's"
        );
        sb.AppendLine(
            "   *  (NOT_FOUND), or an empty key or one over 200 characters (VALIDATION_FAILED). Catch it, or an offline overlay"
        );
        sb.AppendLine("   *  surfaces as an unhandled rejection. */");
        sb.AppendLine("  claim(key: string): Promise<boolean>;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzActionResult {");
        sb.AppendLine("  success: boolean;");
        sb.AppendLine("  output: string | null;");
        sb.AppendLine("  error: string | null;");
        sb.AppendLine(
            "  /** Set when the bot refused to run it: AUTH_REQUIRED (the overlay connection is not signed in),"
        );
        sb.AppendLine(
            "   *  FORBIDDEN (another channel's widget, or this widget is turned off), NOT_FOUND, VALIDATION_FAILED or"
        );
        sb.AppendLine(
            "   *  RATE_LIMITED. A refusal is not exhaustive: treat an unknown code as a refusal too. */"
        );
        sb.AppendLine("  errorCode: string | null;");
        sb.AppendLine(
            "  /** The action's context after the run, e.g. tts.audioUrl and tts.durationMs. */"
        );
        sb.AppendLine("  variables: Record<string, string>;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("declare const NomNomz: NnzOverlaySdk;");
        sb.AppendLine();
        sb.AppendLine("declare const WIDGET_ID: string;");
        sb.AppendLine("declare const WIDGET_TOKEN: string;");
        sb.AppendLine("declare const WIDGET_NAME: string;");
        sb.AppendLine($"declare const WIDGET_SETTINGS: {settingsType};");
        sb.Append("declare const WIDGET_EVENT_SUBSCRIPTIONS: string[];");
        return sb.ToString();
    }

    // on / off / onAny. With the payload registry the handler gets the payload of the event it names; a name the
    // registry does not know (a variable event name, a frame the bot relays as is) stays usable but hands the
    // handler `unknown`, never `any`.
    private static void AppendEventMethods(StringBuilder sb, string? customPayloadName)
    {
        const string Handler = "(data: unknown, eventType: string) => void";
        if (customPayloadName is null)
        {
            sb.AppendLine($"  on(eventType: string, handler: {Handler}): NnzOverlaySdk;");
            sb.AppendLine(
                "  /** Removes a handler registered with on() — pass the SAME function reference. */"
            );
            sb.AppendLine($"  off(eventType: string, handler: {Handler}): NnzOverlaySdk;");
            sb.AppendLine(
                "  onAny(handler: (eventType: string, data: unknown) => void): NnzOverlaySdk;"
            );
            return;
        }

        const string TypedHandler = "(data: NnzWidgetEventMap[K], eventType: K) => void";
        string customHandler = $"(data: {customPayloadName}, eventType: string) => void";
        sb.AppendLine(
            $"  on<K extends keyof NnzWidgetEventMap>(eventType: K, handler: {TypedHandler}): NnzOverlaySdk;"
        );
        sb.AppendLine(
            $"  on(eventType: `custom.${{string}}`, handler: {customHandler}): NnzOverlaySdk;"
        );
        sb.AppendLine($"  on(eventType: string, handler: {Handler}): NnzOverlaySdk;");
        sb.AppendLine(
            "  /** Removes a handler registered with on() — pass the SAME function reference. */"
        );
        sb.AppendLine(
            $"  off<K extends keyof NnzWidgetEventMap>(eventType: K, handler: {TypedHandler}): NnzOverlaySdk;"
        );
        sb.AppendLine(
            $"  off(eventType: `custom.${{string}}`, handler: {customHandler}): NnzOverlaySdk;"
        );
        sb.AppendLine($"  off(eventType: string, handler: {Handler}): NnzOverlaySdk;");
        sb.AppendLine("  onAny(handler: (...event: NnzWidgetAnyEvent) => void): NnzOverlaySdk;");
    }

    private static void AppendVarKeyType(StringBuilder sb, IReadOnlyList<string> keys)
    {
        sb.AppendLine("/** The variable keys this script's trigger always sets. */");
        string union =
            keys.Count == 0
                ? "never"
                : string.Join(
                    " | ",
                    keys.Select(key =>
                        key.Contains("${", StringComparison.Ordinal)
                            ? $"`{key}`"
                            : $"'{key.Replace("'", "\\'")}'"
                    )
                );
        sb.AppendLine($"type NnzVarKey = {union};");
        sb.AppendLine();
    }

    private static void AppendBotFacade(StringBuilder sb, IReadOnlyList<string>? triggerKeys)
    {
        if (triggerKeys is not null)
            AppendVarKeyType(sb, triggerKeys);

        sb.AppendLine(
            "/** The primitive-in/primitive-out host facade every script runs against. */"
        );
        sb.AppendLine("declare const bot: {");
        sb.AppendLine("  /** The arguments the trigger passed in ('!roll 20' -> ['20']). */");
        sb.AppendLine("  args: string[];");
        if (triggerKeys is null)
        {
            sb.AppendLine(
                "  /** The value of a variable the pipeline set. null when the variable does not exist. */"
            );
            sb.AppendLine("  getVar(key: string): string | null;");
        }
        else
        {
            sb.AppendLine(
                "  /** The value of a variable this trigger always sets. A key the trigger does not set is flagged as a typo. */"
            );
            sb.AppendLine("  getVar(key: NnzVarKey): string | null;");
            sb.AppendLine(
                "  /** The value of a variable set by setVar or by an earlier pipeline step. Pass true to say the key is dynamic. null when it does not exist. */"
            );
            sb.AppendLine("  getVar(key: string, dynamic: true): string | null;");
        }
        sb.AppendLine(
            "  /** Sets a variable that later pipeline steps can read. The value is stored as text. */"
        );
        sb.AppendLine("  setVar(key: string, value: string): void;");
        sb.AppendLine("  /** Appends to the script's output (capped by the execution budget). */");
        sb.AppendLine("  send(message: string): void;");
        sb.AppendLine("  /** Stops the actions after this one in the same pipeline. */");
        sb.AppendLine("  stopPipeline(): void;");
        sb.AppendLine(
            "  /** The raw capability bridge every nnz.api.* wrapper goes through; an ungranted key is denied. */"
        );
        sb.AppendLine("  call(key: string, ...args: string[]): string | null;");
        sb.AppendLine("};");
        sb.AppendLine();
        sb.AppendLine(
            "/** Writes lines to the test run panel. Viewers never see them. Objects are shown as JSON. */"
        );
        sb.AppendLine("declare const console: {");
        sb.AppendLine("  /** Writes one line to the test run panel. */");
        sb.AppendLine("  log(...values: unknown[]): void;");
        sb.AppendLine("  /** Writes one line to the test run panel. Same as log. */");
        sb.AppendLine("  info(...values: unknown[]): void;");
        sb.AppendLine("  /** Marks the line with \"warn:\". */");
        sb.AppendLine("  warn(...values: unknown[]): void;");
        sb.AppendLine("  /** Marks the line with \"error:\". */");
        sb.AppendLine("  error(...values: unknown[]): void;");
        sb.AppendLine("};");
    }

    // One named interface per nnz.<namespace> — hovering `nnz` itself now shows six short type references
    // instead of the whole batteries tree inline (owner: "a better type definition than it being a big
    // object"), and hovering e.g. `nnz.time` shows the named NnzTime signature directly.
    private static void AppendBatteryInterfaces(StringBuilder sb)
    {
        sb.AppendLine("interface NnzUnits {");
        sb.AppendLine(
            "  /** Converts a value between units, for example convert(10, 'km', 'mi'). Returns NaN when the units do not match. */"
        );
        sb.AppendLine("  convert(value: number, from: string, to: string): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzTime {");
        sb.AppendLine("  /** The current time as an ISO 8601 string. */");
        sb.AppendLine("  now(): string;");
        sb.AppendLine(
            "  /** Turns an ISO 8601 string into epoch ms. Returns NaN when the text is not a date. */"
        );
        sb.AppendLine("  parse(iso: string): number;");
        sb.AppendLine("  /** Turns epoch ms into an ISO 8601 string. */");
        sb.AppendLine("  format(epochMs: number): string;");
        sb.AppendLine(
            "  /** Adds ms to an ISO 8601 time and returns the new ISO 8601 time. Use a negative number to go back. */"
        );
        sb.AppendLine("  add(iso: string, ms: number): string;");
        sb.AppendLine(
            "  /** The difference a minus b, in ms. It is negative when a is earlier. */"
        );
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
        sb.AppendLine("  /** Limits the value to the range min to max. */");
        sb.AppendLine("  clamp(value: number, min: number, max: number): number;");
        sb.AppendLine(
            "  /** Rounds the value to digits decimal places. digits is optional and defaults to 0. */"
        );
        sb.AppendLine("  round(value: number, digits?: number): number;");
        sb.AppendLine(
            "  /** The point between a and b at position t. t = 0 gives a and t = 1 gives b. */"
        );
        sb.AppendLine("  lerp(a: number, b: number, t: number): number;");
        sb.AppendLine("  /** The total of all values. */");
        sb.AppendLine("  sum(values: number[]): number;");
        sb.AppendLine("  /** The average of all values. 0 for an empty list. */");
        sb.AppendLine("  avg(values: number[]): number;");
        sb.AppendLine("  /** The smallest value. */");
        sb.AppendLine("  min(values: number[]): number;");
        sb.AppendLine("  /** The largest value. */");
        sb.AppendLine("  max(values: number[]): number;");
        sb.AppendLine(
            "  /** A random whole number from min to max, both included. The ends can be in either order. Returns NaN when there is no whole number between them, for example randomInt(1.2, 1.8). */"
        );
        sb.AppendLine("  randomInt(min: number, max: number): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzStr {");
        sb.AppendLine(
            "  /** Pads the start of the text up to length. pad is optional and defaults to a space. */"
        );
        sb.AppendLine("  padStart(value: string, length: number, pad?: string): string;");
        sb.AppendLine(
            "  /** Pads the end of the text up to length. pad is optional and defaults to a space. */"
        );
        sb.AppendLine("  padEnd(value: string, length: number, pad?: string): string;");
        sb.AppendLine("  /** Removes spaces from both ends of the text. */");
        sb.AppendLine("  trim(value: string): string;");
        sb.AppendLine("  /** The text in upper case. */");
        sb.AppendLine("  upper(value: string): string;");
        sb.AppendLine("  /** The text in lower case. */");
        sb.AppendLine("  lower(value: string): string;");
        sb.AppendLine("  /** The text with each word starting in upper case. */");
        sb.AppendLine("  title(value: string): string;");
        sb.AppendLine(
            "  /** Cuts the text to length characters, ellipsis included. ellipsis is optional and defaults to the ellipsis character. */"
        );
        sb.AppendLine("  truncate(value: string, length: number, ellipsis?: string): string;");
        sb.AppendLine(
            "  /** Turns the text into a lower case slug with dashes, for example 'Hello World' becomes 'hello-world'. */"
        );
        sb.AppendLine("  slugify(value: string): string;");
        sb.AppendLine(
            "  /** Fills each {name} in the template from values. A name with no value stays as it is. */"
        );
        sb.AppendLine("  format(template: string, values: Record<string, unknown>): string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzJson {");
        sb.AppendLine("  /** Parses JSON text. Returns null when the text is not valid JSON. */");
        sb.AppendLine("  parse(text: string): unknown;");
        sb.AppendLine(
            "  /** Turns a value into JSON text. Returns 'null' when the value cannot be serialized. */"
        );
        sb.AppendLine("  stringify(value: unknown): string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzRandom {");
        sb.AppendLine(
            "  /** A random whole number from min to max, both included. The ends can be in either order. Returns NaN when there is no whole number between them, for example int(1.2, 1.8). */"
        );
        sb.AppendLine("  int(min: number, max: number): number;");
        sb.AppendLine("  /** A random item, or undefined when the list is empty. */");
        sb.AppendLine("  pick<T>(items: readonly T[]): T | undefined;");
        sb.AppendLine("  /** A shuffled copy of the list. The original list stays as it is. */");
        sb.AppendLine("  shuffle<T>(items: readonly T[]): T[];");
        sb.AppendLine("  /** A random version 4 UUID. */");
        sb.AppendLine("  uuid(): string;");
        sb.AppendLine("}");
        sb.AppendLine();
    }

    // One named interface per nnz.api.<namespace>, plus the NnzApi interface that groups them — same
    // readability goal as AppendBatteryInterfaces, one level deeper.
    private static void AppendApiInterfaces(StringBuilder sb)
    {
        sb.AppendLine("interface NnzApiUserNamespace {");
        sb.AppendLine(
            "  /** A viewer's public profile, found by id, login or internal id. id is optional and defaults to the user who triggered the script. null when no user matches. */"
        );
        sb.AppendLine("  get(id?: string): NnzApiUser | null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiEconomyNamespace {");
        sb.AppendLine(
            "  /** A viewer's balance in this channel's currency. userId is optional and defaults to the user who triggered the script. 0 when no viewer matches. */"
        );
        sb.AppendLine("  balance(userId?: string): number;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiChatNamespace {");
        sb.AppendLine("  /** Says a line in chat as the bot. */");
        sb.AppendLine("  send(text: string): void;");
        sb.AppendLine(
            "  /** Answers the chat message that started this script, as a threaded reply. If the platform"
        );
        sb.AppendLine(
            "   *  refuses the thread, it mentions the viewer instead. With no chat message, it says a normal line. */"
        );
        sb.AppendLine("  reply(text: string): void;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiMusicNamespace {");
        sb.AppendLine("  /** The track that plays now. null when nothing plays. */");
        sb.AppendLine("  nowPlaying(): NnzApiTrack | null;");
        sb.AppendLine(
            "  /** Requests a song by title, artist or link. Returns true when it was queued and false when it was refused. */"
        );
        sb.AppendLine("  queue(uri: string): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiHttpNamespace {");
        sb.AppendLine(
            "  /** Fetches an https URL and returns the response body as text. Returns null when the URL is not https, the request is blocked or the server does not answer with success. */"
        );
        sb.AppendLine("  fetch(url: string): string | null;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Per-channel key/value state that persists between runs (64 KB per value, 200 keys). */"
        );
        sb.AppendLine("interface NnzApiStorageNamespace {");
        sb.AppendLine(
            "  /** The stored value for the key. null when nothing is stored under it. */"
        );
        sb.AppendLine("  get(key: string): string | null;");
        sb.AppendLine(
            "  /** Stores a text value under the key. Returns false when the write is refused, for example over the size limit. */"
        );
        sb.AppendLine("  set(key: string, value: string): boolean;");
        sb.AppendLine("  /** Deletes the key. Returns true when the call succeeded. */");
        sb.AppendLine("  delete(key: string): boolean;");
        sb.AppendLine(
            "  /** The stored keys. prefix is optional and keeps only keys that start with it. An empty list when none match. */"
        );
        sb.AppendLine("  list(prefix?: string): string[];");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Speak text on the overlay; read/assign a viewer's per-channel voice (setVoice with no voiceId clears to the channel default). */"
        );
        sb.AppendLine("interface NnzApiTtsNamespace {");
        sb.AppendLine(
            "  /** Leave voiceId, ratePercent or pitchPercent undefined to keep the normal value. Null when TTS refused the line. */"
        );
        sb.AppendLine(
            "  speak(text: string, voiceId?: string, ratePercent?: number, pitchPercent?: number): NnzApiTtsResult | null;"
        );
        sb.AppendLine(
            "  /** The voice assigned to a viewer. userIdOrLogin is optional and defaults to the user who triggered the script. null when the viewer uses the channel default or no viewer matches. */"
        );
        sb.AppendLine("  getVoice(userIdOrLogin?: string): NnzApiTtsVoice | null;");
        sb.AppendLine(
            "  /** Assigns a voice to a viewer. voiceId is optional: leave it out to clear back to the channel default. Returns false when it fails. */"
        );
        sb.AppendLine("  setVoice(userIdOrLogin: string, voiceId?: string): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** A viewer's channel stats (messages/watchtime/first-seen/redemptions/song requests); the triggering user when no arg. */"
        );
        sb.AppendLine("interface NnzApiStatsNamespace {");
        sb.AppendLine(
            "  /** A viewer's channel stats. userIdOrLogin is optional and defaults to the user who triggered the script. A viewer never seen gets zeros. */"
        );
        sb.AppendLine("  viewer(userIdOrLogin?: string): NnzApiViewerStats;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Push an event to one of this channel's enabled widgets (by id or name). */"
        );
        sb.AppendLine("interface NnzApiWidgetNamespace {");
        sb.AppendLine(
            "  /** Sends an event to a widget. data is optional. Returns false when no widget matches, the widget is turned off, no browser source has it open, or the send fails. */"
        );
        sb.AppendLine(
            "  emit(widgetIdOrName: string, eventType: string, data?: unknown): boolean;"
        );
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Read / patch a channel-point reward (by id or title); update needs a bot-manageable reward. */"
        );
        sb.AppendLine("interface NnzApiRewardNamespace {");
        sb.AppendLine(
            "  /** A channel-point reward, found by id or title. null when no reward matches. */"
        );
        sb.AppendLine("  get(rewardIdOrTitle: string): NnzApiReward | null;");
        sb.AppendLine(
            "  /** Applies the patch to a reward. Returns false when no reward matches, the bot cannot manage it or the update fails. */"
        );
        sb.AppendLine("  update(rewardIdOrTitle: string, patch: NnzApiRewardPatch): boolean;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** Schedule a saved pipeline to run once after a delay in seconds, rounded up to whole seconds (survives restarts); optional variables + dedupeKey (re-scheduling with the same key replaces the pending run). */"
        );
        sb.AppendLine("interface NnzApiScheduleNamespace {");
        sb.AppendLine(
            "  /** Runs a saved pipeline once after the delay. variables and dedupeKey are optional. Returns false when the pipeline is not found or the schedule is refused. */"
        );
        sb.AppendLine(
            "  pipeline(pipelineName: string, delaySeconds: number, variables?: Record<string, string>, dedupeKey?: string): boolean;"
        );
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("/** A failed host call, as nnz.lastError returns it. */");
        sb.AppendLine("interface NnzApiError {");
        sb.AppendLine("  /** Why the call failed. */");
        sb.AppendLine(
            "  code: 'invalid_argument' | 'not_found' | 'refused' | 'rate_limited' | 'limit_exceeded' | 'upstream_failed';"
        );
        sb.AppendLine("  /** A human-readable reason. */");
        sb.AppendLine("  message: string;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine(
            "/** What nnz.api.actions.invoke returns for one run of a pipeline action. */"
        );
        sb.AppendLine("interface NnzApiActionResult {");
        sb.AppendLine("  /** True when the action ran to completion. */");
        sb.AppendLine("  success: boolean;");
        sb.AppendLine("  /** The action's text output. null when it produced none. */");
        sb.AppendLine("  output: string | null;");
        sb.AppendLine("  /** Why the action failed. null when it succeeded. */");
        sb.AppendLine("  error: string | null;");
        sb.AppendLine("  /** The variables the action set, by name. */");
        sb.AppendLine("  variables: Record<string, string>;");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApiActionsNamespace {");
        sb.AppendLine(
            "  /** Runs one pipeline action by its type, for example 'tts_synthesize'. The type must be a string literal: the grant is per type. */"
        );
        sb.AppendLine(
            "  invoke<T extends keyof NnzActionParams>(actionType: T, params: NnzActionParams[T], variables?: Record<string, string | number>): NnzApiActionResult;"
        );
        sb.AppendLine(
            "  /** Same call for an action with no required field: params may be left out. */"
        );
        sb.AppendLine(
            "  invoke<T extends NnzActionsWithOptionalParams>(actionType: T, params?: NnzActionParams[T], variables?: Record<string, string | number>): NnzApiActionResult;"
        );
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("interface NnzApi {");
        sb.AppendLine("  /** Runs a pipeline action by its type. */");
        sb.AppendLine("  actions: NnzApiActionsNamespace;");
        sb.AppendLine("  /** Read a viewer's public profile. */");
        sb.AppendLine("  user: NnzApiUserNamespace;");
        sb.AppendLine("  /** Read a viewer's currency balance. */");
        sb.AppendLine("  economy: NnzApiEconomyNamespace;");
        sb.AppendLine("  /** Send chat messages as the bot. */");
        sb.AppendLine("  chat: NnzApiChatNamespace;");
        sb.AppendLine("  /** Read the playing track and request songs. */");
        sb.AppendLine("  music: NnzApiMusicNamespace;");
        sb.AppendLine("  /** Fetch an https URL. */");
        sb.AppendLine("  http: NnzApiHttpNamespace;");
        sb.AppendLine("  /** Per-channel storage that persists between runs. */");
        sb.AppendLine("  storage: NnzApiStorageNamespace;");
        sb.AppendLine("  /** Speak text on the overlay and manage viewer voices. */");
        sb.AppendLine("  tts: NnzApiTtsNamespace;");
        sb.AppendLine("  /** Read a viewer's channel stats. */");
        sb.AppendLine("  stats: NnzApiStatsNamespace;");
        sb.AppendLine("  /** Send events to this channel's widgets. */");
        sb.AppendLine("  widget: NnzApiWidgetNamespace;");
        sb.AppendLine("  /** Read and update channel-point rewards. */");
        sb.AppendLine("  reward: NnzApiRewardNamespace;");
        sb.AppendLine("  /** Run a saved pipeline later. */");
        sb.AppendLine("  schedule: NnzApiScheduleNamespace;");
        sb.AppendLine("}");
        sb.AppendLine();
    }
}

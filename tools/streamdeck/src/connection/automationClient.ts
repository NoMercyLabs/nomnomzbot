// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import WebSocket from "ws";
import type { JsonObject } from "@elgato/utils";
import { getPairingState, setPairingState, clearPairingState, needsRefresh, isExpired, type PairingState } from "./tokenStore.js";

export interface NowPlayingPayload extends JsonObject {
  title: string | null;
  artist: string | null;
  durationMs: number;
  positionMs: number;
  isPlaying: boolean;
  shuffleEnabled: boolean;
  repeatMode: string;
  isSaved: boolean | null;
  serverTime: string;
  volumePercent: number;
  albumArtUrl: string | null;
  // Live per-action permissions from the provider (Spotify's actions.disallows): an ad break, a
  // restricted market, or a non-Premium account can block a control the provider generally supports.
  // Optional — an older backend build simply omits them, and every key treats "missing" as permitted.
  canSetShuffle?: boolean;
  canSetRepeat?: boolean;
  canSkipNext?: boolean;
  canSkipPrevious?: boolean;
  canSeek?: boolean;
  canPause?: boolean;
  canResume?: boolean;
}

export interface DevicePayload extends JsonObject {
  id: string;
  name: string;
  type: string;
  isActive: boolean;
  volumePercent: number | null;
}

export interface PlaylistPayload extends JsonObject {
  id: string;
  name: string;
  uri: string;
  trackCount: number;
  imageUrl: string | null;
}

/** The project-wide response envelope (StatusResponseDto&lt;T&gt;): {@code status: "ok"|"error"} +
 * {@code message} on failure. There is no {@code success} boolean or {@code errorCode} on the wire —
 * error KIND (expired token vs. forbidden vs. not found, …) is conveyed purely via HTTP status. */
interface StatusResponse<T> {
  status: string;
  data?: T;
  message?: string;
}

export class AutomationApiError extends Error {
  constructor(
    message: string,
    public readonly errorCode: string | undefined,
  ) {
    super(message);
  }
}

/**
 * The plugin process's single connection to the Automation API (streamdeck-plugin.md P2/P6/P7):
 * one REST client (invoke + reads + self-refresh) and one WS subscription to `song.changed`, shared
 * by every action instance. Token lifecycle (D8) is handled transparently here — every REST call
 * refreshes proactively when needed and clears state on TOKEN_EXPIRED/TOKEN_REVOKED.
 */
/** How often to re-fetch now-playing as a fallback, independent of the WS push — long enough to be
 * cheap, short enough that a missed/dropped `song.changed` frame never leaves a key stale for long. */
const RESYNC_INTERVAL_MS = 15_000;

/** How long a REST call is allowed to hang before it's treated as failed. `fetch` has no default
 * timeout, so a connection that goes silently dead mid-request (machine sleep/wake, VPN toggle, a
 * NAT/firewall dropping an idle keep-alive with no RST) never resolves and never rejects — the
 * request just hangs forever, and every layer built on top of it (the resync fallback, token
 * refresh) hangs with it. This is what turned "the WS dropped" into "stuck until the plugin is
 * manually restarted": the fallback that was supposed to self-heal was itself capable of hanging
 * with no way out. */
const REQUEST_TIMEOUT_MS = 10_000;

/** How long the WS connection can go without ANY inbound frame (event, or the transport's own
 * ping/pong) before it's declared dead and force-reconnected. A half-dead TCP connection can sit
 * "open" from Node's point of view indefinitely — no `close`/`error` event ever fires — because
 * nothing at the OS or protocol layer guarantees a timely RST when a network path silently drops
 * packets. `ws` auto-answers server pings with no application code required, so any inbound frame
 * (a real event OR a bare ping) resets this watchdog; only true silence trips it. Comfortably under
 * the server's own idle keep-alive cadence so this fires first. */
const WS_IDLE_TIMEOUT_MS = 45_000;

/** Timing knobs, injectable so tests can prove the watchdog/timeout behavior at real (fast) speed
 * instead of waiting out production-length windows. The exported singleton below uses the real
 * defaults; only tests construct an {@link AutomationClient} directly with shorter ones. */
export interface AutomationClientTiming {
  requestTimeoutMs: number;
  wsIdleTimeoutMs: number;
  wsWatchdogIntervalMs: number;
}

const DEFAULT_TIMING: AutomationClientTiming = {
  requestTimeoutMs: REQUEST_TIMEOUT_MS,
  wsIdleTimeoutMs: WS_IDLE_TIMEOUT_MS,
  wsWatchdogIntervalMs: 5_000,
};

export class AutomationClient {
  private ws: WebSocket | null = null;
  private reconnectDelayMs = 1000;
  private resyncTimer: ReturnType<typeof setInterval> | null = null;
  private wsWatchdogTimer: ReturnType<typeof setInterval> | null = null;
  private wsLastActivityAt = 0;
  private nowPlayingListeners = new Set<(payload: NowPlayingPayload) => void>();
  private disconnectListeners = new Set<() => void>();
  private readonly timing: AutomationClientTiming;

  constructor(timing: Partial<AutomationClientTiming> = {}) {
    this.timing = { ...DEFAULT_TIMING, ...timing };
  }

  async isPaired(): Promise<boolean> {
    return (await getPairingState()) !== null;
  }

  onNowPlaying(listener: (payload: NowPlayingPayload) => void): void {
    this.nowPlayingListeners.add(listener);
  }

  /** Fired when pairing is lost (hard expiry / revocation) so keys can revert to "not connected". */
  onDisconnected(listener: () => void): void {
    this.disconnectListeners.add(listener);
  }

  /**
   * `POST /automation/v1/invoke` resolves a PIPELINE by id/name (automation-api.md §3
   * `AutomationInvokeRequest`), not a raw action type — there is no "run this one action directly"
   * endpoint. This client invokes by NAME, assuming a pipeline named identically to `actionType`
   * (e.g. "music_play_pause") exists on the broadcaster's channel with exactly that one music_* step.
   * The backend auto-provisions one such pipeline per registered music_* action the first time a
   * device pairs with `Device.Kind === "streamdeck"` (AutomationPairingService), so a fresh install
   * needs zero manual dashboard setup.
   */
  async invoke(actionType: string, params: Record<string, unknown> = {}): Promise<void> {
    await this.request("POST", "/automation/v1/invoke", {
      pipelineName: actionType,
      variables: Object.fromEntries(Object.entries(params).map(([k, v]) => [k, String(v)])),
    });
  }

  async getNowPlaying(): Promise<NowPlayingPayload> {
    return this.request<NowPlayingPayload>("GET", "/automation/v1/music/now-playing");
  }

  async getDevices(): Promise<DevicePayload[]> {
    return this.request<DevicePayload[]>("GET", "/automation/v1/music/devices");
  }

  async getPlaylists(limit = 20, offset = 0): Promise<PlaylistPayload[]> {
    return this.request<PlaylistPayload[]>(
      "GET",
      `/automation/v1/music/playlists?limit=${limit}&offset=${offset}`,
    );
  }

  /** Connects (or reconnects) the WS subscription for `song.changed`. Idempotent.
   *
   * A key rendered before the first `song.changed` frame arrives (e.g. right after pairing, or a
   * key that appears while nothing on Spotify happens to be changing) would otherwise sit on its
   * default icon forever — nothing has "changed" yet from the WS's point of view. So every
   * (re)connect seeds state with one real GET, and a periodic resync (independent of any single
   * missed/dropped event) keeps it honest afterward. */
  async connectStream(): Promise<void> {
    const state = await getPairingState();
    if (!state || this.ws) return;

    const wsUrl = state.backendUrl.replace(/^http/, "ws") + "/automation/v1/stream";
    const socket = new WebSocket(wsUrl);
    this.ws = socket;
    this.wsLastActivityAt = Date.now();

    socket.on("open", () => {
      this.reconnectDelayMs = 1000;
      socket.send(JSON.stringify({ op: "authenticate", id: "auth", token: state.token }));
      socket.send(JSON.stringify({ op: "subscribe", id: "sub", events: ["song.changed"] }));
      void this.resyncNowPlaying();
      this.startResyncTimer();
      this.startWsWatchdog(socket);
    });
    // Any inbound frame proves the connection is alive — including the bare pings `ws` answers on
    // its own — so both are tracked here as watchdog activity, not just real event frames.
    socket.on("ping", () => {
      this.wsLastActivityAt = Date.now();
    });
    socket.on("message", (raw: WebSocket.RawData) => {
      this.wsLastActivityAt = Date.now();
      try {
        // automation-api.md §4.2: a pushed event frame is {op:"event", type, data}, NOT the
        // {event, payload} shape this used to check — which meant song.changed pushes were parsed
        // but never matched, and every "live" update actually came from the periodic REST resync.
        const msg = JSON.parse(raw.toString()) as { op?: string; type?: string; data?: unknown };
        if (msg.op === "event" && msg.type === "song.changed" && msg.data) {
          const payload = msg.data as NowPlayingPayload;
          for (const listener of this.nowPlayingListeners) listener(payload);
        }
      } catch {
        // malformed frame — ignore, the next one will be fine
      }
    });
    socket.on("close", () => {
      this.ws = null;
      this.stopResyncTimer();
      this.stopWsWatchdog();
      setTimeout(() => void this.connectStream(), this.reconnectDelayMs);
      this.reconnectDelayMs = Math.min(this.reconnectDelayMs * 2, 30_000);
    });
    socket.on("error", () => socket.close());
  }

  /** One-shot GET fallback for whenever a `song.changed` push can't be relied on. Silent on failure
   * (not paired, transient network blip) — the next scheduled resync or the next real event covers it. */
  private async resyncNowPlaying(): Promise<void> {
    const payload = await this.getNowPlaying().catch(() => null);
    if (!payload) return;
    for (const listener of this.nowPlayingListeners) listener(payload);
  }

  private startResyncTimer(): void {
    this.stopResyncTimer();
    this.resyncTimer = setInterval(() => void this.resyncNowPlaying(), RESYNC_INTERVAL_MS);
  }

  private stopResyncTimer(): void {
    if (this.resyncTimer) clearInterval(this.resyncTimer);
    this.resyncTimer = null;
  }

  /** Declares the socket dead and forces it closed once it's gone `WS_IDLE_TIMEOUT_MS` without any
   * inbound frame — `terminate()`, not `close()`, because a half-dead connection may never complete
   * a graceful close handshake either; only `terminate()` guarantees the `close` event fires so the
   * existing reconnect-with-backoff logic actually runs. Checked on a short interval rather than one
   * timer per expected ping so a single missed ping doesn't itself trip a reconnect — only sustained
   * silence does. */
  private startWsWatchdog(socket: WebSocket): void {
    this.stopWsWatchdog();
    this.wsWatchdogTimer = setInterval(() => {
      if (Date.now() - this.wsLastActivityAt > this.timing.wsIdleTimeoutMs) {
        socket.terminate();
      }
    }, this.timing.wsWatchdogIntervalMs);
  }

  private stopWsWatchdog(): void {
    if (this.wsWatchdogTimer) clearInterval(this.wsWatchdogTimer);
    this.wsWatchdogTimer = null;
  }

  /** D7/D8 startup + daily check: proactively refresh under the threshold, react to hard expiry. */
  async ensureFreshToken(): Promise<void> {
    const state = await getPairingState();
    if (!state) return;
    if (isExpired(state)) {
      await clearPairingState();
      for (const listener of this.disconnectListeners) listener();
      return;
    }
    if (needsRefresh(state)) {
      await this.refreshToken(state);
    }
  }

  private async refreshToken(state: PairingState): Promise<void> {
    try {
      const response = await fetch(`${state.backendUrl}/automation/v1/refresh`, {
        method: "POST",
        headers: { Authorization: `Bearer ${state.token}` },
        signal: AbortSignal.timeout(this.timing.requestTimeoutMs),
      });
      const body = (await response.json()) as StatusResponse<{
        secret: string;
        token: { expiresAt: string };
      }>;
      if (!response.ok || body.status !== "ok" || !body.data) return;
      await setPairingState({
        ...state,
        token: body.data.secret,
        tokenExpiresAt: body.data.token.expiresAt,
      });
    } catch {
      // network hiccup — the next hourly check retries; the token is still valid until it isn't
    }
  }

  private async request<T>(method: "GET" | "POST", path: string, body?: unknown): Promise<T> {
    const state = await getPairingState();
    if (!state) throw new AutomationApiError("Not paired.", "NOT_PAIRED");

    const response = await fetch(`${state.backendUrl}${path}`, {
      method,
      headers: {
        Authorization: `Bearer ${state.token}`,
        "Content-Type": "application/json",
      },
      body: body ? JSON.stringify(body) : undefined,
      signal: AbortSignal.timeout(this.timing.requestTimeoutMs),
    });
    const parsed = (await response.json().catch(() => null)) as StatusResponse<T> | null;

    // Error KIND is conveyed by HTTP status alone (BaseController.ResultResponse) — 401 is the only
    // one that means "this token is dead", whatever server-internal reason caused it.
    if (response.status === 401) {
      await clearPairingState();
      for (const listener of this.disconnectListeners) listener();
      throw new AutomationApiError(parsed?.message ?? "Token no longer valid.", undefined);
    }
    if (!response.ok || parsed?.status !== "ok") {
      throw new AutomationApiError(parsed?.message ?? `Request failed (${response.status}).`, undefined);
    }
    return parsed.data as T;
  }
}

export const automationClient = new AutomationClient();

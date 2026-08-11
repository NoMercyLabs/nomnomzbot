// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import streamDeck, {
  SingletonAction,
  type KeyDownEvent,
  type WillAppearEvent,
  type DidReceiveSettingsEvent,
  type SendToPluginEvent,
} from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";
import { automationClient, AutomationApiError } from "../connection/automationClient.js";
import { getPairingState, getHost, setHost } from "../connection/tokenStore.js";
import { getDeviceFlowStatus, onDeviceFlowStatusChange } from "../connection/deviceFlowState.js";
import { renderIconKey, DEFAULT_BACKGROUND } from "../nowPlaying/keyRenderer.js";
import { nowPlayingState } from "../nowPlaying/state.js";

interface PiRequest extends JsonObject {
  type: "getPairingStatus" | "getHost" | "setHost" | "listDevices" | "listPlaylists";
  host?: string;
}

/** Every action's Settings may carry a per-key background color, picked in its property inspector. */
function backgroundColorOf(settings: JsonObject): string {
  const value = (settings as { backgroundColor?: unknown }).backgroundColor;
  return typeof value === "string" && value.length > 0 ? value : DEFAULT_BACKGROUND;
}

/**
 * Shared base for every "NomNomzBot: Music" tray action (streamdeck-plugin.md §2). One backend
 * `music_*` type per subclass; each is a thin SDK wrapper — resolve this key's Settings into invoke
 * params, call the action, flash red with the real reason on failure. Never a silent no-op.
 */
export abstract class MusicAction<TSettings extends JsonObject = JsonObject> extends SingletonAction<TSettings> {
  protected abstract readonly actionType: string;

  /** Manifest base name of this action's icon (streamdeck-plugin.md's icon pack, e.g. "play"). */
  protected abstract readonly iconName: string;

  /** Override for a key whose icon depends on live now-playing state (shuffle on/off, saved, …). */
  protected getIconName(_settings: TSettings): string {
    return this.iconName;
  }

  /** Override when this key's action is currently blocked by the provider (Spotify's live
   * `actions.disallows` — an ad break, a restricted market, a non-Premium account) — dims the icon
   * instead of letting the key silently fail on the next press. Undefined by default: most keys
   * (volume, device transfer, playlist actions, …) have no corresponding disallows flag. */
  protected isBlockedByProvider(_settings: TSettings): boolean {
    return false;
  }

  /** Override to map this key's Settings (device/playlist id, volume, seek position, …) to invoke params. */
  protected resolveParams(_settings: TSettings): Record<string, unknown> {
    return {};
  }

  // Subscribes once per key here rather than in every subclass: any key whose icon or blocked-state
  // depends on live now-playing data (getIconName/isBlockedByProvider overrides) redraws itself on
  // every song.changed push automatically instead of each action re-wiring the same listener.
  override async onWillAppear(ev: WillAppearEvent<TSettings>): Promise<void> {
    await this.render(ev.action, ev.payload.settings);
    nowPlayingState.onChange(() => void this.render(ev.action, ev.payload.settings));
  }

  override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<TSettings>): Promise<void> {
    await this.render(ev.action, ev.payload.settings);
  }

  protected async render(action: WillAppearEvent<TSettings>["action"], settings: TSettings): Promise<void> {
    await action.setImage(
      renderIconKey(this.getIconName(settings), backgroundColorOf(settings), this.isBlockedByProvider(settings)),
    );
  }

  override async onKeyDown(ev: KeyDownEvent<TSettings>): Promise<void> {
    if (!(await automationClient.isPaired())) {
      await ev.action.showAlert();
      return;
    }
    try {
      await automationClient.invoke(this.actionType, this.resolveParams(ev.payload.settings));
    } catch (error) {
      const reason = error instanceof AutomationApiError ? (error.errorCode ?? error.message) : "unknown error";
      streamDeck.logger.warn(`${this.actionType} failed: ${reason}`);
      await ev.action.showAlert();
    }
  }

  /** Property-inspector requests (streamdeck-plugin.md P5) — the PI can't hold the bearer token
   * itself, so device/playlist reads are relayed through here. Pairing is entirely plugin-driven
   * (device flow, D9): the PI only reads status and the one host setting, never redeems anything
   * itself. Payload type is the base class's JsonValue (override signatures can't narrow it), cast
   * once inside. */
  override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, TSettings>): Promise<void> {
    const request = ev.payload as PiRequest;
    if (request.type === "getPairingStatus") {
      const [state, flow] = await Promise.all([getPairingState(), Promise.resolve(getDeviceFlowStatus())]);
      await streamDeck.ui.sendToPropertyInspector({
        type: "pairingStatus",
        paired: state !== null,
        tokenExpiresAt: state?.tokenExpiresAt ?? null,
        verificationUri: flow.verificationUri,
        lastError: flow.lastError,
      });
      onDeviceFlowStatusChange((status) =>
        void streamDeck.ui.sendToPropertyInspector({
          type: "pairingStatus",
          paired: status.paired,
          tokenExpiresAt: status.tokenExpiresAt,
          verificationUri: status.verificationUri,
          lastError: status.lastError,
        }),
      );
    } else if (request.type === "getHost") {
      await streamDeck.ui.sendToPropertyInspector({ type: "host", host: await getHost() });
    } else if (request.type === "setHost" && request.host) {
      await setHost(request.host);
    } else if (request.type === "listDevices") {
      const devices = await automationClient.getDevices().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "devices", devices });
    } else if (request.type === "listPlaylists") {
      const playlists = await automationClient.getPlaylists().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "playlists", playlists });
    }
  }
}

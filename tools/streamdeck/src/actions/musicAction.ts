// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import streamDeck, { SingletonAction, type KeyDownEvent, type WillAppearEvent, type SendToPluginEvent} from "@elgato/streamdeck";
import type { JsonObject, JsonValue } from "@elgato/utils";
import { automationClient, AutomationApiError } from "../connection/automationClient.js";
import { redeemCodeManually } from "../connection/pairing.js";
import { getPairingState } from "../connection/tokenStore.js";

interface PiRequest extends JsonObject {
  type: "getPairingStatus" | "redeemCode" | "listDevices" | "listPlaylists";
  code?: string;
  backendUrl?: string;
}


/**
 * Shared base for every "NomNomzBot: Music" tray action (streamdeck-plugin.md §2). One backend
 * `music_*` type per subclass; each is a thin SDK wrapper — resolve this key's Settings into invoke
 * params, call the action, flash red with the real reason on failure. Never a silent no-op.
 */
export abstract class MusicAction<TSettings extends JsonObject = JsonObject> extends SingletonAction<TSettings> {
  protected abstract readonly actionType: string;

  /** Override to map this key's Settings (device/playlist id, volume, seek position, …) to invoke params. */
  protected resolveParams(_settings: TSettings): Record<string, unknown> {
    return {};
  }

  override async onWillAppear(ev: WillAppearEvent<TSettings>): Promise<void> {
    if (!(await automationClient.isPaired())) {
      await ev.action.setTitle("Connect in\ndashboard");
    }
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
   * itself, so device/playlist reads and manual-code redemption are relayed through here. Payload
   * type is the base class's JsonValue (override signatures can't narrow it), cast once inside. */
  override async onSendToPlugin(ev: SendToPluginEvent<JsonValue, TSettings>): Promise<void> {
    const request = ev.payload as PiRequest;
    if (request.type === "getPairingStatus") {
      const state = await getPairingState();
      await streamDeck.ui.sendToPropertyInspector({
        type: "pairingStatus",
        paired: state !== null,
        tokenExpiresAt: state?.tokenExpiresAt ?? null,
      });
    } else if (request.type === "redeemCode" && request.code && request.backendUrl) {
      await redeemCodeManually(request.backendUrl, request.code);
      const state = await getPairingState();
      await streamDeck.ui.sendToPropertyInspector({ type: "pairingStatus", paired: state !== null });
    } else if (request.type === "listDevices") {
      const devices = await automationClient.getDevices().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "devices", devices });
    } else if (request.type === "listPlaylists") {
      const playlists = await automationClient.getPlaylists().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "playlists", playlists });
    }
  }
}

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
import {
  automationClient,
  AutomationApiError,
  getPairingState,
  getHost,
  setHost,
  getDeviceFlowStatus,
  onDeviceFlowStatusChange,
} from "@nomnomzbot/streamdeck-shared";
import { renderIconKey, DEFAULT_BACKGROUND } from "../keyRenderer.js";

interface PiRequest extends JsonObject {
  type:
    | "getPairingStatus"
    | "getHost"
    | "setHost"
    | "listScenes"
    | "listInputs"
    | "listSceneItems"
    | "listSceneTransitions"
    | "listSourceFilters";
  host?: string;
  sceneName?: string;
  sourceName?: string;
}

/** Every action's Settings may carry a per-key background color, picked in its property inspector —
 * same convention as the music actions (musicAction.ts). Exported for {@link "./obsLiveIconAction.js"}. */
export function backgroundColorOf(settings: JsonObject): string {
  const value = (settings as { backgroundColor?: unknown }).backgroundColor;
  return typeof value === "string" && value.length > 0 ? value : DEFAULT_BACKGROUND;
}

/**
 * Shared base for every "NomNomzBot: OBS" tray action (S-PL5c). Deliberately separate from
 * MusicAction rather than a shared generic base: every OBS action here invokes a PIPELINE the
 * dashboard's pipeline builder already ships (obs_switch_scene, obs_input_mute, obs_streaming,
 * obs_recording — obs-control.md §5), each with its own verb/field baked into the key instead of
 * resolved from a live push channel, so there is no `song.changed`-equivalent state to subscribe to
 * and re-render on. A future OBS live-state push (mirroring music's WS `song.changed`) would be the
 * natural point to unify this with MusicAction behind one shared base.
 */
export abstract class ObsAction<TSettings extends JsonObject = JsonObject> extends SingletonAction<TSettings> {
  /** The auto-provisioned PIPELINE this key invokes (AutomationPairingService.EnsureStreamDeckActionPipelinesAsync
   * provisions one such pipeline per obs_* ICommandAction on first pairing) — not necessarily unique per
   * key: several keys (Start/Stop/Toggle Streaming) invoke the SAME pipeline with a different `action` param. */
  protected abstract readonly pipelineName: string;

  /** Manifest base name of this action's icon (bot.nomnomzbot.streamdeck.obs.sdPlugin/imgs/actions/*.svg). */
  protected abstract readonly iconName: string;

  /** Maps this key's Settings to the pipeline's invoke params (e.g. which scene, which input, which verb). */
  protected resolveParams(_settings: TSettings): Record<string, unknown> {
    return {};
  }

  protected async render(action: WillAppearEvent<TSettings>["action"], settings: TSettings): Promise<void> {
    await action.setImage(renderIconKey(this.iconName, backgroundColorOf(settings), false));
  }

  override async onWillAppear(ev: WillAppearEvent<TSettings>): Promise<void> {
    await this.render(ev.action, ev.payload.settings);
  }

  override async onDidReceiveSettings(ev: DidReceiveSettingsEvent<TSettings>): Promise<void> {
    await this.render(ev.action, ev.payload.settings);
  }

  override async onKeyDown(ev: KeyDownEvent<TSettings>): Promise<void> {
    if (!(await automationClient.isPaired())) {
      await ev.action.showAlert();
      return;
    }
    try {
      await automationClient.invoke(this.pipelineName, this.resolveParams(ev.payload.settings));
    } catch (error) {
      const reason = error instanceof AutomationApiError ? (error.errorCode ?? error.message) : "unknown error";
      streamDeck.logger.warn(`${this.pipelineName} failed: ${reason}`);
      await ev.action.showAlert();
    }
  }

  /** Property-inspector requests — same relay pattern as MusicAction's onSendToPlugin (P5): the PI
   * can't hold the bearer token itself, so pairing/host/scene/input reads are relayed through here.
   * listScenes/listInputs (S-STREAMDECK-OBS-REMAINDER) replace the original plain-text scene/input
   * name field (S-PL5c's "keep it simple" fallback) with a fetched picker, matching
   * listDevices/listPlaylists on MusicAction. */
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
    } else if (request.type === "listScenes") {
      const scenes = await automationClient.getObsScenes().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "scenes", scenes });
    } else if (request.type === "listInputs") {
      const inputs = await automationClient.getObsInputs().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "inputs", inputs });
    } else if (request.type === "listSceneItems") {
      const sceneItems = await automationClient.getObsSceneItems(request.sceneName ?? "").catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "sceneItems", sceneItems });
    } else if (request.type === "listSceneTransitions") {
      const transitions = await automationClient.getObsSceneTransitions().catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "transitions", transitions });
    } else if (request.type === "listSourceFilters") {
      const sourceFilters = await automationClient
        .getObsSourceFilters(request.sourceName ?? "")
        .catch(() => []);
      await streamDeck.ui.sendToPropertyInspector({ type: "sourceFilters", sourceFilters });
    }
  }
}

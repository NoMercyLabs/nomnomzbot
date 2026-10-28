// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot. You may use, change and share it for free,
//  for any noncommercial purpose, under the PolyForm Noncommercial License
//  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
//  See LICENSE for details, and TRADEMARKS.md for the name and logo.
//
//  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// -----------------------------------------------------------------------------

// OBS reports hotkeys by internal name. This maps the ones OBS ships to a words key under dashboard.obs.hotkeys.names.
const KNOWN = {
  'OBSBasic.StartStreaming': 'startStreaming',
  'OBSBasic.StopStreaming': 'stopStreaming',
  'OBSBasic.StartRecording': 'startRecording',
  'OBSBasic.StopRecording': 'stopRecording',
  'OBSBasic.PauseRecording': 'pauseRecording',
  'OBSBasic.UnpauseRecording': 'unpauseRecording',
  'OBSBasic.StartReplayBuffer': 'startReplayBuffer',
  'OBSBasic.StopReplayBuffer': 'stopReplayBuffer',
  'ReplayBuffer.Save': 'saveReplay',
  'OBSBasic.StartVirtualCam': 'startVirtualCamera',
  'OBSBasic.StopVirtualCam': 'stopVirtualCamera',
  'OBSBasic.EnablePreview': 'enablePreview',
  'OBSBasic.DisablePreview': 'disablePreview',
  'OBSBasic.EnablePreviewProgram': 'enableStudioMode',
  'OBSBasic.DisablePreviewProgram': 'disableStudioMode',
  'OBSBasic.TogglePreviewProgram': 'toggleStudioMode',
  'OBSBasic.Transition': 'transition',
  'OBSBasic.QuickTransition.1': 'quickTransition',
  'OBSBasic.ResetStats': 'resetStats',
  'OBSBasic.ForceGroupSelected': 'selectGroup',
  'OBSBasic.ForceStopStreaming': 'forceStopStreaming',
  'OBSBasic.SplitFile': 'splitFile',
  'OBSBasic.AddChapterMarker': 'addChapterMarker',
  'OBSBasic.ShowContextBar': 'showContextBar',
  'OBSBasic.HideContextBar': 'hideContextBar',
  'OBSBasic.Screenshot': 'screenshot',
  'OBSBasic.SelectedSourceScreenshot': 'sourceScreenshot',
  'OBSBasic.SelectScene': 'selectScene',
  'ObsBrowser.Refresh': 'refreshBrowser',
  'libobs.mute': 'mute',
  'libobs.unmute': 'unmute',
  'libobs.push-to-mute': 'pushToMute',
  'libobs.push-to-talk': 'pushToTalk',
} as const satisfies Record<string, string>;

// Scene item hotkeys carry the item's number: libobs.<action>.<number>.
const TARGETED = {
  show_scene_item: 'showSceneItem',
  hide_scene_item: 'hideSceneItem',
} as const satisfies Record<string, string>;

export type ObsHotkeyGroup = 'stream' | 'recording' | 'buffers' | 'studio' | 'screenshots' | 'more' | 'unresolved';

export type ObsHotkeyLabel =
  | {
    kind: 'known';
    key: string;
    target?: string;
    group: ObsHotkeyGroup;
  } |
  {
    kind: 'readable';
    text: string;
    group: ObsHotkeyGroup;
  };

export type ObsHotkeyGroupList = {
  group: ObsHotkeyGroup;
  names: string[];
};

// The group of each words key. A hotkey that acts on a source or scene item OBS does not name is unresolved.
const GROUP_OF = {
  startStreaming: 'stream',
  stopStreaming: 'stream',
  forceStopStreaming: 'stream',
  startRecording: 'recording',
  stopRecording: 'recording',
  pauseRecording: 'recording',
  unpauseRecording: 'recording',
  splitFile: 'recording',
  addChapterMarker: 'recording',
  startReplayBuffer: 'buffers',
  stopReplayBuffer: 'buffers',
  saveReplay: 'buffers',
  startVirtualCamera: 'buffers',
  stopVirtualCamera: 'buffers',
  enablePreview: 'studio',
  disablePreview: 'studio',
  enableStudioMode: 'studio',
  disableStudioMode: 'studio',
  toggleStudioMode: 'studio',
  showContextBar: 'studio',
  hideContextBar: 'studio',
  transition: 'studio',
  quickTransition: 'studio',
  screenshot: 'screenshots',
  sourceScreenshot: 'screenshots',
  resetStats: 'more',
  selectGroup: 'more',
  mute: 'unresolved',
  unmute: 'unresolved',
  pushToMute: 'unresolved',
  pushToTalk: 'unresolved',
  selectScene: 'unresolved',
  refreshBrowser: 'unresolved',
  showSceneItem: 'unresolved',
  hideSceneItem: 'unresolved',
} as const satisfies Record<string, ObsHotkeyGroup>;

// The order the groups show in; unresolved goes last, behind Other.
export const OBS_HOTKEY_GROUPS = [
  'stream',
  'recording',
  'buffers',
  'studio',
  'screenshots',
  'more',
  'unresolved',
] as const satisfies readonly ObsHotkeyGroup[];

// A name OBS sends is any string, so the lookup takes it as one.
function lookup<T extends string>(table: Readonly<Record<string, T>>, key: string): T | undefined {
  return table[key];
}

// One word-run per segment: "do_the_thing" and "DoTheThing" both read "Do the thing".
function sentence(raw: string): string {
  const words = raw
    .replace(/([a-z\d])([A-Z])/g, '$1 $2')
    .replace(/[_\-.]+/g, ' ')
    .trim()
    .toLowerCase();

  return words.charAt(0).toUpperCase() + words.slice(1);
}

function known(key: string, target?: string): ObsHotkeyLabel {
  return {
    kind: 'known',
    key,
    target,
    group: lookup(GROUP_OF, key) ?? 'more',
  };
}

// The plain label for an OBS hotkey name; a name this page does not know reads as a sentence instead of the raw name.
export function describeObsHotkey(name: string) {
  const exact = lookup(KNOWN, name);

  if (exact)
    return known(exact);

  const targeted = /^libobs\.([^.]+)\.(.+)$/.exec(name);
  const action = targeted?.[1]
    ? lookup(TARGETED, targeted[1])
    : undefined;

  if (action && targeted?.[2])
    return known(action, targeted[2]);

  return {
    kind: 'readable',
    text: sentence(name.replace(/^(OBSBasic|libobs)\./, '')),
    group: 'more',
  } as const satisfies ObsHotkeyLabel;
}

// Each name once (OBS lists one per source), sorted into groups in the page's order; empty groups left out.
export function groupObsHotkeys(names: readonly string[]): ObsHotkeyGroupList[] {
  const unique = [...new Set(names)];

  return OBS_HOTKEY_GROUPS
    .map((group) => ({
      group,
      names: unique.filter((name) => describeObsHotkey(name).group === group),
    }))
    .filter((entry) => entry.names.length > 0);
}

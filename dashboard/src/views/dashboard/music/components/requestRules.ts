// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { MusicConfig, MusicConfigChange } from '@/api/music';
import { musicRoleRungs, type MusicRoleRung } from '@/api/vocabulary';

export const queueLimits = { min: 1, max: 500 } as const;
export const requestLimits = { min: 1, max: 50 } as const;
export const songSecondsLimits = { min: 10, max: 3600 } as const;
// 0 turns the per-stream cap off (the bot stores no cap); 1 to 100 turns it on.
export const perStreamLimits = { min: 0, max: 100 } as const;

// The banger playlist travels as one select value so the provider that owns it is never guessed.
const playlistSeparator = '|';

// A select item cannot hold an empty value, so "no playlist chosen" has its own word.
export const noPlaylist = 'none';

export function playlistValue(provider: string, id: string): string {
  return `${provider}${playlistSeparator}${id}`;
}

function splitPlaylist(value: string): { provider: string; id: string } {
  const at = value.indexOf(playlistSeparator);

  return { provider: value.slice(0, at), id: value.slice(at + 1) };
}

type RoleCap = string | number;

// A blank role box means "use the per-person cap", so the role boxes hold text.
export type RuleDraft = {
  minTrustLevel: string;
  perPerson: number;
  maxQueueSize: number;
  requestsOpen: boolean;
  allowYouTube: boolean;
  allowSpotify: boolean;
  preferredProvider: string;
  allowExplicit: boolean;
  maxSongSeconds: number;
  perStreamLimit: number;
  bangerAutoCreate: boolean;
  bangerPlaylist: string;
  roleCaps: Record<MusicRoleRung, RoleCap>;
};

export function draftOf(config: MusicConfig): RuleDraft {
  const stored = config.maxRequestsPerRole ?? {};

  return {
    minTrustLevel: config.minTrustLevel,
    perPerson: Number(config.maxRequestsPerUser),
    maxQueueSize: Number(config.maxQueueSize),
    requestsOpen: config.requestsOpen,
    allowYouTube: config.allowYouTube,
    allowSpotify: config.allowSpotify,
    preferredProvider: config.preferredProvider,
    allowExplicit: config.allowExplicit,
    maxSongSeconds: Number(config.maxSongSeconds),
    perStreamLimit: Number(config.perStreamLimit ?? 0),
    bangerAutoCreate: config.bangerAutoCreate,
    bangerPlaylist: config.bangerPlaylistId
      ? playlistValue(config.bangerPlaylistProvider ?? '', config.bangerPlaylistId)
      : noPlaylist,
    roleCaps: Object.fromEntries(
      musicRoleRungs.map((rung: MusicRoleRung): [MusicRoleRung, string] => [
        rung,
        stored[rung] === undefined
          ? ''
          : String(stored[rung]),
      ]),
    ) as Record<MusicRoleRung, string>,
  };
}

function inRange(value: number, min: number, max: number): boolean {
  return Number.isInteger(value) && value >= min && value <= max;
}

// A number input hands Vue a number once it holds digits and an empty string while it is blank.
function isBlank(cap: RoleCap): boolean {
  return String(cap).trim() === '';
}

function roleCapValid(cap: RoleCap): boolean {
  return isBlank(cap) || inRange(Number(cap), requestLimits.min, requestLimits.max);
}

export function draftValid(draft: RuleDraft): boolean {
  const caps = Object.values(draft.roleCaps);

  return (
    inRange(draft.perPerson, requestLimits.min, requestLimits.max) &&
    inRange(draft.maxQueueSize, queueLimits.min, queueLimits.max) &&
    inRange(draft.maxSongSeconds, songSecondsLimits.min, songSecondsLimits.max) &&
    inRange(draft.perStreamLimit, perStreamLimits.min, perStreamLimits.max) &&
    caps.every(roleCapValid)
  );
}

// Blank rungs drop out of the map; the bot replaces the stored map whole.
function roleMapOf(caps: Record<MusicRoleRung, RoleCap>): Record<string, number> {
  const filled = musicRoleRungs.filter((rung: MusicRoleRung): boolean => !isBlank(caps[rung]));
  const entries = filled.map((rung: MusicRoleRung): [string, number] => [rung, Number(caps[rung])]);

  return Object.fromEntries(entries);
}

function sameMap(a: Record<string, number>, b: Record<string, number>): boolean {
  const keys = Object.keys(a);
  const sameSize = keys.length === Object.keys(b).length;

  return sameSize && keys.every((key: string): boolean => a[key] === b[key]);
}

// An empty id clears the playlist; the bot then makes one on first use if auto-create is on.
function playlistChange(value: string): MusicConfigChange {
  if (value === noPlaylist) {
    return { bangerPlaylistId: '', bangerPlaylistProvider: '' };
  }

  const { provider, id } = splitPlaylist(value);

  return { bangerPlaylistId: id, bangerPlaylistProvider: provider };
}

// Only the fields that differ from the saved config are sent.
export function changeOf(config: MusicConfig, draft: RuleDraft): MusicConfigChange {
  const saved = draftOf(config);
  const result: MusicConfigChange = {};

  if (draft.minTrustLevel !== saved.minTrustLevel) {
    result.minTrustLevel = draft.minTrustLevel;
  }

  if (draft.perPerson !== saved.perPerson) {
    result.maxRequestsPerUser = draft.perPerson;
  }

  if (draft.maxQueueSize !== saved.maxQueueSize) {
    result.maxQueueSize = draft.maxQueueSize;
  }

  if (draft.requestsOpen !== saved.requestsOpen) {
    result.requestsOpen = draft.requestsOpen;
  }

  if (draft.allowYouTube !== saved.allowYouTube) {
    result.allowYouTube = draft.allowYouTube;
  }

  if (draft.allowSpotify !== saved.allowSpotify) {
    result.allowSpotify = draft.allowSpotify;
  }

  if (draft.preferredProvider !== saved.preferredProvider) {
    result.preferredProvider = draft.preferredProvider;
  }

  if (draft.allowExplicit !== saved.allowExplicit) {
    result.allowExplicit = draft.allowExplicit;
  }

  if (draft.maxSongSeconds !== saved.maxSongSeconds) {
    result.maxSongSeconds = draft.maxSongSeconds;
  }

  if (draft.perStreamLimit !== saved.perStreamLimit) {
    result.perStreamLimit = draft.perStreamLimit;
  }

  if (draft.bangerAutoCreate !== saved.bangerAutoCreate) {
    result.bangerAutoCreate = draft.bangerAutoCreate;
  }

  if (draft.bangerPlaylist !== saved.bangerPlaylist) {
    Object.assign(result, playlistChange(draft.bangerPlaylist));
  }

  const roleMap = roleMapOf(draft.roleCaps);

  if (!sameMap(roleMap, roleMapOf(saved.roleCaps))) {
    result.maxRequestsPerRole = roleMap;
  }

  return result;
}

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

// The rungs the bot knows for a per-role cap, in the order they rank (MusicService role map).
export const roleRungs = ['viewer', 'subscriber', 'vip', 'moderator', 'broadcaster'] as const;
export const providers = ['auto', 'spotify', 'youtube'] as const;
export const queueLimits = { min: 1, max: 500 } as const;
export const requestLimits = { min: 1, max: 50 } as const;

export type RoleRung = (typeof roleRungs)[number];
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
  roleCaps: Record<RoleRung, RoleCap>;
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
    roleCaps: Object.fromEntries(
      roleRungs.map((rung: RoleRung): [RoleRung, string] => [
        rung,
        stored[rung] === undefined
          ? ''
          : String(stored[rung]),
      ]),
    ) as Record<RoleRung, string>,
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
    caps.every(roleCapValid)
  );
}

// Blank rungs drop out of the map; the bot replaces the stored map whole.
function roleMapOf(caps: Record<RoleRung, RoleCap>): Record<string, number> {
  const filled = roleRungs.filter((rung: RoleRung): boolean => !isBlank(caps[rung]));
  const entries = filled.map((rung: RoleRung): [string, number] => [rung, Number(caps[rung])]);

  return Object.fromEntries(entries);
}

function sameMap(a: Record<string, number>, b: Record<string, number>): boolean {
  const keys = Object.keys(a);
  const sameSize = keys.length === Object.keys(b).length;

  return sameSize && keys.every((key: string): boolean => a[key] === b[key]);
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

  const roleMap = roleMapOf(draft.roleCaps);

  if (!sameMap(roleMap, roleMapOf(saved.roleCaps))) {
    result.maxRequestsPerRole = roleMap;
  }

  return result;
}

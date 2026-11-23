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

import type {
  ChatFilter,
  ChatFilterAction,
  ChatFilterRequest,
  ChatFilterType,
} from '@/api/chatFilters';
import { levelOf, rungOf, rungs, type RungName } from '@/api/permissions';
import { boundTarget, sameTarget, targetToWire, type PickedTarget } from '@/lib/bindingTarget';

// What the editor edits. A list is one entry per line while it is typed; the role is a name, never a number.
// The target is what the filter also runs after its moderation action; null is nothing.
export type ChatFilterDraft = {
  name: string;
  filterType: ChatFilterType;
  action: ChatFilterAction;
  target: PickedTarget | null;
  pattern: string;
  terms: string;
  allowedDomains: string;
  matchBareDomains: boolean;
  timeoutSeconds: number;
  exemptRung: RungName;
  isCaseSensitive: boolean;
  isEnabled: boolean;
};

// Everyone as the floor would exempt every sender, so the filter could never act.
export const EXEMPT_RUNGS = rungs
  .filter((rung) => rung.level > 0)
  .map((rung) => rung.name);

export const DEFAULT_TIMEOUT_SECONDS = 600;

const lines = (list: readonly string[] | null | undefined): string => (list ?? []).join('\n');

// One entry per line; blank lines and doubled entries drop out.
export function toList(text: string): string[] {
  const entries = text.split('\n').map((line) => line.trim()).filter((line) => line !== '');

  return [...new Set(entries)];
}

export function toDraft(filter: ChatFilter | null) {
  return {
    name: filter?.name ?? '',
    filterType: filter?.filterType ?? 'Blocklist',
    action: filter?.action ?? 'Delete',
    target: boundTarget(filter?.target, null),
    pattern: filter?.pattern ?? '',
    terms: lines(filter?.terms),
    allowedDomains: lines(filter?.linkPolicy?.allowedDomains),
    matchBareDomains: filter?.linkPolicy?.matchBareDomains ?? true,
    timeoutSeconds: Number(filter?.timeoutSeconds ?? DEFAULT_TIMEOUT_SECONDS),
    exemptRung: filter
      ? rungOf(filter.exemptMinRoleLevel)
      : 'Moderator',
    isCaseSensitive: filter?.isCaseSensitive ?? false,
    isEnabled: filter?.isEnabled ?? true,
  } as const satisfies ChatFilterDraft;
}

// Only the fields of the chosen type travel; a timeout length travels only with a timeout action.
// The target travels only when it differs from what the bot holds; a cleared target travels as an empty pipeline.
export function toRequest(draft: ChatFilterDraft, existing: ChatFilter | null = null) {
  const held = boundTarget(existing?.target, null);

  return {
    filterType: draft.filterType,
    name: draft.name.trim(),
    action: draft.action,
    ...(sameTarget(draft.target, held)
      ? {}
      : { target: targetToWire(draft.target) }),
    pattern: draft.filterType === 'Regex'
      ? draft.pattern
      : null,
    terms: draft.filterType === 'Blocklist'
      ? toList(draft.terms)
      : null,
    linkPolicy: draft.filterType === 'LinkPolicy'
      ? { allowedDomains: toList(draft.allowedDomains), matchBareDomains: draft.matchBareDomains }
      : null,
    timeoutSeconds: draft.action === 'Timeout'
      ? draft.timeoutSeconds
      : null,
    exemptMinRoleLevel: levelOf(draft.exemptRung),
    isEnabled: draft.isEnabled,
    isCaseSensitive: draft.isCaseSensitive,
  } as const satisfies ChatFilterRequest;
}

export type ChatFilterProblem = 'name' | 'pattern' | 'terms' | 'timeout';

// What stops a save before the bot sees it; each name is the field to mark.
export function problems(draft: ChatFilterDraft): ChatFilterProblem[] {
  const found: ChatFilterProblem[] = [];

  if (draft.name.trim() === '') {
    found.push('name');
  }

  if (draft.filterType === 'Regex' && draft.pattern.trim() === '') {
    found.push('pattern');
  }

  if (draft.filterType === 'Blocklist' && toList(draft.terms).length === 0) {
    found.push('terms');
  }

  if (draft.action === 'Timeout' && !(draft.timeoutSeconds >= 1)) {
    found.push('timeout');
  }

  return found;
}

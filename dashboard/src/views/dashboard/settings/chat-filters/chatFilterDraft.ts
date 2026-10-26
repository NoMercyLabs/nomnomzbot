// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type {
  ChatFilter,
  ChatFilterAction,
  ChatFilterRequest,
  ChatFilterType,
} from '@/api/chatFilters';
import { levelOf, rungOf, rungs, type RungName } from '@/api/permissions';

// What the editor edits. A list is one entry per line while it is typed; the role is a name, never a number.
export type ChatFilterDraft = {
  name: string;
  filterType: ChatFilterType;
  action: ChatFilterAction;
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
export function toRequest(draft: ChatFilterDraft) {
  return {
    filterType: draft.filterType,
    name: draft.name.trim(),
    action: draft.action,
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

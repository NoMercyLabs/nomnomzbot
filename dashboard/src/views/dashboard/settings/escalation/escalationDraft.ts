// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { EscalationChange, EscalationPolicy } from '@/api/escalationPolicy';

// What the Escalation page edits: the stored policy with every number as a number (the bot may send text),
// and the three actions a rung can take. The ranges and defaults come from the bot with the policy.
export const stepActions = ['warn', 'timeout', 'ban'] as const;

export type StepAction = (typeof stepActions)[number];

export type DraftStep = {
  atOffense: number;
  action: StepAction;
  timeoutSeconds: number | null;
};

export type EscalationDraft = {
  isEnabled: boolean;
  countAutoModViolations: boolean;
  offenseWindowHours: number;
  defaultTimeoutSeconds: number;
  ladder: DraftStep[];
};

type StoredStep = EscalationPolicy['ladder'][number];

function toAction(action: string): StepAction {
  return stepActions.find((known) => known === action) ?? 'warn';
}

function toStep(step: StoredStep) {
  return {
    atOffense: Number(step.atOffense),
    action: toAction(step.action),
    timeoutSeconds:
      step.timeoutSeconds === null || step.timeoutSeconds === undefined
        ? null
        : Number(step.timeoutSeconds),
  } as const satisfies DraftStep;
}

export function toDraft(policy: Omit<EscalationPolicy, 'defaults' | 'limits'>) {
  return {
    isEnabled: policy.isEnabled,
    countAutoModViolations: policy.countAutoModViolations,
    offenseWindowHours: Number(policy.offenseWindowHours),
    defaultTimeoutSeconds: Number(policy.defaultTimeoutSeconds),
    ladder: policy.ladder.map(toStep),
  } as const satisfies EscalationDraft;
}

export function sameDraft(a: EscalationDraft, b: EscalationDraft): boolean {
  return JSON.stringify(a) === JSON.stringify(b);
}

export function toChange(draft: EscalationDraft) {
  return {
    isEnabled: draft.isEnabled,
    countAutoModViolations: draft.countAutoModViolations,
    offenseWindowHours: draft.offenseWindowHours,
    defaultTimeoutSeconds: draft.defaultTimeoutSeconds,
    ladder: draft.ladder.map((step) => ({ ...step })),
  } as const satisfies EscalationChange;
}

// A new rung sits one offense after the last one and starts as a warning.
export function nextStep(draft: EscalationDraft) {
  const last = draft.ladder.at(-1)?.atOffense ?? 0;

  return {
    atOffense: last + 1,
    action: 'warn',
    timeoutSeconds: null,
  } as const satisfies DraftStep;
}

// A timeout rung always holds a length (the draft default); the other actions hold none.
export function withAction(step: DraftStep, action: StepAction, defaultSeconds: number) {
  return {
    ...step,
    action,
    timeoutSeconds: action === 'timeout'
      ? (step.timeoutSeconds ?? defaultSeconds)
      : null,
  } as const satisfies DraftStep;
}

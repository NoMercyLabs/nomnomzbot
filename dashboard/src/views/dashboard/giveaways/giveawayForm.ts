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

// The giveaway dialog edits text and switches; this file turns them into the draft the API saves, and back.
import { readEligibility, readWeighting, type Giveaway, type GiveawayDraft } from '@/api/giveaways';
import { boundTarget, type PickedTarget } from '@/lib/bindingTarget';

export const entryModes = ['keyword', 'active_viewers'] as const;
export const prizeModes = ['announce', 'currency', 'pipeline', 'code_pool'] as const;

export type GiveawayForm = {
  title: string;
  entryMode: string;
  keyword: string;
  entryCost: string;
  maxEntriesPerUser: string;
  requireSub: boolean;
  minStandingLevel: string;
  minWatchMinutes: string;
  minAccountAgeDays: string;
  subT1: string;
  subT2: string;
  subT3: string;
  vip: string;
  winnerCount: string;
  excludeModerators: boolean;
  claimWindowMinutes: string;
  prizeMode: string;
  prizeCurrencyAmount: string;
  prizeFromPot: boolean;
  prizeTarget: PickedTarget | null;
  prizeCodePoolId: string;
  requires18Plus: boolean;
  scheduledCloseAt: string;
};

const text = (value: number | string | null | undefined): string =>
  value === null || value === undefined
    ? ''
    : String(value);

const pad = (value: number): string => String(value).padStart(2, '0');

// The datetime-local input reads and writes the viewer's local time without a zone.
function localInput(iso: string | null | undefined): string {
  if (!iso)
    return '';

  const at = new Date(iso);

  return `${at.getFullYear()}-${pad(at.getMonth() + 1)}-${pad(at.getDate())}T${pad(at.getHours())}:${pad(at.getMinutes())}`;
}

export function formFromGiveaway(giveaway: Giveaway | null) {
  const rules = readEligibility(giveaway?.eligibilityJson);
  const tickets = readWeighting(giveaway?.weightingJson);

  return {
    title: giveaway?.title ?? '',
    entryMode: giveaway?.entryMode ?? 'keyword',
    keyword: giveaway
      ? text(giveaway.keyword)
      : '!enter',
    entryCost: text(giveaway?.entryCost),
    maxEntriesPerUser: text(giveaway?.maxEntriesPerUser ?? 1),
    requireSub: rules.requireSub,
    minStandingLevel: text(rules.minStandingLevel),
    minWatchMinutes: text(rules.minWatchMinutes),
    minAccountAgeDays: text(rules.minAccountAgeDays),
    subT1: text(tickets.subT1),
    subT2: text(tickets.subT2),
    subT3: text(tickets.subT3),
    vip: text(tickets.vip),
    winnerCount: text(giveaway?.winnerCount ?? 1),
    excludeModerators: giveaway?.excludeModerators ?? false,
    claimWindowMinutes: text(giveaway?.claimWindowMinutes),
    prizeMode: giveaway?.prizeMode ?? 'announce',
    prizeCurrencyAmount: text(giveaway?.prizeCurrencyAmount),
    prizeFromPot: giveaway?.prizeFromPot ?? false,
    prizeTarget: boundTarget(giveaway?.target, giveaway?.prizePipelineId),
    prizeCodePoolId: text(giveaway?.prizeCodePoolId),
    requires18Plus: giveaway?.requires18Plus ?? false,
    scheduledCloseAt: localInput(giveaway?.scheduledCloseAt),
  } as const satisfies GiveawayForm;
}

// A blank box means "not set". A number input hands its value on as a number once it is filled, so the
// value is read as text first.
const blank = (value: string): boolean => String(value).trim() === '';

const optional = (value: string): number | null =>
  blank(value)
    ? null
    : Number(value);

export function draftFromForm(form: GiveawayForm) {
  return {
    title: form.title.trim(),
    entryMode: form.entryMode,
    keyword: form.keyword.trim(),
    entryCost: optional(form.entryCost),
    maxEntriesPerUser: Number(form.maxEntriesPerUser),
    eligibility: {
      requireSub: form.requireSub,
      minStandingLevel: optional(form.minStandingLevel),
      minWatchMinutes: optional(form.minWatchMinutes),
      minAccountAgeDays: optional(form.minAccountAgeDays),
    },
    weighting: {
      subT1: Number(form.subT1),
      subT2: Number(form.subT2),
      subT3: Number(form.subT3),
      vip: Number(form.vip),
    },
    winnerCount: Number(form.winnerCount),
    excludeModerators: form.excludeModerators,
    claimWindowMinutes: optional(form.claimWindowMinutes),
    prizeMode: form.prizeMode,
    prizeCurrencyAmount: optional(form.prizeCurrencyAmount),
    prizeFromPot: form.prizeFromPot,
    prizeTarget: form.prizeTarget,
    prizeCodePoolId: form.prizeCodePoolId === ''
      ? null
      : form.prizeCodePoolId,
    requires18Plus: form.requires18Plus,
    scheduledCloseAt:
      form.scheduledCloseAt === ''
        ? null
        : new Date(form.scheduledCloseAt).toISOString(),
  } as const satisfies GiveawayDraft;
}

const wholeFrom = (value: number, least: number): boolean =>
  Number.isInteger(value) && value >= least;

const wholeOrBlank = (value: string, least: number): boolean =>
  blank(value) || wholeFrom(Number(value), least);

// One code per line; blank lines and repeats drop out, so a pasted list loads cleanly.
export function codesFromText(text: string): string[] {
  const lines = text.split('\n').map((line): string => line.trim());
  const filled = lines.filter((line): boolean => line !== '');

  return Array.from(new Set(filled));
}

// The i18n key (under dashboard.giveaways.dialog.problem) of the first thing wrong, or undefined.
export function formProblem(form: GiveawayForm): string | undefined {
  const draft = draftFromForm(form);
  const tickets = Object.values(draft.weighting);

  const checks: [boolean, string][] = [
    [draft.title === '', 'titleRequired'],
    [!wholeFrom(draft.winnerCount, 1), 'winnersInvalid'],
    [!wholeFrom(draft.maxEntriesPerUser, 1), 'maxEntriesInvalid'],
    [!wholeOrBlank(form.entryCost, 0), 'entryCostInvalid'],
    [!wholeOrBlank(form.minStandingLevel, 0), 'ruleInvalid'],
    [!wholeOrBlank(form.minWatchMinutes, 0), 'ruleInvalid'],
    [!wholeOrBlank(form.minAccountAgeDays, 0), 'ruleInvalid'],
    [tickets.some((count): boolean => !wholeFrom(count, 1)), 'ticketsInvalid'],
    [!wholeOrBlank(form.claimWindowMinutes, 1), 'claimInvalid'],
    [
      form.prizeMode === 'currency' && !wholeFrom(Number(form.prizeCurrencyAmount), 1),
      'currencyInvalid',
    ],
    [form.prizeMode === 'pipeline' && form.prizeTarget === null, 'pipelineRequired'],
    [form.prizeMode === 'code_pool' && form.prizeCodePoolId === '', 'poolRequired'],
    [
      form.scheduledCloseAt !== '' && Number.isNaN(new Date(form.scheduledCloseAt).getTime()),
      'closeInvalid',
    ],
  ];

  return checks.find(([wrong]) => wrong)?.[1];
}

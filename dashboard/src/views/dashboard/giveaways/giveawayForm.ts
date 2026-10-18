// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

// The giveaway dialog edits text and switches; this file turns them into the draft the API saves, and back.
import { readEligibility, readWeighting, type Giveaway, type GiveawayDraft } from '@/api/giveaways';

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
  prizePipelineId: string;
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

export function formFromGiveaway(giveaway: Giveaway | null): GiveawayForm {
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
    prizePipelineId: text(giveaway?.prizePipelineId),
    prizeCodePoolId: text(giveaway?.prizeCodePoolId),
    requires18Plus: giveaway?.requires18Plus ?? false,
    scheduledCloseAt: localInput(giveaway?.scheduledCloseAt),
  };
}

// A blank box means "not set". A number input hands its value on as a number once it is filled, so the
// value is read as text first.
const blank = (value: string): boolean => String(value).trim() === '';

const optional = (value: string): number | null =>
  blank(value)
    ? null
    : Number(value);

export function draftFromForm(form: GiveawayForm): GiveawayDraft {
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
    prizePipelineId: form.prizePipelineId === ''
      ? null
      : form.prizePipelineId,
    prizeCodePoolId: form.prizeCodePoolId === ''
      ? null
      : form.prizeCodePoolId,
    requires18Plus: form.requires18Plus,
    scheduledCloseAt:
      form.scheduledCloseAt === ''
        ? null
        : new Date(form.scheduledCloseAt).toISOString(),
  };
}

const wholeFrom = (value: number, least: number): boolean =>
  Number.isInteger(value) && value >= least;

const wholeOrBlank = (value: string, least: number): boolean =>
  blank(value) || wholeFrom(Number(value), least);

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
    [form.prizeMode === 'pipeline' && form.prizePipelineId === '', 'pipelineRequired'],
    [form.prizeMode === 'code_pool' && form.prizeCodePoolId === '', 'poolRequired'],
    [
      form.scheduledCloseAt !== '' && Number.isNaN(new Date(form.scheduledCloseAt).getTime()),
      'closeInvalid',
    ],
  ];

  return checks.find(([wrong]) => wrong)?.[1];
}

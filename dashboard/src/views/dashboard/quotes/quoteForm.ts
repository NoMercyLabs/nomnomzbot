// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import type { Quote, QuoteDraft } from '@/api/quotes';
import { fromLocalInput, toLocalInput } from '@/lib/time';

// The most text the server keeps for one quote.
export const maxQuoteLength = 500;

// What the dialog fields hold: text only, the date as a datetime-local value.
export type QuoteForm = {
  text: string;
  quotedDisplayName: string;
  contextGame: string;
  quotedAt: string;
};

export type QuoteProblem = 'textRequired' | 'textTooLong';

export function formFromQuote(quote: Quote | null) {
  return {
    text: quote?.text ?? '',
    quotedDisplayName: quote?.quotedDisplayName ?? '',
    contextGame: quote?.contextGame ?? '',
    quotedAt: toLocalInput(quote?.quotedAt),
  } as const satisfies QuoteForm;
}

export function formProblem(form: QuoteForm): QuoteProblem | undefined {
  const text = form.text.trim();

  if (text === '')
    return 'textRequired';

  if (text.length > maxQuoteLength)
    return 'textTooLong';

  return undefined;
}

const blankAsNull = (value: string): string | null => {
  const text = value.trim();

  return text === ''
    ? null
    : text;
};

// A new quote carries the signed-in user as its author; an empty date lets the server use the time of saving.
export function draftFromForm(form: QuoteForm, authorId: string | null) {
  return {
    text: form.text.trim(),
    quotedDisplayName: blankAsNull(form.quotedDisplayName),
    contextGame: blankAsNull(form.contextGame),
    quotedAt: form.quotedAt === ''
      ? null
      : fromLocalInput(form.quotedAt),
    createdByUserId: authorId,
  } as const satisfies QuoteDraft;
}

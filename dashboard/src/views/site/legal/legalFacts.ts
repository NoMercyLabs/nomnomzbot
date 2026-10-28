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

// The facts both legal documents name. They are the owner's answers (2026-10-23): the company that runs the
// hosted service, the address for privacy and legal requests, and the date the texts took effect. The governing
// law (the Netherlands) is a word, so it lives in each language's i18n file as site.legal.law.
export const legalFacts = {
  company: 'NoMercy Labs',
  email: 'legal@nomercy.tv',
  effective: '2026-10-23T12:00:00Z',
} as const satisfies Record<string, string>;

// The address of each document, as the Discord and Twitch app pages and the footer link to it.
export const legalPaths = {
  privacy: 'privacy-policy',
  terms: 'terms-of-service',
} as const satisfies Record<'privacy' | 'terms', string>;

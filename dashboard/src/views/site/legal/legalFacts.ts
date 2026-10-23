// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

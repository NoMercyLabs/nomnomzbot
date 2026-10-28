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

import type { RouteRecordRaw } from 'vue-router';

import { loadPage } from '@/i18n';

import { legalPaths } from './legalFacts';

// Public pages, open to a signed-out visitor: Discord and Twitch ask for these addresses when an app is set up.
function legalRoute(doc: 'privacy' | 'terms') {
  return {
    path: legalPaths[doc],
    name: `legal-${doc}`,
    props: { doc },
    component: () =>
      loadPage(
        () => import('./LegalView.vue'),
        (locale) => import(`./i18n/${locale}.json`),
      ),
  } as const satisfies RouteRecordRaw;
}

export const privacyRoute = legalRoute('privacy');
export const termsRoute = legalRoute('terms');

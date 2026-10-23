// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
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

// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { watch } from 'vue';
import type { Router } from 'vue-router';

import { i18n } from '@/i18n';

const titleKey = 'site.home.title';

// The home words load with the home page, after the first paint, so the title is set once they are there:
// after each navigation and after each language change. Until then the title in the HTML (English) stays.
function setTitle(): void {
  if (!i18n.global.te(titleKey)) return;

  document.title = i18n.global.t(titleKey);
}

export function followSiteTitle(router: Router): void {
  router.afterEach(setTitle);
  watch(i18n.global.locale, setTitle);
}

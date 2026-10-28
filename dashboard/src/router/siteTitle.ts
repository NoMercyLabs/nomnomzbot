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

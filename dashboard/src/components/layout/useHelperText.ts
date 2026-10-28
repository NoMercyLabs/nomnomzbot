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

import { useI18n } from 'vue-i18n';

import type { TemplateHelper } from '@/api/templates';

// The bot sends a translation key for each helper, never the sentence. A key this dashboard has no words for shows nothing.
export function useHelperText() {
  const { t, te } = useI18n();

  return {
    describe: (helper: TemplateHelper): string =>
      te(helper.descriptionKey)
        ? t(helper.descriptionKey)
        : '',
    groupLabel: (group: string): string =>
      te(`template.group.${group}`)
        ? t(`template.group.${group}`)
        : group,
  } as const satisfies {
    describe: (helper: TemplateHelper) => string;
    groupLabel: (group: string) => string;
  };
}

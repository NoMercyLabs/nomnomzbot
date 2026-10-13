// -----------------------------------------------------------------------------
//  Copyright (c) NoMercy Labs.
//
//  This file is part of NomNomzBot, free software licensed under the GNU Affero
//  General Public License v3.0 or later. You may redistribute and/or modify it
//  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.
//
//  SPDX-License-Identifier: AGPL-3.0-or-later
// -----------------------------------------------------------------------------

import { useI18n } from 'vue-i18n'

import type { TemplateHelper } from '@/api/templates'

// The bot sends a translation key for each helper, never the sentence. A key this dashboard has no words for shows nothing.
export function useHelperText(): {
  describe: (helper: TemplateHelper) => string
  groupLabel: (group: string) => string
} {
  const { t, te } = useI18n()

  return {
    describe: (helper: TemplateHelper): string =>
      te(helper.descriptionKey) ? t(helper.descriptionKey) : '',
    groupLabel: (group: string): string =>
      te(`template.group.${group}`) ? t(`template.group.${group}`) : group,
  }
}

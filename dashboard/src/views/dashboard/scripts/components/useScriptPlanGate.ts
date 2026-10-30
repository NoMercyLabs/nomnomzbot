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

import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useResourceLimits } from '@/api/resourceLimits';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const scriptTimeKey = 'sandbox_exec_ms';

// Whether the plan of this channel lets it write and run code scripts. A plan sets script run time per
// month; 0 means the plan has no code scripts. A limit that is missing, or a read that failed, does not
// block: the bot refuses the write itself, and its text is shown instead. Every caller shares one read.
export function useScriptPlanGate() {
  const { t } = useI18n();
  const { channelId } = useCurrentChannel();
  const limits = useResourceLimits(channelId);

  const blocked = computed<boolean>(() => {
    const row = limits.data.value?.find((item) => item.limitKey === scriptTimeKey);

    return row !== undefined && Number(row.limit) === 0;
  });

  const reason = computed<string | undefined>(() =>
    blocked.value
      ? t('dashboard.scripts.planNotice')
      : undefined);

  return { blocked, reason };
}

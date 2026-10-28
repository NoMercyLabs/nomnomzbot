<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useActionRequired,
  useDismissActionRequired,
  type ActionRequiredItem,
} from '@/api/actionRequired';
import { attentionWords } from '@/api/actionRequiredText';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { SHRINK_WHEN_EMPTY } from './shrinkWhenEmpty';

const { t, te, locale } = useI18n();
const { channelId } = useCurrentChannel();

const attentionQuery = useActionRequired(channelId);
const { mutate: dismissItem } = useDismissActionRequired(channelId);
const attentionState = usePanelState(
  attentionQuery,
  (items: ActionRequiredItem[]) => items.length === 0,
);

const attention = computed(() =>
  (attentionQuery.data.value ?? []).map((item) => ({
    id: item.id,
    ...attentionWords(item, {
      t,
      te,
      locale,
    }),
  })),
);
</script>

<template>
  <PanelCard
    :class="SHRINK_WHEN_EMPTY"
    :title="t('dashboard.home.attention.title')"
    :state="attentionState"
    :empty-text="t('dashboard.home.attention.empty')"
    :error-text="t('dashboard.home.attention.error')"
    @retry="attentionQuery.refetch()"
  >
    <RowGroup>
      <SummaryRow
        v-for="item in attention"
        :key="item.id"
        :title="item.title"
        :detail="item.message ?? undefined"
      >
        <template #trailing>
          <div class="flex items-center gap-1">
            <Button v-if="item.route" variant="outline" size="sm" as-child>
              <RouterLink :to="item.route">{{ t('dashboard.home.attention.open') }}</RouterLink>
            </Button>
            <Button variant="ghost" size="sm" @click="dismissItem(item.id)">
              {{ t('dashboard.home.attention.dismiss') }}
            </Button>
          </div>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>

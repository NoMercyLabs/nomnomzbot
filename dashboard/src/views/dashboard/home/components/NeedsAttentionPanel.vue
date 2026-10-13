<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useActionRequired, type ActionRequiredItem } from '@/api/actionRequired';
import { attentionWords } from '@/api/actionRequiredText';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();

const attentionQuery = useActionRequired(channelId);
const attentionState = usePanelState(
  attentionQuery,
  (items: ActionRequiredItem[]) => items.length === 0,
);

const attention = computed(() =>
  (attentionQuery.data.value ?? []).map((item) => ({
    id: item.id,
    ...attentionWords(item, { t, te }),
  })),
);
</script>

<template>
  <PanelCard
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
        <template v-if="item.route" #trailing>
          <Button variant="outline" size="sm" as-child>
            <RouterLink :to="item.route">{{ t('dashboard.home.attention.open') }}</RouterLink>
          </Button>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>

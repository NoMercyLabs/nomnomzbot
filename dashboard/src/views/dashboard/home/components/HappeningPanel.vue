<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import type { ComputedRef } from 'vue'
import { useI18n } from 'vue-i18n'

import { useActivity, type ActivityEvent } from '@/api/home'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import RowGroup from '@/components/layout/RowGroup.vue'
import SummaryRow from '@/components/layout/SummaryRow.vue'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

defineProps<{ live: boolean }>()

const { t, te, locale } = useI18n()
const { channelId } = useCurrentChannel()

const activityQuery = useActivity(channelId)
const activityState: ComputedRef<PanelState> = usePanelState(
  activityQuery,
  (items: ActivityEvent[]) => items.length === 0,
)

function eventLabel(event: ActivityEvent): string {
  const key: string = `dashboard.home.activity.type.${event.type.replace('.', '_')}`
  return te(key) ? t(key) : t('dashboard.home.activity.type.other')
}

function eventTime(event: ActivityEvent): string {
  return new Intl.DateTimeFormat(locale.value, { hour: '2-digit', minute: '2-digit' }).format(
    new Date(event.timestamp),
  )
}
</script>

<template>
  <PanelCard
    :title="live ? t('dashboard.home.activity.live') : t('dashboard.home.activity.offline')"
    :state="activityState"
    :empty-text="t('dashboard.home.activity.empty')"
    :error-text="t('dashboard.home.activity.error')"
    @retry="activityQuery.refetch()"
  >
    <RowGroup>
      <SummaryRow
        v-for="event in activityQuery.data.value ?? []"
        :key="event.id"
        :title="event.username ?? eventLabel(event)"
        :detail="event.username ? eventLabel(event) : undefined"
      >
        <template #trailing>
          <span class="text-sm text-muted-foreground tabular-nums">{{ eventTime(event) }}</span>
        </template>
      </SummaryRow>
    </RowGroup>
  </PanelCard>
</template>

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
import { RouterLink, useRouter } from 'vue-router';

import { useActivity, type ActivityEvent } from '@/api/home';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import ArrowRight from '~icons/nnz/arrow-right';

import ActivityRows from '../../events/components/ActivityRows.vue';
import { SHRINK_WHEN_EMPTY } from './shrinkWhenEmpty';

defineProps<{ live: boolean }>();

const { t } = useI18n();
const { channelId } = useCurrentChannel();

// Every event of the current stream (the last one while offline), not a fixed count.
const activityQuery = useActivity(channelId);
const activityState = usePanelState(activityQuery, (items: ActivityEvent[]) =>
  items.length === 0);

const router = useRouter();
// "All events" shows only when an events page exists: a link that lands elsewhere breaks its promise.
const allEventsRoute = computed<string | null>(() =>
  router.hasRoute('dashboard-events')
    ? 'dashboard-events'
    : null,
);
</script>

<template>
  <PanelCard
    :class="SHRINK_WHEN_EMPTY"
    :title="live ? t('dashboard.home.activity.live') : t('dashboard.home.activity.offline')"
    :state="activityState"
    :empty-text="t('dashboard.home.activity.empty')"
    :error-text="t('dashboard.home.activity.error')"
    @retry="activityQuery.refetch()"
  >
    <template v-if="allEventsRoute" #actions>
      <Button as-child variant="ghost" size="sm">
        <RouterLink :to="{ name: allEventsRoute }">
          {{ t('dashboard.home.activity.allEvents') }}
          <ArrowRight class="size-4" aria-hidden="true" />
        </RouterLink>
      </Button>
    </template>
    <ActivityRows :events="activityQuery.data.value ?? []" />
  </PanelCard>
</template>

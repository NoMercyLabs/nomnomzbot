<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// "Thanks for the music": the viewers with the most song requests, all time, with a bar scaled to the leader.
import { computed } from 'vue';
import { RouterLink } from 'vue-router';

import ArrowRight from '~icons/nnz/arrow-right';

import { Button } from '@/components/ui/button';
import { useMusicTopRequesters, type TopRequester } from '@/api/music';
import PanelCard from '@/components/layout/PanelCard.vue';
import UserName from '@/components/layout/UserName.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

const { channelId } = useCurrentChannel();

const query = useMusicTopRequesters(channelId);
const state = usePanelState(
  query,
  (rows: TopRequester[]): boolean => rows.length === 0,
);
const leader = computed<number>(() => query.data.value?.[0]?.count ?? 1);

function share(count: number): string {
  return `${Math.round((count / leader.value) * 100)}%`;
}
</script>

<template>
  <PanelCard
    :title="$t('dashboard.music.thanks.title')"
    :description="$t('dashboard.music.thanks.subtitle')"
    :state="state"
    pad
    :empty-text="$t('dashboard.music.thanks.empty')"
    :error-text="$t('dashboard.music.thanks.error')"
    @retry="query.refetch()"
  >
    <ul class="flex flex-col gap-3">
      <li
        v-for="item in query.data.value"
        :key="item.userId"
        data-top-requester
        class="flex items-center gap-3 text-sm"
      >
        <span class="min-w-0 flex-1">
          <UserName :user-id="item.userId" :name="item.displayName" />
          <span class="mt-1 block h-1.5 rounded-full bg-muted">
            <span
              data-bar
              class="block h-full rounded-full bg-primary"
              :style="{ width: share(item.count) }"
            />
          </span>
        </span>
        <span class="flex-none text-xs text-muted-foreground">
          {{ $t('dashboard.music.thanks.songs', { count: item.count }, item.count) }}
        </span>
      </li>
    </ul>
    <template #footer>
      <Button as-child variant="ghost" size="sm" class="ml-auto">
        <RouterLink to="/app/people">
          {{ $t('dashboard.music.thanks.openPeople') }}
          <ArrowRight class="size-4" aria-hidden="true" />
        </RouterLink>
      </Button>
    </template>
  </PanelCard>
</template>

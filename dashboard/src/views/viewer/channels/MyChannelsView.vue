<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The viewer home: the channels a person watches, each with a live mark and an Open link, and the channels
// they manage. Own data only: it lists channels, never other people. The first live channel is the one primary
// action; every other Open is an outline button.
import { computed } from 'vue';

import { useModeratedChannels, useMyChannels } from '@/api/channels';
import { useWatchedChannels, type WatchedChannel } from '@/api/viewer';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty';
import { Skeleton } from '@/components/ui/skeleton';
import { useTime } from '@/composables/useTime';
import { usePanelState } from '@/composables/usePanelState';
import { merge } from '@/lib/array';

import ViewerPage from '../components/ViewerPage.vue';

const time = useTime();

const watched = useWatchedChannels();
const mine = useMyChannels();
const moderated = useModeratedChannels();

const pageState = usePanelState(watched, (data) =>
  data.length === 0);
const channels = computed<readonly WatchedChannel[]>(() => watched.data.value ?? []);
const primaryId = computed<string | undefined>(
  () => channels.value.find((channel) => channel.isLive)?.channelId,
);

// One chip per channel: the same channel can be on both lists.
const managed = computed(() => {
  const both = merge(mine.data.value ?? [], moderated.data.value ?? []);
  const byLogin = new Map(both.map((channel) => [channel.login, channel]));

  return Array.from(byLogin.values());
});

function lastSeen(channel: WatchedChannel): string {
  return channel.lastSeenAt
    ? time.date(channel.lastSeenAt)
    : '';
}
</script>

<template>
  <ViewerPage
    :title="$t('viewer.channels.title')"
    :state="pageState"
    :error-text="$t('viewer.channels.error')"
    :retry-label="$t('viewer.channels.retry')"
    @retry="watched.refetch()"
  >
    <template #loading>
      <ul class="grid gap-3 sm:grid-cols-2">
        <li v-for="slot in 2" :key="slot">
          <Card data-viewer-channel-loading>
            <CardContent class="flex items-center gap-3">
              <Skeleton class="size-8 rounded-full" />
              <Skeleton class="h-4 flex-1" />
              <Skeleton class="h-8 w-16" />
            </CardContent>
          </Card>
        </li>
      </ul>
    </template>

    <template #empty>
      <Empty data-viewer-channels-empty>
        <EmptyHeader>
          <EmptyTitle>{{ $t('viewer.channels.empty.title') }}</EmptyTitle>
          <EmptyDescription>{{ $t('viewer.channels.empty.description') }}</EmptyDescription>
        </EmptyHeader>
      </Empty>
    </template>

    <ul class="grid gap-3 sm:grid-cols-2">
      <li v-for="channel in channels" :key="channel.channelId">
        <Card data-viewer-channel>
          <CardContent class="flex items-center gap-3">
            <Avatar>
              <AvatarImage v-if="channel.profileImageUrl" :src="channel.profileImageUrl" />
              <AvatarFallback>{{ channel.displayName.slice(0, 2).toUpperCase() }}</AvatarFallback>
            </Avatar>
            <div class="flex min-w-0 flex-1 flex-col">
              <span class="truncate font-medium">{{ channel.displayName }}</span>
              <span class="text-xs text-muted-foreground">
                <StatusBadge v-if="channel.isLive" tone="live" :label="$t('viewer.channels.live')" />
                <template v-else-if="lastSeen(channel)">
                  {{ $t('viewer.channels.lastSeen', { date: lastSeen(channel) }) }}
                </template>
              </span>
            </div>
            <Button
              :variant="channel.channelId === primaryId ? 'default' : 'outline'"
              size="sm"
              as-child
            >
              <RouterLink :to="{ name: 'viewer-me', params: { channel: channel.name } }">
                {{ $t('viewer.channels.open') }}
              </RouterLink>
            </Button>
          </CardContent>
        </Card>
      </li>
    </ul>

    <template v-if="managed.length > 0" #footer>
      <section class="flex flex-col gap-3">
        <h2 class="text-base font-semibold">{{ $t('viewer.channels.managed') }}</h2>
        <ul class="flex flex-wrap gap-2">
          <li v-for="channel in managed" :key="channel.login" data-managed-channel>
            <Button variant="outline" size="sm" as-child>
              <RouterLink :to="`/app/u/${channel.login}`">{{ channel.displayName }}</RouterLink>
            </Button>
          </li>
        </ul>
      </section>
    </template>
  </ViewerPage>
</template>

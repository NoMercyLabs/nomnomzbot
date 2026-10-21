<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The viewer home: the channels a person watches, each with a live mark and an Open link, and the channels
// they manage. Own data only: it lists channels, never other people. The first live channel is the one primary
// action; every other Open is an outline button.
import { computed } from 'vue';

import { useModeratedChannels, useMyChannels } from '@/api/channels';
import { useWatchedChannels, type WatchedChannel } from '@/api/viewer';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Empty, EmptyDescription, EmptyHeader, EmptyTitle } from '@/components/ui/empty';
import { useTime } from '@/composables/useTime';
import { merge } from '@/lib/array';

const time = useTime();

const watched = useWatchedChannels();
const mine = useMyChannels();
const moderated = useModeratedChannels();

const channels = computed<readonly WatchedChannel[]>(() => watched.data.value ?? []);
const primaryId = computed<string | undefined>(
  () => channels.value.find((channel) => channel.isLive)?.channelId,
);

const managed = computed(() => merge(mine.data.value ?? [], moderated.data.value ?? []));

function lastSeen(channel: WatchedChannel): string {
  return channel.lastSeenAt
    ? time.date(channel.lastSeenAt)
    : '';
}
</script>

<template>
  <div class="flex flex-col gap-6 px-6">
    <h1 class="text-xl font-semibold">{{ $t('viewer.channels.title') }}</h1>

    <p v-if="watched.isError.value" class="text-sm text-destructive" role="alert">
      {{ $t('viewer.channels.error') }}
      <Button variant="outline" size="sm" @click="watched.refetch()">
        {{ $t('viewer.channels.retry') }}
      </Button>
    </p>

    <Empty v-else-if="watched.isSuccess.value && channels.length === 0" data-viewer-channels-empty>
      <EmptyHeader>
        <EmptyTitle>{{ $t('viewer.channels.empty.title') }}</EmptyTitle>
        <EmptyDescription>{{ $t('viewer.channels.empty.description') }}</EmptyDescription>
      </EmptyHeader>
    </Empty>

    <ul v-else class="grid gap-3 sm:grid-cols-2">
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
                <Badge v-if="channel.isLive">{{ $t('viewer.channels.live') }}</Badge>
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

    <section v-if="managed.length > 0" class="flex flex-col gap-3">
      <h2 class="text-base font-semibold">{{ $t('viewer.channels.managed') }}</h2>
      <ul class="flex flex-wrap gap-2">
        <li v-for="channel in managed" :key="channel.login" data-managed-channel>
          <Button variant="outline" size="sm" as-child>
            <RouterLink :to="`/app/u/${channel.login}`">{{ channel.displayName }}</RouterLink>
          </Button>
        </li>
      </ul>
    </section>
  </div>
</template>

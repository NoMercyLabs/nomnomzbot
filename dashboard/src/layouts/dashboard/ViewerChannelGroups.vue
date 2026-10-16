<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Inside a channel Command: the person's recent viewer chats, then every channel this bot serves that
// matches what they type (chat-client.md 9a "Reaching any channel"). Picking one opens its viewer chat.
import { refDebounced } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter } from 'vue-router';

import { useChannelDirectory } from '@/api/channels';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { CommandGroup, CommandItem, useCommand } from '@/components/ui/command';
import { useRecentChannels, type RecentChannel } from '@/composables/useRecentChannels';
import { initials } from '@/lib/text';

const SEARCH_DELAY_MS = 250;

const emit = defineEmits<{ picked: [] }>();

const { t } = useI18n();
const router = useRouter();
const { filterState } = useCommand();
const { recent, remember } = useRecentChannels();

const typed = computed(() => filterState.search.trim());
const asked = refDebounced(typed, SEARCH_DELAY_MS);
const directory = useChannelDirectory(asked);

const recentLogins = computed(() => new Set(recent.value.map((row) => row.login)));
const found = computed(() =>
  (directory.data.value ?? []).filter((row) => !recentLogins.value.has(row.login)));

// A row with onBot=false is a Twitch channel this deployment does not serve (chat-client.md 9a "Any Twitch channel").
const groups = computed(() => [
  {
    key: 'bot',
    heading: t('frame.channel.every'),
    rows: found.value.filter((row) => row.onBot !== false),
  },
  {
    key: 'twitch',
    heading: t('frame.channel.twitch'),
    rows: found.value.filter((row) => row.onBot === false),
  },
]);

async function open(row: RecentChannel): Promise<void> {
  remember(row);
  emit('picked');

  await router.push({
    name: 'viewer-chat',
    params: { channel: row.login },
  });
}
</script>

<template>
  <CommandGroup v-if="recent.length > 0" :heading="t('frame.channel.recent')">
    <CommandItem
      v-for="row in recent"
      :key="row.login"
      :value="`${row.displayName} ${row.login}`"
      data-slot="recent-channel"
      @select="open(row)"
    >
      <Avatar class="size-6 rounded-md">
        <AvatarImage v-if="row.profileImageUrl" :src="row.profileImageUrl" :alt="''" />
        <AvatarFallback class="rounded-md text-xs">{{ initials(row.displayName) }}</AvatarFallback>
      </Avatar>
      <span class="min-w-0 flex-1 truncate">{{ row.displayName }}</span>
    </CommandItem>
  </CommandGroup>
  <template v-for="group in groups" :key="group.key">
    <CommandGroup
      v-if="group.rows.length > 0"
      :heading="group.heading"
      :aria-busy="directory.isFetching.value"
    >
      <CommandItem
        v-for="row in group.rows"
        :key="row.login"
        :value="`${row.displayName} ${row.login}`"
        :data-slot="group.key === 'bot' ? 'directory-channel' : 'twitch-channel'"
        @select="open(row)"
      >
        <Avatar class="size-6 rounded-md">
          <AvatarImage v-if="row.profileImageUrl" :src="row.profileImageUrl" :alt="''" />
          <AvatarFallback class="rounded-md text-xs">{{ initials(row.displayName) }}</AvatarFallback>
        </Avatar>
        <span class="min-w-0 flex-1 truncate">{{ row.displayName }}</span>
        <span
          v-if="row.isLive"
          class="flex items-center gap-1.5 text-xs text-muted-foreground"
        >
          <span class="size-2 rounded-full bg-success" aria-hidden="true" />
          {{ t('frame.channel.live') }}
        </span>
      </CommandItem>
    </CommandGroup>
  </template>
</template>

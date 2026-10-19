<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A person's whole stored chat in this channel, newest first, one small heading per calendar day (local time)
// and only the time on each line. Older pages load on request and join the day group they continue.
import { computed } from 'vue';

import { useUserChat, type ChatLine } from '@/api/chat';
import EmoteText from '@/components/layout/EmoteText.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { lineText } from '@/components/layout/profile/profileChat';
import { useTime } from '@/composables/useTime';

const props = defineProps<{ internalUserId: string | null }>();

const time = useTime();
const { channelId } = useCurrentChannel();

type DayGroup = {
  day: string;
  heading: string;
  lines: ChatLine[];
};

const chat = useUserChat(channelId, () => props.internalUserId);
// A line with no words and no pieces would be an empty row, so it is left out.
const lines = computed(() =>
  (chat.data.value?.pages ?? [])
    .flatMap((page) =>
      page.messages)
    .filter((line) =>
      lineText(line) !== ''));
const state = usePanelState(chat, () =>
  lines.value.length === 0);

// Local calendar day of a line, the key of its group; it is also the heading the group shows.
function dayOf(iso: string): string {
  return time.date(iso);
}

function timeOf(iso: string): string {
  return time.time(iso);
}

const groups = computed(() => {
  const found: DayGroup[] = [];

  for (const line of lines.value) {
    const day = dayOf(line.timestamp);
    const last = found[found.length - 1];

    if (last?.day === day) {
      last.lines.push(line);
    } else {
      found.push({
        day,
        heading: day,
        lines: [line],
      });
    }
  }

  return found;
});
</script>

<template>
  <PanelCard
    data-slot="profile-chat"
    bare
    :title="$t('profile.chat.title')"
    :state="state"
    :empty-text="$t('profile.chat.empty')"
    :error-text="$t('profile.chat.error')"
    @retry="chat.refetch()"
  >
    <div data-profile-chat-lines :aria-label="$t('profile.chat.title')" class="grid gap-4">
      <section v-for="group in groups" :key="group.day" data-profile-chat-day class="grid gap-2">
        <h4 data-profile-chat-day-heading class="text-xs text-muted-foreground">{{ group.heading }}</h4>
        <ul class="grid gap-2">
          <li v-for="line in group.lines" :key="line.id" class="flex gap-2 text-sm">
            <span class="shrink-0 text-xs text-muted-foreground">{{ timeOf(line.timestamp) }}</span>
            <EmoteText class="wrap-anywhere" :text="lineText(line)" />
          </li>
        </ul>
      </section>
    </div>

    <template v-if="chat.hasNextPage.value" #footer>
      <Button
        data-profile-chat-more
        type="button"
        variant="ghost"
        size="sm"
        :disabled="chat.isFetchingNextPage.value"
        @click="chat.fetchNextPage()"
      >
        {{ $t('profile.chat.more') }}
      </Button>
    </template>
  </PanelCard>
</template>

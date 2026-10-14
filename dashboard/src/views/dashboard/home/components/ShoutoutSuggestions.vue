<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Chatters of this stream worth a shoutout (owner 2026-10-12). Shoutout sends it and marks the
// row sent; Not now drops the chatter for this stream. No one to suggest: no group at all.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import { useShoutout } from '@/api/people';
import {
  useDismissShoutoutSuggestion,
  useShoutoutSuggestions,
  type ShoutoutSuggestion,
} from '@/api/shoutouts';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';

const props = defineProps<{ channelId: string | null }>();

const { t } = useI18n();

const suggestionsQuery = useShoutoutSuggestions(() => props.channelId);
const shoutout = useShoutout(() => props.channelId);
const dismiss = useDismissShoutoutSuggestion(() => props.channelId);

const sent = ref<Set<string>>(new Set());
const refused = ref<Set<string>>(new Set());

const suggestions = computed<ShoutoutSuggestion[]>(() => suggestionsQuery.data.value ?? []);
const waiting = computed(() =>
  suggestions.value.filter((entry) => !sent.value.has(entry.twitchUserId)).length);

function detailOf(entry: ShoutoutSuggestion): string {
  if (sent.value.has(entry.twitchUserId))
    return t('dashboard.home.shoutouts.sent');

  if (refused.value.has(entry.twitchUserId))
    return t('dashboard.home.shoutouts.failed');

  const streams = t('dashboard.home.shoutouts.streams', Number(entry.streamsLast14Days));

  return entry.lastCategory === null
    ? streams
    : `${streams} · ${entry.lastCategory}`;
}

async function send(entry: ShoutoutSuggestion): Promise<void> {
  refused.value.delete(entry.twitchUserId);

  try {
    await shoutout.mutateAsync({ targetTwitchUserId: entry.twitchUserId });
    sent.value.add(entry.twitchUserId);
  } catch {
    refused.value.add(entry.twitchUserId);
  }
}
</script>

<template>
  <section v-if="suggestions.length > 0" data-shoutout-suggestions class="border-t border-border">
    <h3 data-suggestions-header class="px-3 pt-1 text-xs font-medium text-muted-foreground">
      {{ t('dashboard.home.shoutouts.title', { count: waiting }) }}
    </h3>
    <RowGroup>
      <div
        v-for="entry in suggestions"
        :key="entry.twitchUserId"
        :data-suggestion="entry.twitchUserId"
      >
        <SummaryRow :title="entry.displayName" :detail="detailOf(entry)">
          <template #title>
            <UserName :user-id="entry.twitchUserId" :name="entry.displayName" />
          </template>
          <template v-if="!sent.has(entry.twitchUserId)" #trailing>
            <Button
              variant="outline"
              size="sm"
              :disabled="shoutout.isPending.value"
              @click="send(entry)"
            >
              {{ t('dashboard.home.shoutouts.send') }}
            </Button>
            <Button
              variant="ghost"
              size="sm"
              :disabled="dismiss.isPending.value"
              @click="dismiss.mutate(entry.twitchUserId)"
            >
              {{ t('dashboard.home.shoutouts.later') }}
            </Button>
          </template>
        </SummaryRow>
      </div>
    </RowGroup>
  </section>
</template>

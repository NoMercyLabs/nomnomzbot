<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Everything moderation did to a person in this channel, newest first: what happened, why, for how long and
// who did it. A row with no moderator was the bot's own (a filter, AutoMod). Older rows load on request.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useChannelAccess } from '@/api/access';
import {
  useModerationHistory,
  type ModerationHistoryEntry,
  type ModerationHistoryPage,
} from '@/api/moderation';
import PanelCard from '@/components/layout/PanelCard.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { numberOrNull } from '@/lib/number';
import { formatLength, shortDate } from '@/lib/time';

const props = defineProps<{ internalUserId: string | null }>();

const { t, te, locale } = useI18n();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);

const canRead = computed(() => access.can('moderation:history:read'));

const history = useModerationHistory(channelId, () =>
  canRead.value
    ? props.internalUserId
    : null);
const entries = computed(() =>
  (history.data.value?.pages ?? []).flatMap((page: ModerationHistoryPage) =>
    page.entries));
const listState = usePanelState(history, () =>
  entries.value.length === 0);
const state = computed(() =>
  canRead.value && props.internalUserId
    ? listState.value
    : 'empty');
const emptyText = computed(() =>
  canRead.value
    ? t('profile.history.empty')
    : t('profile.history.noAccess'));

function what(entry: ModerationHistoryEntry): string {
  const seconds = numberOrNull(entry.durationSeconds);

  if (entry.actionType === 'timeout' && seconds !== null)
    return t('profile.history.timedOutFor', { length: formatLength(seconds, locale.value) });

  const key = `profile.history.kinds.${entry.actionType}`;

  return te(key)
    ? t(key)
    : t('profile.history.kinds.other');
}

function byline(entry: ModerationHistoryEntry): string {
  const date = shortDate(entry.occurredAt, locale.value);

  return entry.moderatorDisplayName
    ? t('profile.history.by', { name: entry.moderatorDisplayName, date })
    : t('profile.history.automatic', { date });
}
</script>

<template>
  <PanelCard
    data-slot="profile-history"
    :title="$t('profile.history.title')"
    :state="state"
    :empty-text="emptyText"
    :error-text="$t('profile.history.error')"
    @retry="history.refetch()"
  >
    <ul class="grid gap-2 p-2 text-sm">
      <li v-for="entry in entries" :key="entry.id" data-profile-history class="grid gap-0.5">
        <span class="font-medium">{{ what(entry) }}</span>
        <span v-if="entry.reason" class="wrap-break-word">{{ entry.reason }}</span>
        <span class="text-xs text-muted-foreground">{{ byline(entry) }}</span>
      </li>
    </ul>

    <template v-if="history.hasNextPage.value" #footer>
      <Button
        data-profile-history-more
        type="button"
        variant="ghost"
        size="sm"
        :disabled="history.isFetchingNextPage.value"
        @click="history.fetchNextPage()"
      >
        {{ $t('profile.history.more') }}
      </Button>
    </template>
  </PanelCard>
</template>

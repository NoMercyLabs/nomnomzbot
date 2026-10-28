<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { computed, type Component } from 'vue';
import { useI18n } from 'vue-i18n';
import { RouterLink, useRouter } from 'vue-router';
import { toast } from 'vue-sonner';

import { useChannelAccess } from '@/api/access';
import {
  ActivityReplayError,
  useActivity,
  useActivityReplay,
  type ActivityEvent,
} from '@/api/home';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { useTime } from '@/composables/useTime';

import ArrowRight from '~icons/nnz/arrow-right';
import Repeat from '~icons/nnz/repeat';

import { describeActivity, type ActivityRowText } from './activityKinds';
import { SHRINK_WHEN_EMPTY } from './shrinkWhenEmpty';

defineProps<{ live: boolean }>();

const { t } = useI18n();
const clock = useTime();
const { channelId } = useCurrentChannel();
const access = useChannelAccess(channelId);
const replay = useActivityReplay(channelId);

async function replayEvent(eventId: string): Promise<void> {
  try {
    const result = await replay.mutateAsync(eventId);

    const message = t(Number(result.eventsReplayed) === 1
      ? 'dashboard.home.activity.replayResultOne'
      : 'dashboard.home.activity.replayResult', {
      events: result.eventsReplayed,
      chat: result.chatMessagesSent,
      voice: result.ttsQueued,
      overlays: result.overlaysShown,
      widgets: result.widgetsNotified,
    });

    toast.success(message);
  } catch (error) {
    const status = error instanceof ActivityReplayError
      ? error.status
      : null;

    // 409: the event was found but nothing went out. A warning, so it never reads as a replay.
    if (status === 409) {
      toast.warning(t('dashboard.home.activity.replayNothing'));

      return;
    }

    toast.error(t(status === 404
      ? 'dashboard.home.activity.replayNotFound'
      : 'dashboard.home.activity.replayFailed'));
  }
}

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

type Row = {
  id: string;
  icon: Component;
  title: string;
  // Words before and after the linked name; Vue trims a space at the edge of a tag, so each is its own field.
  lead: string | null;
  suffix: string | null;
  userId: string | null;
  username: string | null;
  color: string | null;
  detail: string | undefined;
  time: string;
};

function say(text: ActivityRowText): string {
  const count = text.params?.count;

  return t(text.key, text.params ?? {}, typeof count === 'number'
    ? count
    : 1);
}

function eventTime(event: ActivityEvent): string {
  return clock.weekdayTime(event.timestamp);
}

// The row title when the name is not a profile link: lead, name and suffix read as one sentence.
function plainTitle(
  lead: string | null,
  username: string | null,
  suffix: string | null,
): string | undefined {
  // Words around a name that is missing say nothing, so the row falls back to its detail.
  if (lead !== null && username === null)
    return undefined;

  const words = [lead, username, suffix].filter(Boolean);

  return words.length === 0
    ? undefined
    : words.join(' ');
}

const rows = computed<Row[]>(() =>
  (activityQuery.data.value ?? []).map((event) => {
    const view = describeActivity(event);
    const detail = say(view.detail);
    const username = event.username ?? view.actor ?? null;
    const lead = view.lead
      ? say(view.lead)
      : null;
    const suffix = view.title
      ? say(view.title)
      : null;
    const title = plainTitle(lead, username, suffix);

    return {
      id: event.id,
      icon: view.icon,
      title: title ?? detail,
      lead,
      suffix,
      userId: event.twitchUserId ?? null,
      username,
      color: event.color ?? null,
      detail: title === undefined
        ? undefined
        : detail,
      time: eventTime(event),
    } as const satisfies Row;
  }),
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
    <RowGroup>
      <div v-for="row in rows" :key="row.id" data-activity-row>
        <SummaryRow :title="row.title" :detail="row.detail">
          <template v-if="row.userId && row.username" #title>
            <template v-if="row.lead">{{ row.lead }}{{ ' ' }}</template>
            <UserName :user-id="row.userId" :name="row.username" :color="row.color" />
            <template v-if="row.suffix">{{ ' ' }}{{ row.suffix }}</template>
          </template>
          <template #leading>
            <component
              :is="row.icon"
              class="size-5 flex-none text-muted-foreground"
              aria-hidden="true"
            />
          </template>
          <template #trailing>
            <span class="text-sm text-muted-foreground tabular-nums">{{ row.time }}</span>
            <Tooltip v-if="access.can('dashboard:replay')">
              <TooltipTrigger as-child>
                <Button
                  data-activity-replay
                  variant="ghost"
                  size="icon-sm"
                  :aria-label="t('dashboard.home.activity.replay')"
                  :disabled="replay.isPending.value"
                  @click="replayEvent(row.id)"
                >
                  <Repeat class="size-4" aria-hidden="true" />
                </Button>
              </TooltipTrigger>
              <TooltipContent>{{ t('dashboard.home.activity.replayTip') }}</TooltipContent>
            </Tooltip>
          </template>
        </SummaryRow>
      </div>
    </RowGroup>
  </PanelCard>
</template>

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
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

import ArrowRight from '~icons/nnz/arrow-right';
import Repeat from '~icons/nnz/repeat';

import { describeActivity, type ActivityRowText } from './activityKinds';

defineProps<{ live: boolean }>();

const { t, locale } = useI18n();
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
  userId: string | null;
  username: string | null;
  detail: string | undefined;
  time: string;
};

function say(text: ActivityRowText): string {
  const count: unknown = text.params?.count;

  return t(text.key, text.params ?? {}, typeof count === 'number'
    ? count
    : 1);
}

function eventTime(event: ActivityEvent): string {
  return new Intl.DateTimeFormat(locale.value, {
    weekday: 'short',
    hour: '2-digit',
    minute: '2-digit',
    hourCycle: 'h23',
  }).format(new Date(event.timestamp));
}

const rows = computed<Row[]>(() =>
  (activityQuery.data.value ?? []).map((event: ActivityEvent): Row => {
    const view = describeActivity(event);
    const detail = say(view.detail);
    const title = view.title
      ? say(view.title)
      : (event.username ?? view.actor ?? undefined);

    return {
      id: event.id,
      icon: view.icon,
      title: title ?? detail,
      userId: event.userId ?? null,
      username: event.username ?? view.actor ?? null,
      detail: title === undefined
        ? undefined
        : detail,
      time: eventTime(event),
    };
  }),
);
</script>

<template>
  <PanelCard
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
            <UserName :user-id="row.userId" :name="row.username" />
            <span v-if="row.title !== row.username"> {{ row.title }}</span>
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

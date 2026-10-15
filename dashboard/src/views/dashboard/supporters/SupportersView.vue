<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Who supports the channel: Twitch subs and gifts, cheers, and tips from the connected providers.
// Twitch rows come from the activity feed; tips come from the supporter events list.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { useActivity, useHomeStats } from '@/api/home';
import { useSupporterEvents, type SupporterEvent } from '@/api/supporters';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import StatStrip from '@/components/layout/StatStrip.vue';
import StatTile from '@/components/layout/StatTile.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import UserName from '@/components/layout/UserName.vue';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import GiftCard from '~icons/nnz/gift-card';

import {
  cheerRows,
  subscriptionRows,
  topCheerers,
  topGifters,
  type CheerRow,
  type Person,
  type SubscriptionRow,
} from './supporterRows';

const { t, n, locale } = useI18n();
const { channelId } = useCurrentChannel();

const stats = useHomeStats(channelId);
const activity = useActivity(channelId);
const events = useSupporterEvents(channelId);

const subscriptions = computed<SubscriptionRow[]>(() =>
  subscriptionRows(activity.data.value ?? []));
const cheers = computed<CheerRow[]>(() => cheerRows(activity.data.value ?? []));
const cheerers = computed(() => topCheerers(cheers.value));
const gifters = computed(() => topGifters(subscriptions.value));
const subscriberCount = computed<number>(() => Number(stats.data.value?.subscriberCount ?? 0));

const subscriptionsState = usePanelState(activity, () =>
  subscriptions.value.length === 0);
const cheersState = usePanelState(activity, () =>
  cheers.value.length === 0);
const topState = usePanelState(
  activity,
  () => cheerers.value.length === 0 && gifters.value.length === 0,
);
const eventsState = usePanelState(events, (rows: SupporterEvent[]) =>
  rows.length === 0);

function when(timestamp: string): string {
  return new Intl.DateTimeFormat(locale.value, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(new Date(timestamp));
}

function subscriptionDetail(row: SubscriptionRow): string {
  const tier = row.tier ?? '1';

  if (row.kind === 'gift')
    return t('dashboard.supporters.subs.gifted', { count: row.giftCount ?? 1, tier }, row.giftCount ?? 1);

  return row.months !== null && row.months > 1
    ? t('dashboard.supporters.subs.subscribedMonths', { tier, months: row.months })
    : t('dashboard.supporters.subs.subscribed', { tier });
}

function tipAmount(event: SupporterEvent): string | null {
  if (event.amountMinor === null || event.amountMinor === undefined || !event.currency)
    return null;

  return new Intl.NumberFormat(undefined, { style: 'currency', currency: event.currency })
    .format(Number(event.amountMinor) / 100);
}

function tipDetail(event: SupporterEvent): string {
  const parts = [t(`dashboard.supporters.tips.kind.${event.kind}`, event.kind), tipAmount(event), event.tier];

  return parts.filter((part) => part !== null && part !== undefined && part !== '').join(' · ');
}

function isNamed(person: Person): person is Person & { userId: string; name: string } {
  return person.userId !== null && person.name !== null;
}
</script>

<template>
  <PageHeader :title="t('dashboard.supporters.title')" />
  <PageBody>
    <StatStrip>
      <StatTile
        :label="t('dashboard.supporters.stats.subscribers')"
        :value="subscriberCount"
        :context="t('dashboard.supporters.stats.subscribersContext')"
      />
      <StatTile
        :label="t('dashboard.supporters.stats.gifts')"
        :value="subscriptions.filter(row => row.kind === 'gift').length"
        :context="t('dashboard.supporters.stats.recent')"
      />
      <StatTile
        :label="t('dashboard.supporters.stats.bits')"
        :value="n(cheers.reduce((sum, row) => sum + row.bits, 0))"
        :context="t('dashboard.supporters.stats.recent')"
      />
    </StatStrip>
    <div class="grid gap-4 lg:grid-cols-2">
      <PanelCard
        tier="lead"
        :title="t('dashboard.supporters.subs.title')"
        :state="subscriptionsState"
        :empty-text="t('dashboard.supporters.subs.empty')"
        :error-text="t('dashboard.supporters.subs.error')"
        @retry="activity.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="row in subscriptions"
            :key="row.id"
            :title="row.person.name ?? ''"
            :detail="subscriptionDetail(row)"
            :meta="when(row.timestamp)"
          >
            <template #leading>
              <GiftCard
                v-if="row.kind === 'gift' || row.receivedAsGift"
                class="size-5 text-muted-foreground"
                role="img"
                :aria-label="t('dashboard.supporters.subs.giftMark')"
              />
            </template>
            <template #title>
              <UserName
                v-if="isNamed(row.person)"
                :user-id="row.person.userId"
                :name="row.person.name"
                :color="row.person.color"
              />
              <span v-else>{{ t('dashboard.supporters.anonymous') }}</span>
              <span v-if="row.receivedAsGift" class="text-muted-foreground">
                {{ t('dashboard.supporters.subs.receivedGiftDetail') }}
              </span>
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
      <PanelCard
        :title="t('dashboard.supporters.cheers.title')"
        :state="cheersState"
        :empty-text="t('dashboard.supporters.cheers.empty')"
        :error-text="t('dashboard.supporters.cheers.error')"
        @retry="activity.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="row in cheers"
            :key="row.id"
            :title="row.person.name ?? ''"
            :detail="t('dashboard.supporters.cheers.bits', { count: row.bits }, row.bits)"
            :meta="when(row.timestamp)"
          >
            <template #title>
              <UserName
                v-if="isNamed(row.person)"
                :user-id="row.person.userId"
                :name="row.person.name"
                :color="row.person.color"
              />
              <span v-else>{{ t('dashboard.supporters.anonymous') }}</span>
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
      <PanelCard
        :title="t('dashboard.supporters.top.title')"
        :description="t('dashboard.supporters.top.note')"
        :state="topState"
        :empty-text="t('dashboard.supporters.top.empty')"
        :error-text="t('dashboard.supporters.top.error')"
        @retry="activity.refetch()"
      >
        <h3 class="mb-2 text-sm font-medium text-muted-foreground">
          {{ t('dashboard.supporters.top.cheerers') }}
        </h3>
        <RowGroup v-if="cheerers.length > 0">
          <SummaryRow
            v-for="entry in cheerers"
            :key="`bits-${entry.key}`"
            :title="entry.person.name ?? ''"
            :detail="t('dashboard.supporters.cheers.bits', { count: entry.total }, entry.total)"
          >
            <template #title>
              <UserName
                v-if="isNamed(entry.person)"
                :user-id="entry.person.userId"
                :name="entry.person.name"
                :color="entry.person.color"
              />
            </template>
          </SummaryRow>
        </RowGroup>
        <h3 class="mt-4 mb-2 text-sm font-medium text-muted-foreground">
          {{ t('dashboard.supporters.top.gifters') }}
        </h3>
        <RowGroup v-if="gifters.length > 0">
          <SummaryRow
            v-for="entry in gifters"
            :key="`gift-${entry.key}`"
            :title="entry.person.name ?? ''"
            :detail="t('dashboard.supporters.top.subs', { count: entry.total }, entry.total)"
          >
            <template #title>
              <UserName
                v-if="isNamed(entry.person)"
                :user-id="entry.person.userId"
                :name="entry.person.name"
                :color="entry.person.color"
              />
            </template>
          </SummaryRow>
        </RowGroup>
      </PanelCard>
      <PanelCard
        :title="t('dashboard.supporters.tips.title')"
        :state="eventsState"
        :empty-text="t('dashboard.supporters.tips.empty')"
        :error-text="t('dashboard.supporters.tips.error')"
        @retry="events.refetch()"
      >
        <RowGroup>
          <SummaryRow
            v-for="event in events.data.value ?? []"
            :key="event.id"
            :title="event.supporterDisplayName"
            :detail="tipDetail(event)"
            :meta="when(event.receivedAt)"
          />
        </RowGroup>
      </PanelCard>
    </div>
  </PageBody>
</template>

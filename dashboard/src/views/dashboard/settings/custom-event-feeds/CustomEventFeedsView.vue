<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Custom event feeds: outside sources (a heart-rate sensor, a REST ticker, a companion app) whose data becomes a
// `custom.<name>` event. Each feed fires pipelines, fills {{custom.<name>.*}} variables and feeds overlays.
// The list follows the bot: a feed made, changed or fed from anywhere shows without a reload.
import Add from '~icons/nnz/add';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  fetchEventFeedBlastRadius,
  useDeleteEventFeed,
  useEventFeedPresets,
  useEventFeeds,
  useSaveEventFeed,
  type EventFeed,
  type EventFeedBlastRadius,
  type EventFeedDraft,
} from '@/api/customEventFeeds';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';
import { errorText, fieldErrorLines } from '@/lib/errors';

import EventFeedDialog from './components/EventFeedDialog.vue';
import EventFeedRow from './components/EventFeedRow.vue';
import TestFeedDialog from './components/TestFeedDialog.vue';

const { t, te } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const feeds = useEventFeeds(channelId);
const presets = useEventFeedPresets(channelId);
const save = useSaveEventFeed(channelId);
const remove = useDeleteEventFeed(channelId);

const state = usePanelState(feeds, (rows) =>
  rows.length === 0);

const rows = computed(() => feeds.data.value ?? []);

const editorOpen = ref(false);
const editing = ref<EventFeed | null>(null);
const saveError = ref<string | undefined>(undefined);
const pageError = ref<string | undefined>(undefined);
const busyId = ref<string | undefined>(undefined);

const testOpen = ref(false);
const testing = ref<EventFeed | null>(null);

const confirmOpen = ref(false);
const deleting = ref<EventFeed | null>(null);
const consequences = ref<string[]>([]);

function openEditor(feed: EventFeed | null): void {
  editing.value = feed;
  saveError.value = undefined;
  editorOpen.value = true;
}

function openTest(feed: EventFeed): void {
  testing.value = feed;
  testOpen.value = true;
}

async function submit(draft: EventFeedDraft): Promise<void> {
  saveError.value = undefined;

  try {
    await save.mutateAsync({ id: editing.value?.id ?? null, draft });
    editorOpen.value = false;
  } catch (failure) {
    saveError.value = [errorText(failure, t), ...fieldErrorLines(failure)].join(' ');
  }
}

// The switch saves the feed as it is with the other state. A saved secret stays: an empty secret keeps it.
function draftOf(feed: EventFeed, isEnabled: boolean) {
  return {
    name: feed.name,
    displayName: feed.displayName,
    sourceKind: feed.sourceKind,
    presetKey: feed.presetKey ?? null,
    endpointUrl: feed.endpointUrl ?? null,
    authSecret: null,
    fieldMap: feed.fieldMap,
    pollIntervalSeconds: feed.pollIntervalSeconds ?? null,
    isEnabled,
  } as const satisfies EventFeedDraft;
}

async function flip(feed: EventFeed): Promise<void> {
  if (busyId.value !== undefined) return;

  pageError.value = undefined;
  busyId.value = feed.id;

  try {
    await save.mutateAsync({ id: feed.id, draft: draftOf(feed, !feed.isEnabled) });
  } catch (failure) {
    pageError.value = errorText(failure, t);
  } finally {
    busyId.value = undefined;
  }
}

function categoryLabel(key: string): string {
  const path = `dashboard.settings.customEventFeeds.delete.category.${key}`;

  return te(path)
    ? t(path)
    : key.replaceAll('_', ' ');
}

function blastLines(blast: EventFeedBlastRadius): string[] {
  const lines = blast.categories
    .filter((category) => Number(category.count) > 0)
    .map((category) =>
      t(
        blast.isMinimum
          ? 'dashboard.settings.customEventFeeds.delete.lineAtLeast'
          : 'dashboard.settings.customEventFeeds.delete.line',
        { count: Number(category.count), what: categoryLabel(category.categoryKey) },
      ));

  return lines.length > 0
    ? lines
    : [t('dashboard.settings.customEventFeeds.delete.nothing')];
}

// The blast radius is counted before the confirm opens: a feed whose reach cannot be read is not offered for delete.
async function askDelete(feed: EventFeed): Promise<void> {
  pageError.value = undefined;

  try {
    consequences.value = blastLines(await fetchEventFeedBlastRadius(feed.id));
    deleting.value = feed;
    confirmOpen.value = true;
  } catch (failure) {
    pageError.value = errorText(failure, t);
  }
}

async function confirmDelete(): Promise<void> {
  if (deleting.value === null) return;

  try {
    await remove.mutateAsync(deleting.value.id);
  } catch (failure) {
    pageError.value = errorText(failure, t);
  }

  confirmOpen.value = false;
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.customEventFeeds')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="custom-event-feeds"
      tier="lead"
      :title="$t('dashboard.settings.customEventFeeds.title')"
      :description="$t('dashboard.settings.customEventFeeds.description')"
      :state="state"
      :empty-text="$t('dashboard.settings.customEventFeeds.empty')"
      :error-text="$t('dashboard.settings.customEventFeeds.loadFailed')"
      @retry="feeds.refetch()"
    >
      <template v-if="can('customdata:write')" #actions>
        <Button data-new-feed @click="openEditor(null)">
          <Add class="size-5" />
          {{ $t('dashboard.settings.customEventFeeds.new') }}
        </Button>
      </template>
      <RowGroup>
        <EventFeedRow
          v-for="feed in rows"
          :key="feed.id"
          :feed="feed"
          :busy="busyId !== undefined"
          @toggle="flip(feed)"
          @test="openTest(feed)"
          @edit="openEditor(feed)"
          @remove="askDelete(feed)"
        />
      </RowGroup>
    </PanelCard>
    <p v-if="pageError" role="alert" class="pt-3 text-sm text-destructive">{{ pageError }}</p>
  </PageBody>

  <EventFeedDialog
    v-if="can('customdata:write')"
    v-model:open="editorOpen"
    :feed="editing"
    :presets="presets.data.value ?? []"
    :pending="save.isPending.value"
    :error="saveError"
    @submit="submit"
  />
  <TestFeedDialog v-if="can('customdata:write')" v-model:open="testOpen" :feed="testing" />
  <ConfirmDialog
    v-model:open="confirmOpen"
    :title="$t('dashboard.settings.customEventFeeds.delete.title', { name: deleting?.displayName ?? '' })"
    :description="$t('dashboard.settings.customEventFeeds.delete.description')"
    :consequences="consequences"
    :action="$t('dashboard.settings.customEventFeeds.delete.action')"
    :pending="remove.isPending.value"
    @confirm="confirmDelete"
  />
</template>

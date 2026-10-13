<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The live chat of the open channel: the feed, the moderation on each line, and the send box.
// The hub keeps the feed cache current; this page only reads it. Ban asks once, here, and
// bans in this channel only (the all-channels ban is its own opt-in action elsewhere).
import { refDebounced, useEventListener } from '@vueuse/core';
import { computed, ref, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import Cross from '~icons/nnz/cross';
import SettingsFader from '~icons/nnz/settings-fader';

import { useCurrentUser } from '@/api/auth';
import {
  chatSearchMinLength,
  useBanUser,
  useChatHistory,
  useChatSearch,
  type ChatLine as ChatLineData,
} from '@/api/chat';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { useChatDisplay } from './chatDisplay';
import ChatComposer from './components/ChatComposer.vue';
import ChatDisplayPanel from './components/ChatDisplayPanel.vue';
import ChatLine from './components/ChatLine.vue';
import ViewerPanel from './components/ViewerPanel.vue';

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();

const history = useChatHistory(channelId);
const banner = useBanUser(channelId);

const display = useChatDisplay();
const displayOpen = ref(false);
const focusedUserId = ref<string | null>(null);
const currentUser = useCurrentUser();
const me = computed<string | null>(() => currentUser.data.value?.username ?? null);
const banTarget = ref<ChatLineData | null>(null);
const banFailed = ref(false);

const lines = computed<ChatLineData[]>(() => history.data.value ?? []);
const visible = computed<ChatLineData[]>(() =>
  lines.value.filter(
    (line: ChatLineData) =>
      !(!display.value.commands && line.isCommand) &&
      !(display.value.deleted === 'hide' && line.removed === 'deleted'),
  ));

const panelState = usePanelState(history, () =>
  visible.value.length === 0);
const emptyText = computed<string>(() =>
  lines.value.length > 0
    ? t('dashboard.chat.feed.emptyCommands')
    : t('dashboard.chat.feed.empty'),
);
const live = computed(() => current.value?.isLive === true);

// Search swaps the live feed for the matches from the whole stored history, newest first.
// A page can come back empty with a cursor (the server scans a bounded slice), so an empty
// result keeps going back by itself; "Search further back" asks for older matches.
const SEARCH_PAUSE_MS = 250;
const search = ref('');
const typed = refDebounced(computed(() => search.value.trim()), SEARCH_PAUSE_MS);
const searching = computed(() => typed.value.length >= chatSearchMinLength);
const found = useChatSearch(channelId, typed);
const foundLines = computed<ChatLineData[]>(() =>
  (found.data.value?.pages ?? []).flatMap((page) => page.messages));
const searchState = usePanelState(found, () =>
  foundLines.value.length === 0);
// Only a finished search says it reached the first message; a failed one says nothing of the sort.
const searchDone = computed(() =>
  searchState.value === 'ready' || searchState.value === 'empty');

watchEffect(() => {
  if (foundLines.value.length > 0 || !found.hasNextPage.value || found.isFetching.value) return;

  void found.fetchNextPage();
});

const feedLines = computed<ChatLineData[]>(() =>
  searching.value
    ? foundLines.value
    : visible.value);

const feedPanel = computed(() =>
  searching.value
    ? {
        title: t('dashboard.chat.search.title'),
        description: t('dashboard.chat.search.count', { count: foundLines.value.length, query: typed.value }),
        state: searchState.value,
        emptyText: t('dashboard.chat.search.empty', { query: typed.value }),
        errorText: t('dashboard.chat.search.error'),
      }
    : {
        title: t('dashboard.chat.feed.title'),
        description: t('dashboard.chat.feed.count', { count: visible.value.length }),
        state: panelState.value,
        emptyText: emptyText.value,
        errorText: t('dashboard.chat.feed.error'),
      });

function retryFeed(): void {
  if (searching.value) {
    void found.refetch();

    return;
  }

  void history.refetch();
}

const banOpen = computed(() => banTarget.value !== null);

// Focus on one chatter: their lines light up, the rest step back, the Viewer panel shows them.
// Picking the same chatter again, Escape, or closing the Viewer ends it.
function focusOf(line: ChatLineData): 'on' | 'dim' | undefined {
  if (focusedUserId.value === null)
    return undefined;

  return line.userId === focusedUserId.value
    ? 'on'
    : 'dim';
}

function pickChatter(line: ChatLineData): void {
  focusedUserId.value = focusedUserId.value === line.userId
    ? null
    : line.userId;
}

const focusedName = computed<string>(() =>
  lines.value.filter((line: ChatLineData) =>
    line.userId === focusedUserId.value).at(-1)?.displayName ?? '');

function closeViewer(): void {
  focusedUserId.value = null;
}

function clearFocusOnEscape(event: KeyboardEvent): void {
  if (event.key === 'Escape') {
    focusedUserId.value = null;
  }
}

useEventListener(document, 'keydown', clearFocusOnEscape);

function askBan(line: ChatLineData): void {
  banFailed.value = false;
  banTarget.value = line;
}

function closeBan(open: boolean): void {
  if (!open) {
    banTarget.value = null;
  }
}

async function confirmBan(): Promise<void> {
  const target = banTarget.value;

  if (!target) return;

  try {
    await banner.mutateAsync({ targetUserId: target.userId, scope: 'this_channel' });
    banTarget.value = null;
  } catch {
    banTarget.value = null;
    banFailed.value = true;
  }
}
</script>

<template>
  <PageHeader :title="$t('dashboard.chat.title')" :crumb="$t('dashboard.chat.crumb')">
    <template #status>
      <StatusBadge
        :tone="live ? 'live' : 'off'"
        :label="live ? $t('dashboard.chat.status.live') : $t('dashboard.chat.status.offline')"
      />
    </template>
    <template #bar>
      <div data-chat-controls class="ml-auto flex items-center gap-2">
        <SearchField v-model="search" :placeholder="$t('dashboard.chat.search.placeholder')" />
        <Button
          data-display-toggle
          variant="outline"
          size="sm"
          :aria-pressed="displayOpen"
          @click="displayOpen = !displayOpen"
        >
          <SettingsFader class="size-4" aria-hidden="true" />
          {{ $t('dashboard.chat.display.toggle') }}
        </Button>
      </div>
    </template>
  </PageHeader>
  <div class="flex min-h-0 flex-1 flex-col gap-3 p-6">
    <p v-if="banFailed" role="alert" class="text-sm text-destructive">
      {{ $t('dashboard.chat.ban.failed') }}
    </p>
    <div class="flex min-h-0 flex-1 gap-3">
      <PanelCard
        class="min-h-96 flex-1"
        tier="lead"
        v-bind="feedPanel"
        @retry="retryFeed"
      >
        <template v-if="focusedUserId !== null" #actions>
          <Button data-focus-clear variant="ghost" size="sm" @click="focusedUserId = null">
            <Cross class="size-4" aria-hidden="true" />
            {{ $t('dashboard.chat.viewer.clearFocus', { name: focusedName }) }}
          </Button>
        </template>
        <div class="flex h-full min-h-80 flex-col">
          <VirtualList
            :key="searching ? 'search' : 'feed'"
            :stick="!searching"
            :items="feedLines"
            :item-key="(line: ChatLineData) => line.id"
            :estimate-size="40"
            :label="searching ? $t('dashboard.chat.search.label') : $t('dashboard.chat.feed.label')"
          >
            <template #default="{ item, index }">
              <ChatLine
                :line="item"
                :channel-id="channelId"
                :display="display"
                :index="index"
                :me="me"
                :focus="focusOf(item)"
                @ban="askBan"
                @focus="pickChatter"
              />
            </template>
          </VirtualList>
        </div>
        <template #footer>
          <template v-if="searching">
            <Button
              v-if="found.hasNextPage.value"
              data-search-more
              variant="outline"
              size="sm"
              :disabled="found.isFetchingNextPage.value"
              @click="found.fetchNextPage()"
            >
              {{ $t('dashboard.chat.search.more') }}
            </Button>
            <p v-else-if="searchDone" data-search-end class="text-sm text-muted-foreground">
              {{ $t('dashboard.chat.search.end') }}
            </p>
          </template>
          <ChatComposer v-else :channel-id="channelId" />
        </template>
      </PanelCard>
      <aside v-if="focusedUserId !== null" data-viewer-panel class="w-80 flex-none">
        <ViewerPanel
          :user-id="focusedUserId"
          :channel-id="channelId"
          :lines="visible"
          :hour12="display.timestamps === '12'"
          @close="closeViewer"
        />
      </aside>
      <aside v-if="displayOpen" data-display-panel class="w-80 flex-none">
        <ChatDisplayPanel v-model="display" />
      </aside>
    </div>
  </div>
  <ConfirmDialog
    :open="banOpen"
    :title="$t('dashboard.chat.ban.title', { name: banTarget?.displayName ?? '' })"
    :description="$t('dashboard.chat.ban.description', { name: banTarget?.displayName ?? '' })"
    :consequences="[$t('dashboard.chat.ban.consequences')]"
    :action="$t('dashboard.chat.ban.action')"
    :pending="banner.isPending.value"
    @update:open="closeBan"
    @confirm="confirmBan"
  />
</template>

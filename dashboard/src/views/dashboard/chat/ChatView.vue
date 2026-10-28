<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// The live chat of the open channel: the feed, the moderation on each line, and the send box.
// The hub keeps the feed cache current; this page only reads it. Ban asks once, here, and
// bans in this channel only (the all-channels ban is its own opt-in action elsewhere).
import { refDebounced, useElementSize, useEventListener } from '@vueuse/core';
import { computed, onMounted, ref, watch, watchEffect } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute, useRouter } from 'vue-router';
import ArrowUpRight from '~icons/nnz/arrow-up-right';
import Cross from '~icons/nnz/cross';
import SettingsFader from '~icons/nnz/settings-fader';
import Settings from '~icons/nnz/settings';
import UserCircle from '~icons/nnz/user-circle';

import { useChannelAccess } from '@/api/access';
import { useCurrentUser } from '@/api/auth';
import {
  chatSearchMinLength,
  useBanUser,
  useChatHistory,
  useChatSearch,
  useViewerChatHistory,
  type ChatLine as ChatLineData,
} from '@/api/chat';
import { useViewerChannel } from '@/api/viewer';
import { useViewerChatLive } from '@/realtime/useViewerChatLive';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import SearchField from '@/components/layout/SearchField.vue';
import SidePanel from '@/components/layout/SidePanel.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import { Button } from '@/components/ui/button';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePhone, useScreenFrom } from '@/composables/useLayout';
import { usePanelState } from '@/composables/usePanelState';

import { useChatDisplay } from './chatDisplay';
import ChatDockHeader from './components/ChatDockHeader.vue';
import ChatComposer from './components/ChatComposer.vue';
import ChatDisplayPanel from './components/ChatDisplayPanel.vue';
import ChatIdentityPanel from './components/ChatIdentityPanel.vue';
import ChatLine from './components/ChatLine.vue';
import RedemptionLine from './components/RedemptionLine.vue';
import ModToolsPanel from './components/ModToolsPanel.vue';
import ViewerPanel from './components/ViewerPanel.vue';

const { t } = useI18n();
const route = useRoute();
const { channelId: openChannelId, current } = useCurrentChannel();

// The viewer chat is this page for a participant: their own voice, no search, no moderation and no
// per-person history (chat-client.md 9a). Under the viewer frame the channel comes from its name in the address.
const viewerRoute = route.meta.viewerChat === true;
// Every moderation control needs the action behind it; a previewed viewer holds none, so none shows.
const access = useChannelAccess(openChannelId);
const viewerMode = computed<boolean>(
  () => viewerRoute || (access.known.value && !access.can('moderation:chat:settings:read')),
);
const channelName = computed(() =>
  String(route.params.channel ?? ''));
const namedChannel = viewerRoute && channelName.value !== ''
  ? useViewerChannel(channelName)
  : null;
const channelId = computed(() =>
  namedChannel
    ? namedChannel.data.value?.broadcasterId ?? null
    : openChannelId.value);

const history = viewerRoute
  ? useViewerChatHistory(channelId)
  : useChatHistory(channelId);
const banner = useBanUser(channelId);

if (viewerRoute) {
  useViewerChatLive(channelId);
}

// The chat in its own window, for a second monitor (owner 2026-10-14: "where is the chat popout?").
const router = useRouter();
const poppedOut = computed(() => route.meta.popout === true);

// The full address an OBS Custom Browser Dock opens.
const dockUrl = computed(() =>
  viewerRoute
    ? ''
    : location.origin + router.resolve({ name: 'popout-chat' }).href);

function popOut(): void {
  window.open(router.resolve({ name: 'popout-chat' }).href, 'nnz-chat', 'popup,width=440,height=760');
}

// A phone is narrow AND touch (the `phone:` variant); a narrow desktop window keeps the desktop page.
const phone = usePhone();

// The popout, the OBS dock and a phone share one slim header, so the feed keeps the height.
const docked = computed(() => poppedOut.value || phone.value);

// The docked header keeps the search field shut until the person asks for it; the desktop page always shows it.
const dockSearchOpen = ref(false);
const searchShown = computed(() => !docked.value || dockSearchOpen.value);

// Below md the frame lets the page grow, so a phone chat takes the screen height left under the frame header, the
// dock header (and the open search band) and above the bottom bar. The feed scrolls inside that, so the page does not.
const bottomBar = ref<HTMLElement | null>(null);

onMounted(() => {
  bottomBar.value = document.querySelector<HTMLElement>('[data-slot=bottom-bar],[data-slot=viewer-nav]');
});

const { height: bottomBarHeight } = useElementSize(bottomBar, { width: 0, height: 0 }, { box: 'border-box' });

// The viewer frame keeps 6rem of bottom padding for its fixed nav (pb-24) and 8.75rem above the feed (header, main
// padding, dock header, gap), so the body claims the padding the nav covers; the dashboard frame has 6rem of headers.
const viewerAbove: number = 8.75;
const dashboardAbove: number = 6;
const searchBand: number = 3.3;
const bodyStyle = computed<Record<string, string> | undefined>(() => {
  if (!phone.value || poppedOut.value)
    return undefined;

  const above = (viewerRoute
    ? viewerAbove
    : dashboardAbove) + (dockSearchOpen.value
    ? searchBand
    : 0);
  const height = `calc(100svh - ${above}rem - ${bottomBarHeight.value}px)`;

  const marginBottom = viewerRoute
    ? `calc(${bottomBarHeight.value}px - 6rem)`
    : '0px';

  return {
    height,
    flex: 'none',
    marginBottom,
  };
});

// Closing the docked search clears what was typed.
watch(dockSearchOpen, (open) => {
  if (!open) {
    search.value = '';
  }
});

const display = useChatDisplay();
const displayOpen = ref(false);
const modToolsOpen = ref(false);
const identityOpen = ref(false);
const focusedUserId = ref<string | null>(null);
const currentUser = useCurrentUser();
const me = computed<string | null>(() => currentUser.data.value?.username ?? null);
const banTarget = ref<ChatLineData | null>(null);
const banFailed = ref(false);
const replyTo = ref<ChatLineData | null>(null);

const lines = computed<ChatLineData[]>(() => history.data.value ?? []);
const linesById = computed(() =>
  new Map(lines.value.map((line) =>
    [line.id, line])));

// The names "@" offers in the composer: everyone in the loaded chat, the latest speaker first.
const chatters = computed<string[]>(() =>
  [...new Set([...lines.value].reverse().map((line) =>
    line.displayName))]);

function parentOf(line: ChatLineData): ChatLineData | null {
  return line.replyToMessageId
    ? linesById.value.get(line.replyToMessageId) ?? null
    : null;
}

const visible = computed<ChatLineData[]>(() =>
  lines.value.filter(
    (line) =>
      !(!display.value.commands && line.isCommand) &&
      !(display.value.deleted === 'hide' && line.removed === 'deleted') &&
      !(viewerRoute && line.removed !== undefined),
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
  lines.value.filter((line) =>
    line.userId === focusedUserId.value).at(-1)?.displayName ?? '');

function closeViewer(): void {
  focusedUserId.value = null;
}

// The viewer panel is open while a chatter is in focus; closing the drawer ends the focus.
const viewerOpen = computed<boolean>({
  get: () =>
    focusedUserId.value !== null,
  set: (open) => {
    if (!open) {
      closeViewer();
    }
  },
});

// Side panels never squeeze the chat: two fit beside it on a wide screen, else one. Opening one more closes
// the panel opened longest ago (owner 2026-10-14: "maybe only allow both if there is enough space?").
type ChatPanel = 'viewer' | 'display' | 'modTools' | 'identity';

const twoPanelsFit = useScreenFrom('twoPanels');
const openOrder = ref<ChatPanel[]>([]);
const closers = {
  viewer: closeViewer,
  display: () => {
    displayOpen.value = false;
  },
  modTools: () => {
    modToolsOpen.value = false;
  },
  identity: () => {
    identityOpen.value = false;
  },
} as const satisfies Record<ChatPanel, () => void>;

function track(panel: ChatPanel, open: boolean): void {
  openOrder.value = openOrder.value.filter((other) =>
    other !== panel);

  if (!open) return;

  openOrder.value.push(panel);
  makeRoom();
}

function makeRoom(): void {
  const room = twoPanelsFit.value
    ? 2
    : 1;

  while (openOrder.value.length > room) {
    const oldest = openOrder.value.shift() as ChatPanel;

    closers[oldest]();
  }
}

// A window made narrower with two panels open closes the older one too.
watch(twoPanelsFit, makeRoom);

watch(viewerOpen, (open) =>
  track('viewer', open));

watch(displayOpen, (open) =>
  track('display', open));

watch(modToolsOpen, (open) =>
  track('modTools', open));

watch(identityOpen, (open) =>
  track('identity', open));

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
  <PageHeader v-if="!docked" :title="$t('dashboard.chat.title')" :crumb="$t('dashboard.chat.crumb')">
    <template #status>
      <StatusBadge
        :tone="live ? 'live' : 'off'"
        :label="live ? $t('dashboard.chat.status.live') : $t('dashboard.chat.status.offline')"
      />
    </template>
    <template v-if="!docked" #bar>
      <!-- A phone has no second window to dock or pop out into; its toggles keep only their icons. -->
      <div data-chat-controls class="ml-auto flex min-w-0 items-center gap-2 phone:ml-0 phone:flex-1">
        <SearchField
          v-if="!viewerMode"
          v-model="search"
          class="w-64 phone:w-auto phone:min-w-0 phone:flex-1"
          :placeholder="$t('dashboard.chat.search.placeholder')"
        />
        <Button
          v-if="!viewerMode"
          data-mod-tools-toggle
          variant="outline"
          size="sm"
          :aria-pressed="modToolsOpen"
          :aria-label="$t('dashboard.chat.modTools.title')"
          @click="modToolsOpen = !modToolsOpen"
        >
          <Settings class="size-4" aria-hidden="true" />
          <span class="phone:sr-only">{{ $t('dashboard.chat.modTools.title') }}</span>
        </Button>
        <Button
          v-if="!viewerMode"
          data-identity-toggle
          variant="outline"
          size="sm"
          :aria-pressed="identityOpen"
          :aria-label="$t('dashboard.chat.identity.title')"
          @click="identityOpen = !identityOpen"
        >
          <UserCircle class="size-4" aria-hidden="true" />
          <span class="phone:sr-only">{{ $t('dashboard.chat.identity.title') }}</span>
        </Button>
        <Button
          data-display-toggle
          variant="outline"
          size="sm"
          :aria-pressed="displayOpen"
          :aria-label="$t('dashboard.chat.display.toggle')"
          @click="displayOpen = !displayOpen"
        >
          <SettingsFader class="size-4" aria-hidden="true" />
          <span class="phone:sr-only">{{ $t('dashboard.chat.display.toggle') }}</span>
        </Button>
        <CopyLinkButton
          v-if="!poppedOut && !viewerMode"
          class="phone:hidden"
          :url="dockUrl"
          :label="$t('dashboard.chat.copyDockLink')"
        />
        <Button v-if="!viewerMode"
                data-chat-popout
                class="phone:hidden"
                variant="outline"
                size="sm"
                @click="popOut"
        >
          <ArrowUpRight class="size-4" aria-hidden="true" />
          {{ $t('dashboard.chat.popout') }}
        </Button>
      </div>
    </template>
  </PageHeader>
  <ChatDockHeader
    v-else
    v-model:search-open="dockSearchOpen"
    v-model:mod-tools-open="modToolsOpen"
    v-model:identity-open="identityOpen"
    v-model:display-open="displayOpen"
    :title="$t('dashboard.chat.title')"
    :live="live"
    :status-label="live ? $t('dashboard.chat.status.live') : $t('dashboard.chat.status.offline')"
    :count-label="feedPanel.description"
    :clear-focus-label="focusedUserId === null ? null : $t('dashboard.chat.viewer.clearFocus', { name: focusedName })"
    :show-frame-actions="!poppedOut"
    :viewer="viewerMode"
    @clear-focus="focusedUserId = null"
  />
  <!-- Docked: the search field opens under the header, so the feed keeps the rest of the height. -->
  <div v-if="docked && searchShown" class="flex-none border-b border-border p-2">
    <SearchField v-model="search" :placeholder="$t('dashboard.chat.search.placeholder')" />
  </div>
  <div
    data-page-body
    :style="bodyStyle"
    class="flex min-h-0 flex-1 flex-col"
    :class="docked ? 'gap-0 p-0' : 'gap-3 p-6 max-md:py-0'"
  >
    <p v-if="banFailed" role="alert" class="text-sm text-destructive max-md:px-4 max-md:pt-3">
      {{ $t('dashboard.chat.ban.failed') }}
    </p>
    <div class="flex min-h-0 flex-1 gap-3">
      <PanelCard
        :class="docked ? 'min-h-0 flex-1' : 'min-h-96 flex-1'"
        tier="lead"
        :headless="docked"
        :flush="docked"
        v-bind="feedPanel"
        @retry="retryFeed"
      >
        <template v-if="focusedUserId !== null && !docked" #actions>
          <Button data-focus-clear variant="ghost" size="sm" @click="focusedUserId = null">
            <Cross class="size-4" aria-hidden="true" />
            {{ $t('dashboard.chat.viewer.clearFocus', { name: focusedName }) }}
          </Button>
        </template>
        <div class="flex h-full min-h-80 flex-col">
          <VirtualList
            :key="searching ? 'search' : 'feed'"
            :stick="!searching"
            :pause-on-hover="poppedOut"
            :items="feedLines"
            :item-key="(line: ChatLineData) => line.id"
            :estimate-size="40"
            :label="searching ? $t('dashboard.chat.search.label') : $t('dashboard.chat.feed.label')"
          >
            <template #default="{ item, index }">
              <RedemptionLine
                v-if="item.redemption"
                :line="item"
                :display="display"
                :me="me"
                :compact="poppedOut"
              />
              <ChatLine
                v-else
                :line="item"
                :channel-id="channelId"
                :display="display"
                :compact="poppedOut"
                :index="index"
                :me="me"
                :channel-login="current?.login ?? null"
                :focus="focusOf(item)"
                :parent="parentOf(item)"
                :viewer="viewerMode"
                @ban="askBan"
                @focus="pickChatter"
                @reply="replyTo = $event"
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
          <ChatComposer
            v-else
            v-model:reply-to="replyTo"
            :channel-id="channelId"
            :chatters="chatters"
            :viewer="viewerMode"
          />
        </template>
      </PanelCard>
      <SidePanel
        v-if="!viewerMode"
        v-model:open="viewerOpen"
        data-viewer-panel
        :label="focusedName"
      >
        <ViewerPanel
          v-if="focusedUserId !== null"
          :user-id="focusedUserId"
          :channel-id="channelId"
          :channel-login="current?.login ?? null"
          :lines="visible"
          :hour12="display.timestamps === '12'"
        />
      </SidePanel>
      <SidePanel v-model:open="displayOpen" see-through data-display-panel :label="$t('dashboard.chat.display.title')">
        <ChatDisplayPanel v-model="display" />
      </SidePanel>
      <SidePanel v-if="!viewerMode" v-model:open="modToolsOpen" data-mod-tools-panel :label="$t('dashboard.chat.modTools.title')">
        <ModToolsPanel :channel-id="channelId" :channel-name="current?.login ?? ''" />
      </SidePanel>
      <SidePanel v-if="!viewerMode" v-model:open="identityOpen" data-identity-panel :label="$t('dashboard.chat.identity.title')">
        <ChatIdentityPanel />
      </SidePanel>
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

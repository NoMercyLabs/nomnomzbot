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
import { useLocalStorage } from '@vueuse/core';
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import SettingsFader from '~icons/nnz/settings-fader';

import { useCurrentUser } from '@/api/auth';
import { useBanUser, useChatHistory, type ChatLine as ChatLineData } from '@/api/chat';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import StatusBadge from '@/components/layout/StatusBadge.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import { Button } from '@/components/ui/button';
import { Label } from '@/components/ui/label';
import { Switch } from '@/components/ui/switch';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePanelState } from '@/composables/usePanelState';

import { useChatDisplay } from './chatDisplay';
import ChatComposer from './components/ChatComposer.vue';
import ChatDisplayPanel from './components/ChatDisplayPanel.vue';
import ChatLine from './components/ChatLine.vue';

const { t } = useI18n();
const { channelId, current } = useCurrentChannel();

const history = useChatHistory(channelId);
const banner = useBanUser(channelId);

const hideCommands = useLocalStorage<boolean>('nnz.chat.hideCommands', false);
const display = useChatDisplay();
const displayOpen = ref(false);
const currentUser = useCurrentUser();
const me = computed<string | null>(() => currentUser.data.value?.username ?? null);
const banTarget = ref<ChatLineData | null>(null);
const banFailed = ref(false);

const lines = computed<ChatLineData[]>(() => history.data.value ?? []);
const visible = computed<ChatLineData[]>(() =>
  lines.value.filter(
    (line: ChatLineData) =>
      !(hideCommands.value && line.isCommand) &&
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

const banOpen = computed(() => banTarget.value !== null);

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
  </PageHeader>
  <div class="flex min-h-0 flex-1 flex-col gap-3 p-6">
    <p v-if="banFailed" role="alert" class="text-sm text-destructive">
      {{ $t('dashboard.chat.ban.failed') }}
    </p>
    <div class="flex min-h-0 flex-1 gap-3">
      <PanelCard
        class="min-h-96 flex-1"
        tier="lead"
        :title="$t('dashboard.chat.feed.title')"
        :description="$t('dashboard.chat.feed.count', { count: visible.length })"
        :state="panelState"
        :empty-text="emptyText"
        :error-text="$t('dashboard.chat.feed.error')"
        @retry="history.refetch()"
      >
        <template #actions>
          <div class="flex items-center gap-2">
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
            <Switch id="chat-hide-commands" v-model="hideCommands" />
            <Label for="chat-hide-commands" class="text-sm font-normal">
              {{ $t('dashboard.chat.hideCommands') }}
            </Label>
          </div>
        </template>
        <div class="flex h-full min-h-80 flex-col">
          <VirtualList
            stick
            :items="visible"
            :item-key="(line: ChatLineData) => line.id"
            :estimate-size="40"
            :label="$t('dashboard.chat.feed.label')"
          >
            <template #default="{ item, index }">
              <ChatLine
                :line="item"
                :channel-id="channelId"
                :display="display"
                :index="index"
                :me="me"
                @ban="askBan"
              />
            </template>
          </VirtualList>
        </div>
        <template #footer>
          <ChatComposer :channel-id="channelId" />
        </template>
      </PanelCard>
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

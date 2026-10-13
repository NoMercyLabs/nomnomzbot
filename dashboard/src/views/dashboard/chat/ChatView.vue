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
import { useLocalStorage, type RemovableRef } from '@vueuse/core'
import { computed, ref, type ComputedRef, type Ref } from 'vue'
import { useI18n } from 'vue-i18n'

import { useBanUser, useChatHistory, type ChatLine as ChatLineData } from '@/api/chat'
import PageHeader from '@/components/layout/PageHeader.vue'
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue'
import StatusBadge from '@/components/layout/StatusBadge.vue'
import VirtualList from '@/components/layout/VirtualList.vue'
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue'
import { Label } from '@/components/ui/label'
import { Toaster } from '@/components/ui/sonner'
import { Switch } from '@/components/ui/switch'
import { TooltipProvider } from '@/components/ui/tooltip'
import { useCurrentChannel } from '@/composables/useCurrentChannel'
import { usePanelState } from '@/composables/usePanelState'

import ChatComposer from './components/ChatComposer.vue'
import ChatLine from './components/ChatLine.vue'

const { t } = useI18n()
const { channelId, current } = useCurrentChannel()

const history = useChatHistory(channelId)
const banner = useBanUser(channelId)

const hideCommands: RemovableRef<boolean> = useLocalStorage('nnz.chat.hideCommands', false)
const banTarget: Ref<ChatLineData | null> = ref(null)
const banFailed: Ref<boolean> = ref(false)

const lines: ComputedRef<ChatLineData[]> = computed(() => history.data.value ?? [])
const visible: ComputedRef<ChatLineData[]> = computed(() =>
  hideCommands.value ? lines.value.filter((line: ChatLineData) => !line.isCommand) : lines.value,
)

const panelState: ComputedRef<PanelState> = usePanelState(history, () => visible.value.length === 0)
const emptyText: ComputedRef<string> = computed(() =>
  lines.value.length > 0 ? t('dashboard.chat.feed.emptyCommands') : t('dashboard.chat.feed.empty'),
)
const live: ComputedRef<boolean> = computed(() => current.value?.isLive === true)

const banOpen: ComputedRef<boolean> = computed(() => banTarget.value !== null)

function askBan(line: ChatLineData): void {
  banFailed.value = false
  banTarget.value = line
}

function closeBan(open: boolean): void {
  if (!open) banTarget.value = null
}

async function confirmBan(): Promise<void> {
  const target: ChatLineData | null = banTarget.value
  if (!target) return
  try {
    await banner.mutateAsync({ targetUserId: target.userId, scope: 'this_channel' })
    banTarget.value = null
  } catch {
    banTarget.value = null
    banFailed.value = true
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
          <Switch id="chat-hide-commands" v-model="hideCommands" />
          <Label for="chat-hide-commands" class="text-sm font-normal">
            {{ $t('dashboard.chat.hideCommands') }}
          </Label>
        </div>
      </template>
      <TooltipProvider :delay-duration="300">
        <div class="flex h-full min-h-80 flex-col">
          <VirtualList
            stick
            :items="visible"
            :item-key="(line: ChatLineData) => line.id"
            :estimate-size="40"
            :label="$t('dashboard.chat.feed.label')"
          >
            <template #default="{ item }">
              <ChatLine :line="item" :channel-id="channelId" @ban="askBan" />
            </template>
          </VirtualList>
        </div>
      </TooltipProvider>
      <template #footer>
        <ChatComposer :channel-id="channelId" />
      </template>
    </PanelCard>
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
  <Toaster />
</template>

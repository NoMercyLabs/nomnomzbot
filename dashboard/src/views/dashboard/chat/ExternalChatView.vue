<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Viewer chat for a Twitch channel this deployment does not serve (chat-client.md 9a "Any Twitch channel").
// The channel is named by its login in the address and found by its Twitch id in `?id=`. It reads the live
// lines the hub pushes on the viewer's own token and sends on that token. There is no history and no
// moderation here. Send is the one primary action; a missing Twitch permission shows the grant prompt.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';

import { useCurrentUser } from '@/api/auth';
import { chatMessageMaxLength, type ChatLine as ChatLineData } from '@/api/chat';
import { useStartScopeRegrant } from '@/api/chatIdentity';
import { deviceApprovalQuery, signInUrl } from '@/api/signIn';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { readScope, useExternalChat } from '@/realtime/useExternalChat';

import { useChatDisplay } from './chatDisplay';
import ChatLine from './components/ChatLine.vue';

const route = useRoute();
const login = computed(() =>
  String(route.params.channel ?? ''));
const twitchId = String(route.query.id ?? '');

const chat = useExternalChat(twitchId);
const display = useChatDisplay();
const currentUser = useCurrentUser();
const me = computed<string | null>(() => currentUser.data.value?.username ?? null);

const text = ref('');
const sending = ref(false);
const canSend = computed(() =>
  text.value.trim() !== '' && !sending.value);

async function submit(): Promise<void> {
  if (!canSend.value) return;

  sending.value = true;

  const sent = await chat.send(text.value.trim());

  sending.value = false;

  if (sent) {
    text.value = '';
  }
}

// Reading needs user:read:chat, which a sign-in grants: one click re-consents as a moderator and returns here.
// Nothing is signed out. Sending (user:write:chat) uses the device re-grant below.
const needsReadScope = computed(() =>
  chat.missingScope.value === readScope);

function allowReading(): void {
  window.location.assign(signInUrl('twitch', route.fullPath, 'moderator'));
}

// The same additive re-consent the chat identity panel uses: nothing is signed out.
const regrant = useStartScopeRegrant();
const regrantCode = computed(() => regrant.data.value ?? null);
const approval = useQuery(computed(() => deviceApprovalQuery('twitch', regrantCode.value)));
const approved = computed(() => approval.data.value?.status === 'authorized');
</script>

<template>
  <PageHeader :title="login" :crumb="$t('dashboard.chat.external.crumb')" />
  <div data-page-body class="flex min-h-0 flex-1 flex-col gap-3 p-6 max-md:py-0">
    <p v-if="chat.failure.value && !chat.missingScope.value" role="alert" class="text-sm text-destructive">
      {{ $t('dashboard.chat.external.failed') }}
    </p>
    <PanelCard
      class="min-h-96 flex-1"
      tier="lead"
      :title="login"
      :description="$t('dashboard.chat.external.note')"
      :state="chat.lines.value.length === 0 ? 'empty' : 'ready'"
      :empty-text="$t('dashboard.chat.external.empty')"
    >
      <div class="flex h-full min-h-80 flex-col">
        <VirtualList
          stick
          :items="chat.lines.value"
          :item-key="(line: ChatLineData) => line.id"
          :estimate-size="40"
          :label="$t('dashboard.chat.feed.label')"
        >
          <template #default="{ item, index }">
            <ChatLine
              :line="item"
              :channel-id="twitchId"
              :display="display"
              :index="index"
              :me="me"
              :channel-login="login"
              viewer
            />
          </template>
        </VirtualList>
      </div>
      <template #footer>
        <div v-if="chat.missingScope.value" class="flex flex-col gap-2 rounded-md border p-3" data-test="scope">
          <p class="text-sm">
            {{ $t(needsReadScope ? 'dashboard.chat.external.needsReadScope' : 'dashboard.chat.external.needsScope', { scope: chat.missingScope.value }) }}
          </p>
          <Button v-if="needsReadScope" class="self-start" data-test="allow-read" @click="allowReading">
            {{ $t('dashboard.chat.external.allowReading') }}
          </Button>
          <p v-if="!needsReadScope && regrantCode && !approved" class="text-sm" data-test="regrant-code">
            {{ $t('dashboard.chat.identity.enterCode', { code: regrantCode.userCode }) }}
            <a :href="regrantCode.verificationUri" target="_blank" rel="noopener" class="underline">
              {{ regrantCode.verificationUri }}
            </a>
          </p>
          <p v-if="!needsReadScope && approved" class="text-sm">{{ $t('dashboard.chat.identity.approved') }}</p>
          <Button
            v-if="!needsReadScope && !regrantCode"
            class="self-start"
            :disabled="regrant.isPending.value"
            data-test="regrant"
            @click="regrant.mutate()"
          >
            {{ $t('dashboard.chat.identity.regrant') }}
          </Button>
        </div>
        <form class="flex items-center gap-2" @submit.prevent="submit">
          <Input
            v-model="text"
            data-external-chat-input
            :maxlength="chatMessageMaxLength"
            :placeholder="$t('dashboard.chat.external.placeholder', { name: login })"
            :aria-label="$t('dashboard.chat.external.placeholder', { name: login })"
          />
          <Button type="submit" :disabled="!canSend">
            {{ $t('dashboard.chat.external.send') }}
          </Button>
        </form>
      </template>
    </PanelCard>
  </div>
</template>

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// Viewer chat for a Twitch channel this deployment does not serve (chat-client.md 9a "Any Twitch channel").
// The channel is named by its login in the address and found by its Twitch id in `?id=`. It reads the live
// lines the hub pushes on the viewer's own token and sends on that token. There is no history and no
// moderation here. Send is the one primary action; a missing Twitch permission shows the grant prompt.
import { computed, ref } from 'vue';
import { useRoute } from 'vue-router';

import { useCurrentUser } from '@/api/auth';
import { chatMessageMaxLength, type ChatLine as ChatLineData } from '@/api/chat';
import { signInUrl } from '@/api/signIn';
import InlineNotice from '@/components/layout/InlineNotice.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import TwitchRegrantDialog from '@/components/layout/TwitchRegrantDialog.vue';
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
const guiding = ref(false);
const sendScopes = computed(() =>
  chat.missingScope.value
    ? [{
        scope: chat.missingScope.value,
        features: ['chat_send'],
        detectedAtRuntime: false,
        chatNotified: false,
      }]
    : []);
</script>

<template>
  <PageHeader :title="login" :crumb="$t('dashboard.chat.external.crumb')" />
  <div data-page-body class="flex min-h-0 flex-1 flex-col gap-3 p-6 max-md:py-0">
    <InlineNotice v-if="chat.failure.value && !chat.missingScope.value" tone="error" compact>
      {{ $t('dashboard.chat.external.failed') }}
    </InlineNotice>
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
          <Button v-if="!needsReadScope" class="self-start" data-test="regrant" @click="guiding = true">
            {{ $t('dashboard.chat.identity.regrant') }}
          </Button>
        </div>
        <!-- Outside the scope block: on approval the block goes, and the dialog stays to say so. -->
        <TwitchRegrantDialog
          v-model:open="guiding"
          :scopes="sendScopes"
          @approved="chat.missingScope.value = null"
        />
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

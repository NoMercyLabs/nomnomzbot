<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The channel's own bot account. Connected, the channel speaks through that account; not connected, it speaks
// through the shared bot. The status is live (the bot refreshes it when the channel settings change); the page
// connects with a device code or a browser sign-in, and disconnects after a confirm.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';

import { ApiError } from '@/api/errors';
import { useBotStatus } from '@/api/home';
import { useDisconnectBot, useStartBotBrowserLogin } from '@/api/whiteLabelBot';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import PageBody from '@/components/layout/PageBody.vue';
import PageHeader from '@/components/layout/PageHeader.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import type { PanelState } from '@/composables/usePanelState';
import RowGroup from '@/components/layout/RowGroup.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useCan } from '@/composables/useCan';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

import ReadabilityRow from '../components/ReadabilityRow.vue';
import BotLoginPanel from './components/BotLoginPanel.vue';

const { t } = useI18n();
const { channelId } = useCurrentChannel();
const { can } = useCan();
const status = useBotStatus(channelId);
const disconnect = useDisconnectBot(channelId);
const browserLogin = useStartBotBrowserLogin(channelId);

const loggingIn = ref(false);
const confirming = ref(false);

const bot = computed(() => status.data.value ?? null);
const usesSharedBot = computed(() => bot.value?.usesSharedBot === true);
// "Connected" means the channel's OWN bot account; the shared system bot is never one.
const connected = computed(() => bot.value?.connected === true && !usesSharedBot.value);
const canLogIn = computed(() => can('channelbot:connect') && can('integration:write'));
const name = computed(() => bot.value?.displayName ?? bot.value?.login ?? '');

const sharedTitle = computed(() =>
  bot.value?.login
    ? t('dashboard.settings.whiteLabelBot.shared.titleNamed', { login: bot.value.login })
    : t('dashboard.settings.whiteLabelBot.shared.title'));

const state = computed<PanelState>(() => {
  if (status.isError.value)
    return 'error';

  return status.isPending.value
    ? 'loading'
    : 'ready';
});

function failureText(error: Error, fallbackKey: string): string {
  return error instanceof ApiError
    ? t(error.messageKey)
    : t(fallbackKey);
}

function onConnected(): void {
  loggingIn.value = false;
  toast.success(t('dashboard.settings.whiteLabelBot.connectedToast'));
}

function confirmDisconnect(): void {
  disconnect.mutate(undefined, {
    onSuccess: () => {
      confirming.value = false;
      toast.success(t('dashboard.settings.whiteLabelBot.disconnectedToast'));
    },
    onError: (error: Error) =>
      toast.error(failureText(error, 'dashboard.settings.whiteLabelBot.disconnectFailed')),
  });
}

function openBrowserLogin(): void {
  browserLogin.mutate(undefined, {
    onSuccess: (start) => {
      window.open(start.authorizeUrl, '_blank', 'noopener');
    },
    onError: (error: Error) =>
      toast.error(failureText(error, 'dashboard.settings.whiteLabelBot.browserFailed')),
  });
}
</script>

<template>
  <PageHeader :title="$t('frame.nav.settingsPages.whiteLabelBot')" :crumb="$t('dashboard.settings.crumb')" />

  <PageBody>
    <PanelCard
      data-view="white-label-bot"
      pad
      :title="$t('dashboard.settings.whiteLabelBot.title')"
      :description="$t('dashboard.settings.whiteLabelBot.description')"
      :state="state"
      :error-text="$t('dashboard.settings.whiteLabelBot.loadFailed')"
      @retry="status.refetch()"
    >
      <RowGroup>
        <div class="flex min-h-11 flex-wrap items-center gap-x-4 gap-y-2 px-3 py-2" data-slot="bot-identity">
          <Avatar class="size-10">
            <AvatarImage v-if="bot?.profileImageUrl" :src="bot.profileImageUrl" :alt="name" />
            <AvatarFallback>{{ name.slice(0, 2).toUpperCase() }}</AvatarFallback>
          </Avatar>
          <div class="min-w-0 flex-1 basis-56">
            <template v-if="connected">
              <span class="block text-sm font-medium">{{ name }}</span>
              <span v-if="bot?.login" class="block text-sm text-muted-foreground">{{ $t('dashboard.settings.whiteLabelBot.handle', { login: bot.login }) }}</span>
            </template>
            <template v-else>
              <span class="block text-sm font-medium">{{ sharedTitle }}</span>
              <span class="block text-sm text-muted-foreground">{{ $t('dashboard.settings.whiteLabelBot.shared.description') }}</span>
            </template>
          </div>
          <Badge :variant="connected ? 'default' : 'secondary'">
            {{ connected ? $t('dashboard.settings.whiteLabelBot.connected') : $t('dashboard.settings.whiteLabelBot.notConnected') }}
          </Badge>
        </div>
        <BotLoginPanel
          v-if="loggingIn && canLogIn"
          @cancel="loggingIn = false"
          @connected="onConnected"
        />
        <ReadabilityRow
          v-else-if="connected && canLogIn"
          :title="$t('dashboard.settings.whiteLabelBot.replace.title')"
          :description="$t('dashboard.settings.whiteLabelBot.replace.description')"
        >
          <Button variant="outline" size="sm" @click="loggingIn = true">
            {{ $t('dashboard.settings.whiteLabelBot.replace.action') }}
          </Button>
        </ReadabilityRow>
        <ReadabilityRow
          v-else-if="canLogIn"
          :title="$t('dashboard.settings.whiteLabelBot.connect.title')"
          :description="$t('dashboard.settings.whiteLabelBot.connect.description')"
        >
          <Button size="sm" @click="loggingIn = true">
            {{ $t('dashboard.settings.whiteLabelBot.connect.action') }}
          </Button>
        </ReadabilityRow>
        <ReadabilityRow
          v-if="!loggingIn && can('channelbot:connect')"
          :title="$t('dashboard.settings.whiteLabelBot.browser.title')"
          :description="$t('dashboard.settings.whiteLabelBot.browser.description')"
        >
          <Button
            variant="outline"
            size="sm"
            :disabled="browserLogin.isPending.value"
            @click="openBrowserLogin"
          >
            {{ $t('dashboard.settings.whiteLabelBot.browser.action') }}
          </Button>
        </ReadabilityRow>
        <ReadabilityRow
          v-if="connected && !loggingIn && can('channelbot:disconnect')"
          :title="$t('dashboard.settings.whiteLabelBot.disconnect.title')"
          :description="$t('dashboard.settings.whiteLabelBot.disconnect.description')"
        >
          <Button variant="outline" size="sm" @click="confirming = true">
            {{ $t('dashboard.settings.whiteLabelBot.disconnect.action') }}
          </Button>
        </ReadabilityRow>
      </RowGroup>
    </PanelCard>
    <ConfirmDialog
      v-model:open="confirming"
      :title="$t('dashboard.settings.whiteLabelBot.disconnect.confirmTitle', { name })"
      :description="$t('dashboard.settings.whiteLabelBot.disconnect.confirmDescription')"
      :consequences="[
        $t('dashboard.settings.whiteLabelBot.disconnect.consequenceShared'),
        $t('dashboard.settings.whiteLabelBot.disconnect.consequenceAccount'),
      ]"
      :action="$t('dashboard.settings.whiteLabelBot.disconnect.action')"
      :pending="disconnect.isPending.value"
      @confirm="confirmDisconnect"
    />
  </PageBody>
</template>

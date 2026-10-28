<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
import { useQuery } from '@tanstack/vue-query';
import { useIntervalFn } from '@vueuse/core';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRoute } from 'vue-router';

import { activeBot } from '@/api/activeBot';
import { providersQuery, RateLimitedError, signInUrl, type LoginProvider, type LoginRole } from '@/api/signIn';
import AppMark from '@/components/layout/AppMark.vue';
import LanguagePicker from '@/components/layout/LanguagePicker.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import RowGroup from '@/components/layout/RowGroup.vue';
import SummaryRow from '@/components/layout/SummaryRow.vue';
import { Button } from '@/components/ui/button';
import { officialOrigin, servedOrigin } from '@/lib/botAddress';
import { returnPath } from '@/router/guard';

import BotChooser from './components/BotChooser.vue';
import DeviceCodePanel from './components/DeviceCodePanel.vue';

const route = useRoute();
const { t, locale } = useI18n();
const providers = useQuery(providersQuery);
const mode = ref<'options' | 'device' | 'bots'>('options');
const botHost = computed(() => new URL(activeBot.value).host);
const botDetail = computed<string>(() => {
  if (activeBot.value === officialOrigin)
    return 'site.login.official';

  return activeBot.value === servedOrigin
    ? 'site.login.thisBot'
    : 'site.login.used';
});
// A browser sign-in comes back to the bot's own site, so for another bot this page uses a code.
// Platforms return only to https or localhost (a secure context), so a plain http network address uses one too.
const otherBot = computed(() => activeBot.value !== servedOrigin);
const codeOnly = computed(() => otherBot.value || !window.isSecureContext);

// A 429 is "not now", so the page counts the wait down and enables the retry at 0.
const waitSeconds = ref(0);
const { pause, resume } = useIntervalFn(
  () => {
    waitSeconds.value = Math.max(waitSeconds.value - 1, 0);

    if (waitSeconds.value === 0) {
      pause();
    }
  },
  1000,
  { immediate: false },
);

watch(
  () => providers.error.value,
  (error) => {
    if (!(error instanceof RateLimitedError)) return;

    waitSeconds.value = error.retryAfterSeconds;
    resume();
  },
  { immediate: true },
);

const problemLine = computed<string>(() => {
  if (providers.error.value instanceof RateLimitedError)
    return t('site.login.rateLimited', { seconds: waitSeconds.value }, waitSeconds.value);

  return t(providers.isError.value
    ? 'site.login.unreachable'
    : 'site.login.noProviders');
});

const returnTo = computed(() => returnPath(route.query.return_to));
const enabled = computed<LoginProvider[]>(() =>
  (providers.data.value ?? []).filter((p) => p.enabled),
);
const switchedOff = computed<string[]>(() =>
  (providers.data.value ?? []).filter((p) => !p.enabled).map((p) => p.displayName),
);
const switchedOffLine = computed<string>(() =>
  t(
    'site.login.switchedOff',
    { providers: new Intl.ListFormat(locale.value).format(switchedOff.value) },
    switchedOff.value.length,
  ),
);
// A code is a second way to sign in, not a second provider: one link for all of them, and the
// provider question comes only when more than one offers a code.
const codeProviders = computed(() => enabled.value.filter((p) => p.flows.includes('device_code')));
const deviceProvider = ref<LoginProvider | null>(null);

function useCode(): void {
  deviceProvider.value = codeProviders.value.length === 1
    ? codeProviders.value[0]!
    : null;

  mode.value = 'device';
}

const shown = computed(() =>
  codeOnly.value
    ? codeProviders.value
    : enabled.value);

// Twitch offers three roles; each asks Twitch for only the scopes that role uses.
const roles: LoginRole[] = ['broadcaster', 'moderator', 'viewer'];

function hasRoles(provider: LoginProvider): boolean {
  return provider.key === 'twitch' && !codeOnly.value;
}

function signInAs(provider: LoginProvider, role: LoginRole): void {
  window.location.assign(signInUrl(provider.key, returnTo.value, role));
}

function signIn(provider: LoginProvider): void {
  if (!codeOnly.value) {
    window.location.assign(signInUrl(provider.key, returnTo.value));

    return;
  }

  deviceProvider.value = provider;
  mode.value = 'device';
}
</script>

<template>
  <!-- The shadcn sign-in block: the brand, then the card, then the terms under it. -->
  <main class="relative flex min-h-svh flex-col items-center justify-center gap-6 p-6">
    <div class="absolute top-4 right-4"><LanguagePicker /></div>
    <h1 class="flex items-center gap-2 text-base font-semibold">
      <AppMark />
      {{ $t('site.login.brand') }}
    </h1>
    <BotChooser v-if="mode === 'bots'" class="w-full max-w-100" @back="mode = 'options'" />
    <PanelCard
      v-else
      :title="$t('site.login.title')"
      :description="$t('site.login.subtitle')"
      tier="lead"
      pad
      class="w-full max-w-100"
    >
      <div class="flex flex-col gap-4">
        <RowGroup>
          <SummaryRow :title="botHost" :detail="$t(botDetail)">
            <template #end>
              <Button v-if="mode === 'options'" variant="outline" size="sm" @click="mode = 'bots'">
                {{ $t('site.login.change') }}
              </Button>
            </template>
          </SummaryRow>
        </RowGroup>

        <DeviceCodePanel
          v-if="mode === 'device' && deviceProvider"
          :provider="deviceProvider"
          :return-to="returnTo"
          @cancel="mode = 'options'"
        />
        <div v-else-if="mode === 'device'" class="flex flex-col gap-2">
          <p class="text-sm">{{ $t('site.login.codeChoose') }}</p>
          <Button
            v-for="provider in codeProviders"
            :key="provider.key"
            variant="outline"
            class="w-full"
            @click="deviceProvider = provider"
          >
            <PlatformLogo :platform="provider.key" />
            {{ $t('site.login.codeWith', { provider: provider.displayName }) }}
          </Button>
          <Button variant="ghost" class="w-full" @click="mode = 'options'">{{
            $t('site.login.device.back')
          }}</Button>
        </div>
        <p
          v-else-if="providers.isPending.value"
          class="text-sm text-muted-foreground"
          role="status"
        >
          {{ $t('site.login.loading') }}
        </p>
        <div
          v-else-if="providers.isError.value || shown.length === 0"
          class="flex flex-col gap-3"
          role="alert"
        >
          <p class="text-sm">{{ problemLine }}</p>
          <Button
            variant="outline"
            class="w-full"
            :disabled="waitSeconds > 0"
            @click="providers.refetch()"
          >{{
            $t('site.login.retry')
          }}</Button>
        </div>
        <div v-else class="flex flex-col gap-2">
          <p v-if="codeOnly" class="text-sm text-muted-foreground">
            {{
              otherBot
                ? $t('site.login.codeOnly', { host: botHost })
                : $t('site.login.codeInsecure')
            }}
          </p>
          <template v-for="provider in shown" :key="provider.key">
            <template v-if="hasRoles(provider)">
              <Button
                v-for="role in roles"
                :key="role"
                :variant="role === 'broadcaster' ? 'default' : 'outline'"
                class="w-full"
                @click="signInAs(provider, role)"
              >
                <PlatformLogo :platform="provider.key" :mono="role === 'broadcaster'" />
                {{ $t(`site.login.role.${role}`) }}
              </Button>
            </template>
            <Button
              v-else
              :variant="shown.length === 1 ? 'default' : 'outline'"
              class="w-full"
              @click="signIn(provider)"
            >
              <PlatformLogo :platform="provider.key" :mono="shown.length === 1" />
              {{ $t('site.login.with', { provider: provider.displayName }) }}
            </Button>
          </template>
          <Button
            v-if="!codeOnly && codeProviders.length > 0"
            variant="ghost"
            class="w-full"
            @click="useCode"
          >
            {{ $t('site.login.useCode') }}
          </Button>
        </div>
      </div>
      <template v-if="mode === 'options' && enabled.length > 0 && switchedOff.length > 0" #footer>
        <p class="text-sm text-muted-foreground">{{ switchedOffLine }}</p>
      </template>
    </PanelCard>
    <p class="max-w-100 text-center text-xs text-balance text-muted-foreground">
      {{ $t('site.login.terms') }}
    </p>
  </main>
</template>

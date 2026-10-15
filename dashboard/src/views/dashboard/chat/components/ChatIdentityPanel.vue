<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Chat identity: how the owner looks in Twitch chat. The name color can be set here; Twitch gives
// everyone 15 named colors and only Turbo or Prime members any hex. Badges show as worn: Twitch has
// no API to choose which badge a user displays.
import { useQuery } from '@tanstack/vue-query';
import { computed, ref } from 'vue';

import { deviceApprovalQuery } from '@/api/signIn';
import {
  useChatIdentity,
  useSetChatColor,
  useStartScopeRegrant,
} from '@/api/chatIdentity';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/input';
import { useCurrentChannel } from '@/composables/useCurrentChannel';

const { channelId } = useCurrentChannel();
const identityQuery = useChatIdentity(channelId);
const setColor = useSetChatColor(channelId);
const regrant = useStartScopeRegrant();

const identity = computed(() => identityQuery.data.value ?? null);
const hex = ref('');
const shownColor = computed(() => identity.value?.color ?? undefined);
const refusal = computed(() => {
  const error = setColor.error.value;

  return error?.detail ?? error?.message ?? null;
});

const regrantCode = computed(() => regrant.data.value ?? null);
const approval = useQuery(computed(() => deviceApprovalQuery('twitch', regrantCode.value)));
const approved = computed(() => approval.data.value?.status === 'authorized');

function isCurrent(colorHex: string): boolean {
  return identity.value?.color?.toUpperCase() === colorHex.toUpperCase();
}

function applyHex(): void {
  const value = hex.value.trim();

  if (value === '') return;

  setColor.mutate(value.startsWith('#')
    ? value
    : `#${value}`);
}

function badgeImage(urls: Record<string, string>): string | undefined {
  return urls['2'] ?? urls['1'];
}
</script>

<template>
  <div class="flex h-full min-w-0 flex-col gap-4 overflow-y-auto rounded-xl border bg-card p-4">
    <header class="flex flex-col gap-1">
      <h2 class="text-base font-semibold">{{ $t('dashboard.chat.identity.title') }}</h2>
      <p class="text-sm text-muted-foreground">{{ $t('dashboard.chat.identity.description') }}</p>
    </header>
    <p v-if="identityQuery.isPending.value" class="text-sm text-muted-foreground">
      {{ $t('dashboard.chat.identity.loading') }}
    </p>
    <p v-else-if="identityQuery.isError.value || !identity" class="text-sm text-destructive">
      {{ $t('dashboard.chat.identity.loadFailed') }}
    </p>
    <div v-else class="flex flex-col gap-6">
      <section class="flex flex-col gap-2" data-test="preview">
        <h3 class="text-sm font-medium">{{ $t('dashboard.chat.identity.preview') }}</h3>
        <div class="flex items-center gap-1 rounded-md border bg-muted/40 px-3 py-2 text-sm">
          <img
            v-for="badge in identity.badges"
            :key="`${badge.setId}/${badge.id}`"
            :src="badgeImage(badge.urls)"
            :alt="badge.setId"
            class="size-[18px]"
          />
          <span class="font-semibold" data-test="name" :style="{ color: shownColor }">
            {{ identity.displayName }}</span>
          <span class="text-muted-foreground">
            {{ $t('dashboard.chat.identity.sample') }}
          </span>
        </div>
      </section>

      <section class="flex flex-col gap-2">
        <h3 class="text-sm font-medium">{{ $t('dashboard.chat.identity.color') }}</h3>

        <div v-if="!identity.canManageColor" class="flex flex-col gap-2 rounded-md border p-3" data-test="scope">
          <p class="text-sm">
            {{ $t('dashboard.chat.identity.needsScope', { scope: identity.colorScope }) }}
          </p>
          <p v-if="regrantCode && !approved" class="text-sm" data-test="regrant-code">
            {{ $t('dashboard.chat.identity.enterCode', { code: regrantCode.userCode }) }}
            <a
              :href="regrantCode.verificationUri"
              target="_blank"
              rel="noopener"
              class="underline"
            >{{ regrantCode.verificationUri }}</a>
          </p>
          <p v-if="approved" class="text-sm">{{ $t('dashboard.chat.identity.approved') }}</p>
          <p v-if="regrant.isError.value" class="text-sm text-destructive">
            {{ regrant.error.value?.detail ?? $t('dashboard.chat.identity.regrantFailed') }}
          </p>
          <Button
            v-if="!regrantCode"
            class="self-start"
            :disabled="regrant.isPending.value"
            data-test="regrant"
            @click="regrant.mutate()"
          >
            {{ $t('dashboard.chat.identity.regrant') }}
          </Button>
        </div>

        <div class="flex flex-wrap gap-2" role="radiogroup" :aria-label="$t('dashboard.chat.identity.color')">
          <button
            v-for="named in identity.namedColors"
            :key="named.name"
            type="button"
            role="radio"
            :aria-checked="isCurrent(named.hex)"
            :aria-label="named.name.replaceAll('_', ' ')"
            :title="named.name.replaceAll('_', ' ')"
            :disabled="!identity.canManageColor || setColor.isPending.value"
            class="size-9 rounded-md border-2 transition-shadow focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
            :class="isCurrent(named.hex)
              ? 'border-foreground'
              : 'border-transparent'"
            :style="{ backgroundColor: named.hex }"
            :data-test="`swatch-${named.name}`"
            @click="setColor.mutate(named.name)"
          />
        </div>

        <form class="flex items-center gap-2" @submit.prevent="applyHex">
          <Input
            v-model="hex"
            class="w-36"
            placeholder="#9146FF"
            :aria-label="$t('dashboard.chat.identity.hex')"
            :disabled="!identity.canManageColor"
            data-test="hex"
          />
          <Button
            type="submit"
            variant="outline"
            :disabled="!identity.canManageColor || setColor.isPending.value"
            data-test="apply-hex"
          >
            {{ $t('dashboard.chat.identity.applyHex') }}
          </Button>
        </form>
        <p class="text-xs text-muted-foreground">{{ $t('dashboard.chat.identity.hexNote') }}</p>
        <p v-if="refusal" class="text-sm text-destructive" role="alert" data-test="refusal">{{ refusal }}</p>
      </section>

      <section class="flex flex-col gap-2">
        <h3 class="text-sm font-medium">{{ $t('dashboard.chat.identity.badges') }}</h3>
        <ul v-if="identity.badges.length > 0" class="flex flex-wrap gap-3" data-test="badges">
          <li
            v-for="badge in identity.badges"
            :key="`${badge.setId}/${badge.id}`"
            class="flex items-center gap-2 text-sm"
          >
            <img :src="badgeImage(badge.urls)" :alt="''" class="size-[18px]" />
            {{ badge.setId.replaceAll('-', ' ') }}
          </li>
        </ul>
        <p v-else class="text-sm text-muted-foreground">{{ $t('dashboard.chat.identity.noBadges') }}</p>
        <p class="text-xs text-muted-foreground" data-test="badge-note">
          {{ $t('dashboard.chat.identity.badgeNote') }}
        </p>
      </section>
    </div>
  </div>
</template>

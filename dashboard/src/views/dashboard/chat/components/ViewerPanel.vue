<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The Viewer drawer: the chatter picked in the feed, with their real totals and latest lines here.
// The foot is one row that swaps between the actions, a step and the result. Timeout and Ban act in
// this channel only. What they said in other channels has no endpoint yet, so the panel says so.
import { computed, ref } from 'vue';
import { useI18n } from 'vue-i18n';
import Check from '~icons/nnz/check';
import Clock from '~icons/nnz/clock';
import Cross from '~icons/nnz/cross';
import StopSign from '~icons/nnz/stop-sign';

import { useBanUser, useTimeoutUser, useUnbanUser, type ChatLine } from '@/api/chat';
import { useCommunityMember } from '@/api/community';
import { useCurrencyConfig } from '@/api/economy';
import { useViewerProfile } from '@/api/people';
import PanelCard, { type PanelState } from '@/components/layout/PanelCard.vue';
import SegmentedControl from '@/components/layout/SegmentedControl.vue';
import StatTile from '@/components/layout/StatTile.vue';
import UserName from '@/components/layout/UserName.vue';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Button } from '@/components/ui/button';
import { readableNameColor } from '@/lib/accent';
import { useProfileDialogStore } from '@/stores/profileDialog';

type Step = 'idle' | 'pick' | 'ban' | 'done';
type Done = { text: string; undo: 'timeout' | 'ban' };

const LENGTHS = [60, 600, 3600] as const;
const LATEST = 6;

const props = defineProps<{
  userId: string | null;
  channelId: string | null;
  lines: ChatLine[];
  hour12: boolean;
}>();

const emit = defineEmits<{ close: [] }>();

const { t, n, locale } = useI18n();
const profile = useProfileDialogStore();
const timeouts = useTimeoutUser(() => props.channelId);
const banner = useBanUser(() => props.channelId);
const unbanner = useUnbanUser(() => props.channelId);

const member = useCommunityMember(() => props.channelId, () =>
  props.userId);
const wallet = useViewerProfile(() => props.channelId, () =>
  member.data.value?.internalUserId ?? null);
const currency = useCurrencyConfig(() =>
  props.userId === null
    ? null
    : props.channelId);

const step = ref<Step>('idle');
const done = ref<Done | null>(null);
const failed = ref(false);

const person = computed(() => member.data.value ?? null);

const theirs = computed<ChatLine[]>(() =>
  props.lines.filter((line: ChatLine) => line.userId === props.userId));
const latest = computed<ChatLine[]>(() =>
  theirs.value.slice(-LATEST).reverse());
const name = computed<string>(() =>
  person.value?.displayName ?? theirs.value.at(-1)?.displayName ?? '');

const state = computed<PanelState>(() => {
  if (props.userId === null)
    return 'empty';

  if (member.isPending.value)
    return 'loading';

  return person.value
    ? 'ready'
    : 'error';
});

const nameColor = computed<string | undefined>(() => {
  const color = theirs.value.at(-1)?.color;

  if (!color)
    return undefined;

  const theme = document.documentElement.classList.contains('light')
    ? 'light'
    : 'dark';

  return readableNameColor(color, theme, 4.5) ?? undefined;
});

const account = computed(() => wallet.data.value?.economy.wallet ?? null);
const currencyName = computed<string>(() =>
  currency.data.value?.currencyNamePlural ?? currency.data.value?.currencyName ?? '');
const showCurrency = computed<boolean>(() =>
  currency.data.value?.isEnabled === true && account.value !== null && currencyName.value !== '');

const lengthChoices = LENGTHS.map((seconds) => ({
  value: String(seconds),
  label: t(`dashboard.chat.lengths.${seconds}`),
}));

function timeOf(line: ChatLine): string {
  return new Date(line.timestamp).toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: props.hour12,
  });
}

function emoteUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0];
}

function go(next: Step): void {
  step.value = next;
  failed.value = false;
}

function openProfile(event: MouseEvent): void {
  if (props.userId === null) return;

  profile.open(
    props.userId,
    event.currentTarget instanceof HTMLElement
      ? event.currentTarget
      : null,
  );
}

async function timeout(seconds: string): Promise<void> {
  if (props.userId === null) return;

  try {
    await timeouts.mutateAsync({ targetUserId: props.userId, durationSeconds: Number(seconds) });

    done.value = {
      text: t('dashboard.chat.viewer.timedOut', {
        name: name.value,
        length: t(`dashboard.chat.lengths.${seconds}`),
      }),
      undo: 'timeout',
    };

    go('done');
  } catch {
    go('idle');
    failed.value = true;
  }
}

async function ban(): Promise<void> {
  if (props.userId === null) return;

  try {
    await banner.mutateAsync({ targetUserId: props.userId, scope: 'this_channel' });
    done.value = { text: t('dashboard.chat.viewer.banned', { name: name.value }), undo: 'ban' };
    go('done');
  } catch {
    go('idle');
    failed.value = true;
  }
}

async function undo(): Promise<void> {
  if (props.userId === null) return;

  try {
    await unbanner.mutateAsync({ targetUserId: props.userId, scope: 'this_channel' });
    go('idle');
  } catch {
    failed.value = true;
  }
}
</script>

<template>
  <PanelCard
    class="h-full"
    :title="$t('dashboard.chat.viewer.title')"
    pad
    :state="state"
    :empty-text="$t('dashboard.chat.viewer.empty')"
    :error-text="$t('dashboard.chat.viewer.error')"
    @retry="member.refetch()"
  >
    <template #actions>
      <Button
        data-viewer-close
        variant="ghost"
        size="icon"
        :aria-label="$t('dashboard.chat.viewer.close')"
        @click="emit('close')"
      >
        <Cross class="size-5" aria-hidden="true" />
      </Button>
    </template>
    <div class="grid min-w-0 grid-cols-[minmax(0,1fr)] gap-4">
      <div class="flex min-w-0 items-center gap-3">
        <Avatar class="size-10">
          <AvatarImage v-if="person?.profileImageUrl" :src="person.profileImageUrl" :alt="name" />
          <AvatarFallback>{{ name.slice(0, 2).toUpperCase() }}</AvatarFallback>
        </Avatar>
        <div class="min-w-0 flex-1">
          <UserName
            v-if="userId"
            class="block max-w-full truncate text-base"
            :user-id="userId"
            :name="name"
            :style="{ color: nameColor }"
          />
          <p class="truncate text-sm text-muted-foreground">
            {{ $t('dashboard.chat.viewer.selected') }}
          </p>
        </div>
        <Button variant="ghost" size="sm" @click="openProfile">
          {{ $t('dashboard.chat.viewer.profile') }}
        </Button>
      </div>
      <div class="grid grid-cols-2 gap-4">
        <StatTile
          plain
          :label="$t('dashboard.chat.viewer.messages')"
          :value="Number(person?.messageCount ?? 0)"
          :context="$t('dashboard.chat.viewer.watched', { hours: n(Number(person?.watchHours ?? 0), { maximumFractionDigits: 1 }) })"
        />
        <StatTile
          v-if="showCurrency && account"
          plain
          :label="currencyName"
          :value="Number(account.balance)"
          :context="$t('dashboard.chat.viewer.earned', { count: n(Number(account.lifetimeEarned)) })"
        />
      </div>
      <section class="grid gap-2">
        <h3 class="text-sm font-medium">{{ $t('dashboard.chat.viewer.latest') }}</h3>
        <p v-if="latest.length === 0" class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.viewer.noMessages') }}
        </p>
        <ul v-else class="grid gap-1">
          <li
            v-for="line in latest"
            :key="line.id"
            data-viewer-message
            class="flex gap-2 text-sm"
          >
            <time class="flex-none text-muted-foreground tabular-nums" :datetime="line.timestamp">{{
              timeOf(line)
            }}</time>
            <span data-viewer-text class="min-w-0 flex-1 wrap-anywhere">
              <template v-for="(fragment, at) in line.fragments" :key="at">
                <img
                  v-if="fragment.emote && emoteUrl(fragment.emote.urls)"
                  :src="emoteUrl(fragment.emote.urls)"
                  :alt="fragment.text"
                  class="mx-0.5 inline-block h-[1.5em] align-middle"
                />
                <template v-else>{{ fragment.text }}</template>
              </template>
            </span>
          </li>
        </ul>
      </section>
      <section class="grid gap-2">
        <h3 class="text-sm font-medium">{{ $t('dashboard.chat.viewer.other') }}</h3>
        <p class="text-sm text-muted-foreground">
          {{ $t('dashboard.chat.viewer.otherUnavailable') }}
        </p>
      </section>
    </div>
    <template v-if="userId !== null" #footer>
      <div class="flex min-h-9 items-center gap-2">
        <p v-if="failed" role="alert" class="min-w-0 flex-1 text-sm text-destructive">
          {{ $t('dashboard.chat.viewer.failed') }}
        </p>
        <template v-else-if="step === 'done' && done">
          <span class="flex min-w-0 flex-1 items-center gap-2 text-sm" role="status">
            <Check class="size-4 flex-none" aria-hidden="true" />
            <span class="min-w-0">{{ done.text }}</span>
          </span>
        </template>
        <template v-else-if="step === 'ban'">
          <span class="min-w-0 flex-1 text-sm">{{
            $t('dashboard.chat.viewer.banAsk', { name })
          }}</span>
        </template>
        <template v-else-if="step === 'pick'">
          <div class="min-w-0 flex-1">
            <SegmentedControl
              :label="$t('dashboard.chat.viewer.length')"
              :options="lengthChoices"
              model-value=""
              @update:model-value="timeout"
            />
          </div>
        </template>
        <div v-else class="flex-1" />

        <template v-if="step === 'done' && done">
          <Button variant="outline" size="sm" @click="undo">
            {{ done.undo === 'ban' ? $t('dashboard.chat.viewer.unban') : $t('dashboard.chat.viewer.undo') }}
          </Button>
        </template>
        <template v-else-if="step === 'ban'">
          <Button variant="outline" size="sm" @click="go('idle')">
            {{ $t('dashboard.chat.viewer.cancel') }}
          </Button>
          <Button data-viewer-ban-confirm variant="destructive" size="sm" @click="ban">
            {{ $t('dashboard.chat.viewer.ban') }}
          </Button>
        </template>
        <template v-else-if="step === 'pick'">
          <Button variant="ghost" size="sm" @click="go('idle')">
            {{ $t('dashboard.chat.viewer.cancel') }}
          </Button>
        </template>
        <template v-else>
          <Button variant="outline" size="sm" @click="go('pick')">
            <Clock class="size-4" aria-hidden="true" />
            {{ $t('dashboard.chat.viewer.timeout') }}
          </Button>
          <Button variant="destructive" size="sm" @click="go('ban')">
            <StopSign class="size-4" aria-hidden="true" />
            {{ $t('dashboard.chat.viewer.ban') }}
          </Button>
        </template>
      </div>
    </template>
  </PanelCard>
</template>

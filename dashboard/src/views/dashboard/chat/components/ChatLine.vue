<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One chat line. The action tray floats over the line on hover or focus and never moves it.
// Timeout picks its length in a menu, Delete runs at once, Ban asks the page for its confirm
// dialog, and Profile opens the one profile dialog of the frame.
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import Clock from '~icons/nnz/clock';
import StopSign from '~icons/nnz/stop-sign';
import Trash from '~icons/nnz/trash';
import UserCircle from '~icons/nnz/user-circle';

import { useDeleteChatMessage, useTimeoutUser, type ChatLine } from '@/api/chat';
import PlatformLogo from '@/components/layout/PlatformLogo.vue';
import UserName from '@/components/layout/UserName.vue';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip';
import { readableNameColor } from '@/lib/accent';
import { formatLength } from '@/lib/time';
import { useProfileDialogStore } from '@/stores/profileDialog';

import { emoteHeights, spacingPadding, splitMentions, type ChatDisplay } from '../chatDisplay';

const LENGTHS: readonly number[] = [60, 600, 3600, 86400];

const props = defineProps<{
  line: ChatLine;
  channelId: string | null;
  display: ChatDisplay;
  index: number;
  me: string | null;
  focus?: 'on' | 'dim';
}>();

const emit = defineEmits<{
  ban: [line: ChatLine];
  focus: [line: ChatLine];
}>();

const { t, locale } = useI18n();
const timeouts = useTimeoutUser(() => props.channelId);
const deletes = useDeleteChatMessage(() => props.channelId);
const profile = useProfileDialogStore();

const time = computed<string>(() =>
  new Date(props.line.timestamp).toLocaleTimeString(locale.value, {
    hour: '2-digit',
    minute: '2-digit',
    hour12: props.display.timestamps === '12',
  }),
);

const nameColor = computed<string | undefined>(() => {
  const { color } = props.line;

  if (!color || props.display.nameColors === 'off')
    return undefined;

  if (props.display.nameColors === 'sent')
    return color;

  const theme = document.documentElement.classList.contains('light')
    ? 'light'
    : 'dark';

  return readableNameColor(color, theme, 4.5) ?? undefined;
});

// A deleted message set to "show" stays readable; a timeout or ban still strikes the line.
const shownDeleted = computed<boolean>(() =>
  props.line.removed === 'deleted' && props.display.deleted === 'show');
const struck = computed<boolean>(() => Boolean(props.line.removed) && !shownDeleted.value);

const mentionsMe = computed<boolean>(() => {
  if (!props.display.mentions)
    return false;

  return props.line.fragments.some((fragment) => {
    const found = splitMentions(fragment.text, props.me);

    return !fragment.emote && found.some((part) => part.mention);
  });
});

const rowClass = computed<string[]>(() => {
  const classes: string[] = [spacingPadding[props.display.spacing]];

  if (props.display.narrow) {
    classes.push('max-w-[65ch]');
  }

  // A focus lights many lines at once, so it takes an 8% tint and keeps the full accent scarce.
  if (props.focus === 'dim') {
    classes.push('opacity-70');
  }

  if (props.focus === 'on') {
    classes.push('bg-primary/8');
  } else if (mentionsMe.value) {
    classes.push('bg-primary/10');
  } else if (props.display.alternating && props.index % 2 === 1) {
    classes.push('bg-muted/40');
  }

  return classes;
});

function parts(text: string): ReturnType<typeof splitMentions> {
  return props.display.mentions
    ? splitMentions(text, props.me)
    : [{ text, mention: false }];
}

function badgeUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0];
}

// A timeout or ban says what happened, with the timeout's length; other removals only strike the line.
const removal = computed<string | null>(() => {
  const { removed, timeoutSeconds } = props.line;

  if (removed === 'banned') {
    return t('dashboard.chat.line.banned');
  }

  if (removed === 'timeout' && timeoutSeconds !== undefined) {
    return t('dashboard.chat.line.timedOut', {
      length: formatLength(timeoutSeconds, locale.value),
    });
  }

  return null;
});

async function timeout(seconds: number): Promise<void> {
  try {
    await timeouts.mutateAsync({ targetUserId: props.line.userId, durationSeconds: seconds });
    toast.success(t('dashboard.chat.timeout.result', { name: props.line.displayName }));
  } catch {
    toast.error(t('dashboard.chat.timeout.failure'));
  }
}

async function remove(): Promise<void> {
  try {
    await deletes.mutateAsync(props.line.id);
    toast.success(t('dashboard.chat.delete.result'));
  } catch {
    toast.error(t('dashboard.chat.delete.failure'));
  }
}

function openProfile(event: MouseEvent): void {
  profile.open(
    props.line.userId,
    event.currentTarget instanceof HTMLElement
      ? event.currentTarget
      : null,
  );
}

function emoteUrl(urls: Record<string, string>): string | undefined {
  return urls['1'] ?? Object.values(urls)[0];
}

// A click on the line itself picks its chatter; a click on a name or an action does its own job.
function pick(event: MouseEvent): void {
  const target = event.target;

  if (target instanceof Element && target.closest('button, a')) return;

  emit('focus', props.line);
}
</script>

<template>
  <div
    :data-line-id="line.id"
    :data-removed="line.removed"
    :data-alt="display.alternating && index % 2 === 1 ? 'on' : 'off'"
    :data-mention="mentionsMe ? 'true' : undefined"
    :data-focus="focus"
    class="group/line relative flex cursor-pointer gap-2 rounded-lg px-3 text-sm hover:bg-muted focus-within:bg-muted"
    :class="rowClass"
    @click="pick"
  >
    <time
      v-if="display.timestamps !== 'off'"
      class="flex-none text-muted-foreground tabular-nums"
      :datetime="line.timestamp"
    >{{ time }}</time>
    <img
      v-if="display.avatars && line.avatarUrl"
      data-avatar
      :src="line.avatarUrl"
      alt=""
      class="size-6 flex-none rounded-full"
    />
    <p
      class="min-w-0 flex-1 wrap-break-word"
      :class="{ 'text-muted-foreground': line.removed, 'line-through': struck }"
    >
      <PlatformLogo
        v-if="display.platform"
        data-platform
        :platform="line.provider"
        class="mr-1 inline-block size-4 align-middle"
      />
      <template v-if="display.badges">
        <img
          v-for="badge in line.badges"
          :key="badge.setId + badge.id"
          data-badge
          :src="badgeUrl(badge.urls)"
          :alt="badge.setId"
          class="mr-1 inline-block size-4 align-middle"
        />
      </template>
      <UserName
        :user-id="line.userId"
        :name="line.displayName"
        :style="{ color: nameColor }"
      />
      <template v-for="(fragment, at) in line.fragments" :key="at">
        <img
          v-if="fragment.emote && emoteUrl(fragment.emote.urls)"
          data-emote
          :src="emoteUrl(fragment.emote.urls)"
          :alt="fragment.text"
          :style="{ height: emoteHeights[display.emoteSize] }"
          class="mx-0.5 inline-block align-middle"
        />
        <template v-else>
          {{ ' ' }}
          <template v-for="(part, slot) in parts(fragment.text)" :key="slot">
            <mark v-if="part.mention" data-mention class="rounded-sm bg-primary/25 px-0.5 text-foreground">{{ part.text }}</mark>
            <template v-else>{{ part.text }}</template>
          </template>
        </template>
      </template>
      <span v-if="shownDeleted" data-deleted-tag class="ml-1 text-muted-foreground">{{
        $t('dashboard.chat.display.deletedTag')
      }}</span>
    </p>
    <span v-if="removal" data-removal class="flex-none text-muted-foreground">{{ removal }}</span>
    <div
      v-if="!line.removed"
      class="absolute top-1/2 right-2 flex -translate-y-1/2 items-center rounded-[12px] border p-0.5 opacity-0 group-hover/line:opacity-100 group-focus-within/line:opacity-100 has-data-[state=open]:opacity-100"
      data-tray
      role="group"
      :aria-label="$t('dashboard.chat.line.actions', { name: line.displayName })"
    >
      <DropdownMenu>
        <Tooltip>
          <TooltipTrigger as-child>
            <DropdownMenuTrigger as-child>
              <Button
                data-line-action="timeout"
                variant="ghost"
                size="icon"
                :aria-label="$t('dashboard.chat.timeout.label', { name: line.displayName })"
              >
                <Clock class="size-5" aria-hidden="true" />
              </Button>
            </DropdownMenuTrigger>
          </TooltipTrigger>
          <TooltipContent>{{ $t('dashboard.chat.timeout.tip') }}</TooltipContent>
        </Tooltip>
        <DropdownMenuContent align="end">
          <DropdownMenuLabel>{{ $t('dashboard.chat.timeout.menu') }}</DropdownMenuLabel>
          <DropdownMenuItem
            v-for="seconds in LENGTHS"
            :key="seconds"
            :data-timeout-length="seconds"
            @select="timeout(seconds)"
          >
            {{ $t(`dashboard.chat.lengths.${seconds}`) }}
          </DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="delete"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.delete.label')"
            @click="remove"
          >
            <Trash class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.delete.label') }}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="ban"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.ban.label', { name: line.displayName })"
            @click="emit('ban', line)"
          >
            <StopSign class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.ban.tip') }}</TooltipContent>
      </Tooltip>
      <Tooltip>
        <TooltipTrigger as-child>
          <Button
            data-line-action="profile"
            variant="ghost"
            size="icon"
            :aria-label="$t('dashboard.chat.line.profile', { name: line.displayName })"
            @click="openProfile"
          >
            <UserCircle class="size-5" aria-hidden="true" />
          </Button>
        </TooltipTrigger>
        <TooltipContent>{{ $t('dashboard.chat.line.profileTip') }}</TooltipContent>
      </Tooltip>
    </div>
  </div>
</template>

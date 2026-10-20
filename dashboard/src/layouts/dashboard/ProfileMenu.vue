<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
import { useFullscreen } from '@vueuse/core';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';
import { useRouter, type RouteLocationRaw } from 'vue-router';
import ChevronDown from '~icons/nnz/chevron-down';
import ArrowEnterFullscreen from '~icons/nnz/arrow-enter-fullscreen';
import ArrowExitFullscreen from '~icons/nnz/arrow-exit-fullscreen';
import BookOpen from '~icons/nnz/book-open';
import DoorOut from '~icons/nnz/door-out';
import Eye from '~icons/nnz/eye';
import Shield from '~icons/nnz/shield';
import Translate from '~icons/nnz/translate';

import { signOut, useCurrentUser } from '@/api/auth';
import { useViewAs, type ViewAsRole } from '@/api/viewAs';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuSub,
  DropdownMenuSubContent,
  DropdownMenuSubTrigger,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { chooseLocale, locales, ownLanguageName } from '@/i18n';
import { loginRoute } from '@/views/site/login/route';

// icon: only the picture, for the narrow header.
defineProps<{ icon?: boolean }>();

const { t, locale } = useI18n();
const router = useRouter();
const { data: user } = useCurrentUser();
const { current } = useCurrentChannel();
const preview = useViewAs();

// Fullscreen hides the browser's own bars and gives the page the whole screen. A browser without the Fullscreen API
// (an iPhone) gets no entry instead of one that does nothing.
const { isSupported: canFullscreen, isFullscreen, toggle: toggleFullscreen } = useFullscreen();

const settingsLink: RouteLocationRaw = { name: 'dashboard-settings', query: { view: 'device' } };
const adminLink: RouteLocationRaw = { name: 'dashboard-admin-channels' };

// The two links appear when their page exists, like every other entry of the frame.
const hasReadability = computed<boolean>(() => router.hasRoute('dashboard-settings'));
// A previewed role is a full identity swap: only the banner shows the real admin.
const previewing = computed<boolean>(() => preview.role.value !== '');
const hasAdmin = computed<boolean>(
  () =>
    !previewing.value && user.value?.isAdmin === true && router.hasRoute('dashboard-admin-channels'),
);

const initials = computed<string>(() =>
  (user.value?.displayName ?? '')
    .replace(/[^A-Za-z0-9]/g, '')
    .slice(0, 2)
    .toUpperCase(),
);
// While a role is previewed the footer names that role; otherwise the person's own role in this channel.
const role = computed<string>(() => {
  if (previewing.value)
    return t(`frame.profile.${preview.role.value}`);

  return current.value?.kind === 'moderated'
    ? t('frame.profile.moderator')
    : t('frame.profile.broadcaster');
});

// Only the owner of the channel can preview a smaller role; a moderator already sees theirs.
const isOwner = computed<boolean>(() => !previewing.value && current.value?.kind === 'own');

async function leave(): Promise<void> {
  await signOut();
  await router.push({ name: loginRoute.name });
}

function startPreview(target: ViewAsRole): Promise<void> {
  const login = current.value?.login;

  return target === 'viewer' && login !== undefined
    ? preview.start(target, { login })
    : preview.start(target);
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <button
        type="button"
        data-slot="profile-menu"
        :aria-label="t('frame.profile.menu')"
        class="group flex min-h-11 min-w-0 items-center gap-2 rounded-md p-2 text-left outline-none hover:bg-sidebar-accent focus-visible:ring-2 focus-visible:ring-ring"
        :class="icon ? '' : 'w-full'"
      >
        <Avatar class="size-8 rounded-lg">
          <AvatarImage v-if="user?.profileImageUrl" :src="user.profileImageUrl" :alt="''" />
          <AvatarFallback class="rounded-lg">{{ initials }}</AvatarFallback>
        </Avatar>
        <template v-if="!icon">
          <span class="min-w-0 flex-1">
            <span class="block truncate text-sm font-medium">{{ user?.displayName }}</span>
            <span class="block truncate text-xs text-muted-foreground">{{ role }}</span>
          </span>
          <ChevronDown
            class="size-4 shrink-0 text-muted-foreground transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
            aria-hidden="true"
          />
        </template>
      </button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" side="top" class="w-64">
      <DropdownMenuLabel class="truncate">{{ user?.displayName }}</DropdownMenuLabel>
      <DropdownMenuSeparator />
      <DropdownMenuItem v-if="hasReadability" as-child>
        <RouterLink :to="settingsLink">
          <BookOpen aria-hidden="true" />
          {{ t('frame.profile.readability') }}
        </RouterLink>
      </DropdownMenuItem>
      <DropdownMenuItem v-if="isOwner" @select="startPreview('moderator')">
        <Eye aria-hidden="true" />
        {{ t('frame.profile.viewAsModerator') }}
      </DropdownMenuItem>
      <DropdownMenuItem v-if="isOwner" @select="startPreview('viewer')">
        <Eye aria-hidden="true" />
        {{ t('frame.profile.viewAsViewer') }}
      </DropdownMenuItem>
      <DropdownMenuSub>
        <DropdownMenuSubTrigger>
          <Translate aria-hidden="true" />
          {{ t('common.language') }}
        </DropdownMenuSubTrigger>
        <DropdownMenuSubContent>
          <DropdownMenuRadioGroup :model-value="locale" @update:model-value="chooseLocale">
            <DropdownMenuRadioItem v-for="tag in locales" :key="tag" :value="tag" :lang="tag">
              {{ ownLanguageName(tag) }}
            </DropdownMenuRadioItem>
          </DropdownMenuRadioGroup>
        </DropdownMenuSubContent>
      </DropdownMenuSub>
      <DropdownMenuItem v-if="canFullscreen" data-slot="fullscreen-toggle" @select="toggleFullscreen">
        <ArrowExitFullscreen v-if="isFullscreen" aria-hidden="true" />
        <ArrowEnterFullscreen v-else aria-hidden="true" />
        {{ isFullscreen ? t('frame.profile.exitFullscreen') : t('frame.profile.fullscreen') }}
      </DropdownMenuItem>
      <DropdownMenuItem v-if="hasAdmin" as-child>
        <RouterLink :to="adminLink">
          <Shield aria-hidden="true" />
          {{ t('frame.profile.admin') }}
        </RouterLink>
      </DropdownMenuItem>
      <DropdownMenuSeparator />
      <DropdownMenuItem @select="leave">
        <DoorOut aria-hidden="true" />
        {{ t('common.signOut') }}
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>

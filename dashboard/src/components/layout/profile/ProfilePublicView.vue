<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The profile a person without community access sees (owner 2026-10-16: "I must be able to look up basic info of
// a viewer, with the emotes and all that. No moderator actions, but everything available to a user."): who they
// are, how long they have been around, their latest public chat and their emotes. It reads only the public
// profile endpoint, so it holds no note, warning, ban or bot standing and offers no action.
import { injectDialogRootContext } from 'reka-ui';
import { computed } from 'vue';
import { useI18n } from 'vue-i18n';

import { usePublicProfile, type PublicProfile } from '@/api/viewer';
import DateTime from '@/components/layout/DateTime.vue';
import EmoteText from '@/components/layout/EmoteText.vue';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ProfileEmoteGroups from '@/components/layout/profile/ProfileEmoteGroups.vue';
import { tierNumber } from '@/components/layout/profile/profileRoles';
import { Avatar, AvatarFallback, AvatarImage } from '@/components/ui/avatar';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DialogDescription, DialogTitle } from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useTime } from '@/composables/useTime';

const props = defineProps<{ userId: string }>();

const tab = defineModel<string>('tab', { default: 'overview' });

const TABS = ['overview', 'chat', 'emotes'] as const;

const { t, te } = useI18n();
const time = useTime();
const { channelId } = useCurrentChannel();
const inDialog = injectDialogRootContext(null) !== null;

const profile = usePublicProfile(channelId, () =>
  props.userId);

const title = computed<string>(() =>
  profile.data.value?.displayName ?? t('profile.title'));

// The badge set ids are the platform's own words; a known one is translated, an unknown one shows as sent.
function badgeText(set: string): string {
  return te(`profile.public.badges.${set}`)
    ? t(`profile.public.badges.${set}`)
    : set;
}

function dateOrNull(iso?: string | null): string | null {
  return iso == null
    ? null
    : time.date(iso);
}

function tierText(code?: string | null): string | null {
  const tier = tierNumber(code ?? null);

  return tier === null
    ? null
    : t('profile.tier', { tier });
}

function factsOf(person: PublicProfile): KeyValueItem[] {
  return [
    { label: t('profile.facts.username'), value: `@${person.username}` },
    { label: t('profile.facts.accountCreated'), value: dateOrNull(person.accountCreatedAt) },
    { label: t('profile.facts.following'), value: dateOrNull(person.followedAt) },
    { label: t('profile.facts.subscription'), value: tierText(person.subTier) },
  ];
}

</script>

<template>
  <div data-slot="profile-public" class="flex flex-1 flex-col">
    <div v-if="profile.isPending.value" data-profile-loading class="grid gap-2 px-6 pb-4">
      <Skeleton class="h-16 rounded-lg" />
      <Skeleton class="h-32 rounded-lg" />
    </div>
    <div
      v-else-if="!profile.data.value"
      data-profile-error
      role="alert"
      class="grid justify-items-start gap-3 px-6 pb-4 text-sm"
    >
      <span class="text-destructive">{{ $t('profile.error') }}</span>
      <Button variant="outline" size="sm" @click="profile.refetch()">{{
        $t('common.panel.retry')
      }}</Button>
    </div>

    <template v-else>
      <div class="flex flex-none items-center gap-3 px-6 pt-2 pb-6">
        <Avatar class="size-12">
          <AvatarImage
            v-if="profile.data.value.profileImageUrl"
            :src="profile.data.value.profileImageUrl"
            :alt="title"
          />
          <AvatarFallback>{{ title.slice(0, 2).toUpperCase() }}</AvatarFallback>
        </Avatar>
        <div class="grid min-w-0 gap-1">
          <div class="flex min-w-0 items-baseline gap-2">
            <component
              :is="inDialog
                ? DialogTitle
                : 'h2'"
              data-profile-public-name
              class="truncate text-lg font-semibold"
              :style="{ color: profile.data.value.color ?? undefined }"
            >
              {{ title }}
            </component>
            <DialogDescription v-if="inDialog" class="sr-only">{{
              `@${profile.data.value.username}`
            }}</DialogDescription>
            <span v-if="profile.data.value.pronouns" class="text-sm text-muted-foreground">{{
              profile.data.value.pronouns
            }}</span>
          </div>
          <ul v-if="profile.data.value.badges.length > 0" data-profile-badges class="flex flex-wrap gap-1">
            <li v-for="badge in profile.data.value.badges" :key="`${badge.setId}/${badge.id}`">
              <Badge variant="secondary">{{ badgeText(badge.setId) }}</Badge>
            </li>
          </ul>
        </div>
      </div>

      <Tabs v-model="tab" class="flex min-w-0 flex-1 flex-col gap-0">
        <div class="flex-none px-6">
          <TabsList>
            <TabsTrigger v-for="name in TABS" :key="name" :value="name">{{
              $t(`profile.tabs.${name}`)
            }}</TabsTrigger>
          </TabsList>
        </div>
        <div class="grid flex-1 grid-cols-[minmax(0,1fr)] overflow-y-auto px-6 py-4">
          <TabsContent value="overview">
            <KeyValueList :items="factsOf(profile.data.value)" />
          </TabsContent>

          <TabsContent value="chat">
            <PanelCard
              bare
              :title="$t('profile.chat.title')"
              :state="profile.data.value.recentMessages.length === 0
                ? 'empty'
                : 'ready'"
              :empty-text="$t('profile.chat.empty')"
              :error-text="$t('profile.chat.error')"
            >
              <ul data-profile-chat-lines class="grid gap-2">
                <li v-for="line in profile.data.value.recentMessages" :key="line.id" class="flex gap-2 text-sm">
                  <span class="shrink-0 text-xs text-muted-foreground"><DateTime :value="line.sentAt" variant="dateTime" /></span>
                  <EmoteText class="wrap-anywhere" :text="line.message" />
                </li>
              </ul>
            </PanelCard>
          </TabsContent>

          <TabsContent value="emotes">
            <PanelCard
              bare
              :title="$t('profile.emotes.title')"
              :state="profile.data.value.emotes.length === 0
                ? 'empty'
                : 'ready'"
              :empty-text="$t('profile.emotes.empty')"
              :error-text="$t('profile.emotes.error')"
            >
              <ProfileEmoteGroups :emotes="profile.data.value.emotes" :no-twitch-emotes="false" />
            </PanelCard>
          </TabsContent>
        </div>
      </Tabs>
    </template>
  </div>
</template>

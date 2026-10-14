<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// One person's full profile: header, tabs and the actions. The profile dialog and the People page both show
// this view, so a person looks the same wherever you open them. It shows only what the bot stores: a tab
// with nothing to show says so. The tabs are local state, not the route: the page behind keeps its own ?view=.
import { injectDialogRootContext } from 'reka-ui';
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';

import {
  useBanMember,
  useCommunityMember,
  useVipChange,
  type CommunityMember,
} from '@/api/community';
import { useViewerProfile } from '@/api/people';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import KeyValueList, { type KeyValueItem } from '@/components/layout/KeyValueList.vue';
import PanelCard from '@/components/layout/PanelCard.vue';
import ProfileHeader, { type ProfilePreview } from '@/components/layout/profile/ProfileHeader.vue';
import ProfileData from '@/components/layout/profile/ProfileData.vue';
import ProfileOverview from '@/components/layout/profile/ProfileOverview.vue';
import ProfilePoints from '@/components/layout/profile/ProfilePoints.vue';
import { roleKeys, type RoleKey } from '@/components/layout/profile/profileRoles';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { SAVE_FAILED_MESSAGE, SAVE_FAILED_TITLE } from '@/composables/useOptimisticSave';
import { merge } from '@/lib/array';
import { shortDate } from '@/lib/time';
import { useSaveErrorsStore } from '@/stores/saveErrors';

type ActivityLine = CommunityMember['recentActivity'][number];

// preview: the name, login and avatar a list row or search hit already has, shown while the person loads.
const props = defineProps<{ userId: string; preview?: ProfilePreview | null }>();

// The header already shows every count, so no tab repeats them. Points and Data come from the bot's stored
// profile, so they show once it loaded.
const BASE_TABS: readonly string[] = ['overview', 'chat', 'moderation'];
const PROFILE_TABS: readonly string[] = ['points', 'data'];

// The dialog scrolls the tabs inside itself. On a page a phone scrolls the page once, never a box inside it.
const inDialog = injectDialogRootContext(null) !== null;

// Every panel stays mounted in one grid cell, so the view takes the tallest tab's size up front and never jumps.
// A phone page scrolls anyway, so there a short tab drops the others' height instead of trailing empty space.
const PANEL_CLASS = inDialog
  ? 'col-start-1 row-start-1 data-[state=inactive]:invisible'
  : 'col-start-1 row-start-1 data-[state=inactive]:invisible max-md:data-[state=inactive]:hidden';
const fit = inDialog
  ? 'min-h-0'
  : 'md:min-h-0';
const scroll = inDialog
  ? 'overflow-y-auto'
  : 'md:overflow-y-auto';

const { t, n, locale } = useI18n();
const errors = useSaveErrorsStore();
const { channelId, current } = useCurrentChannel();

const tab = ref('overview');
const confirmBan = ref(false);

const member = useCommunityMember(channelId, () =>
  props.userId);
const person = computed<CommunityMember | null>(() => member.data.value ?? null);
const viewer = useViewerProfile(channelId, () =>
  person.value?.internalUserId ?? null);
const viewerProfile = computed(() => viewer.data.value ?? null);
const roles = computed<RoleKey[]>(() =>
  person.value
    ? roleKeys(person.value, viewerProfile.value, current.value?.login)
    : []);
// Twitch refuses a ban or VIP on the channel's own account, so the view does not offer them.
const isOwner = computed<boolean>(() => roles.value.includes('broadcaster'));

// The mutations need the channel id when they are made; the view is mounted inside a signed-in channel.
const vipChange = useVipChange(channelId.value ?? '');
const ban = useBanMember(channelId.value ?? '');

// Twitch answers per person; null means Twitch could not be asked, so the view says unknown instead of guessing.
const isVip = computed<boolean | null>(() => person.value?.isVip ?? null);
const vipLabel = computed<string>(() => {
  if (isVip.value === null)
    return t('profile.vip.unknown');

  return isVip.value
    ? t('profile.vip.remove')
    : t('profile.vip.make');
});
const tabs = computed<readonly string[]>(() =>
  viewerProfile.value
    ? merge(BASE_TABS, PROFILE_TABS)
    : BASE_TABS);
const title = computed<string>(() => person.value?.displayName ?? t('profile.title'));
const lines = computed<ActivityLine[]>(() => person.value?.recentActivity ?? []);

// What the bot counted of the moderation on this person, beside the bans it stored one by one.
const actions = computed<KeyValueItem[]>(() => {
  const history = viewerProfile.value?.moderationHistory;

  if (!history)
    return [];

  const last = history.lastActionType && history.lastActionAt
    ? `${history.lastActionType} · ${dateText(history.lastActionAt)}`
    : null;

  return [
    { label: t('profile.moderation.timeouts'), value: n(Number(history.timeoutCount)) },
    { label: t('profile.moderation.bans'), value: n(Number(history.banCount)) },
    { label: t('profile.moderation.warnings'), value: n(Number(history.warningCount)) },
    { label: t('profile.moderation.deleted'), value: n(Number(history.messagesDeletedCount)) },
    { label: t('profile.moderation.lastAction'), value: last },
    {
      label: t('profile.moderation.lastWarning'),
      value: history.lastWarningAt
        ? dateText(history.lastWarningAt)
        : null,
    },
    {
      label: t('profile.moderation.warningSeen'),
      value: history.lastWarningAcknowledgedAt
        ? dateText(history.lastWarningAcknowledgedAt)
        : null,
    },
  ];
});

watch(
  () => props.userId,
  () => {
    tab.value = 'overview';
    confirmBan.value = false;
  },
);

function dateText(value: string): string {
  return shortDate(value, locale.value);
}

function toggleVip(): void {
  if (isVip.value === null) return;

  vipChange.mutate({ userId: props.userId, vip: !isVip.value });
}

async function banNow(): Promise<void> {
  try {
    await ban.mutateAsync({ userId: props.userId, reason: t('profile.ban.reason') });
  } catch {
    errors.report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE);
  }

  confirmBan.value = false;
}
</script>

<template>
  <div data-slot="profile-view" class="flex flex-1 flex-col" :class="fit">
    <ProfileHeader :person="person" :profile="viewerProfile" :roles="roles" :preview="preview" />

    <div v-if="member.isPending.value" data-profile-loading class="grid gap-2 px-6 pb-4">
      <Skeleton class="h-16 rounded-lg" />
      <Skeleton class="h-32 rounded-lg" />
    </div>
    <div
      v-else-if="!person"
      data-profile-error
      role="alert"
      class="grid justify-items-start gap-3 px-6 pb-4 text-sm"
    >
      <span class="text-destructive">{{ $t('profile.error') }}</span>
      <Button variant="outline" size="sm" @click="member.refetch()">{{
        $t('common.panel.retry')
      }}</Button>
    </div>

    <template v-else>
      <Tabs v-model="tab" class="flex flex-1 flex-col gap-0" :class="fit">
        <div class="flex-none overflow-x-auto px-6">
          <TabsList>
            <TabsTrigger v-for="name in tabs" :key="name" :value="name">{{
              $t(`profile.tabs.${name}`)
            }}</TabsTrigger>
          </TabsList>
        </div>
        <div class="grid flex-1 px-6 py-4" :class="[fit, scroll]">
          <TabsContent value="overview" force-mount :class="[PANEL_CLASS, 'grid content-start gap-4']">
            <ProfileOverview
              :person="person"
              :profile="viewerProfile"
              :roles="roles"
              @show-chat="tab = 'chat'"
            />
          </TabsContent>

          <TabsContent value="chat" force-mount :class="PANEL_CLASS">
            <!-- The bot sends at most 10 lines, so they list in full: no box scrolls inside the profile. -->
            <PanelCard
              :title="$t('profile.chat.title')"
              :state="lines.length > 0 ? 'ready' : 'empty'"
              :empty-text="$t('profile.chat.empty')"
            >
              <ul :aria-label="$t('profile.chat.title')" class="grid">
                <li
                  v-for="line in lines"
                  :key="`${line.timestamp}-${line.content}`"
                  class="grid gap-0.5 px-2 py-1.5 text-sm"
                >
                  <span class="text-xs text-muted-foreground">{{ dateText(line.timestamp) }}</span>
                  <span class="wrap-break-word">{{ line.content }}</span>
                </li>
              </ul>
            </PanelCard>
          </TabsContent>

          <TabsContent value="moderation" force-mount :class="[PANEL_CLASS, 'grid content-start gap-4']">
            <PanelCard
              v-if="actions.length > 0"
              data-profile-actions
              pad
              :title="$t('profile.moderation.counts')"
            >
              <KeyValueList :items="actions" />
            </PanelCard>
            <PanelCard
              :title="$t('profile.moderation.title')"
              :state="person.banHistory.length > 0 ? 'ready' : 'empty'"
              :empty-text="$t('profile.moderation.empty')"
            >
              <ul class="grid gap-2 p-2 text-sm">
                <li v-for="record in person.banHistory" :key="record.id" class="grid gap-0.5">
                  <span>{{ record.reason || $t('profile.moderation.noReason') }}</span>
                  <span class="text-xs text-muted-foreground">
                    {{
                      $t('profile.moderation.by', {
                        name: record.bannedBy,
                        date: dateText(record.bannedAt),
                      })
                    }}
                  </span>
                </li>
              </ul>
            </PanelCard>
          </TabsContent>

          <template v-if="viewerProfile">
            <TabsContent value="points" force-mount :class="PANEL_CLASS">
              <ProfilePoints :profile="viewerProfile" />
            </TabsContent>

            <TabsContent value="data" force-mount :class="PANEL_CLASS">
              <ProfileData :profile="viewerProfile" />
            </TabsContent>
          </template>
        </div>
      </Tabs>

      <div
        v-if="!isOwner"
        class="flex flex-none flex-col-reverse gap-2 border-t border-border px-6 py-4 sm:flex-row sm:justify-end"
      >
        <Button
          v-if="!person.isBanned"
          data-profile-ban
          type="button"
          variant="destructive"
          @click="confirmBan = true"
        >
          {{ $t('profile.ban.button') }}
        </Button>
        <Button data-profile-vip type="button" :disabled="isVip === null" @click="toggleVip">
          {{ vipLabel }}
        </Button>
      </div>
    </template>
  </div>

  <ConfirmDialog
    v-model:open="confirmBan"
    :title="$t('profile.ban.title', { name: title })"
    :description="$t('profile.ban.description', { name: title })"
    :action="$t('profile.ban.action', { name: title })"
    :pending="ban.isPending.value"
    @confirm="banNow"
  />
</template>

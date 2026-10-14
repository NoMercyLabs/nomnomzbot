<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one profile dialog of the frame (spec row ProfileDialog + UserName). A UserName opens it through
// the profileDialog store; a second name swaps the person. It shows only what the bot stores:
// a tab with nothing to show says so. The tabs are local state, not the route: the page behind keeps its own ?view=.
import { computed, ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import Cross from '~icons/nnz/cross';

import {
  useBanMember,
  useCommunityMember,
  useVipChange,
  type CommunityMember,
} from '@/api/community';
import { useViewerProfile } from '@/api/people';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import ProfileHeader from '@/components/layout/profile/ProfileHeader.vue';
import ProfileOverview from '@/components/layout/profile/ProfileOverview.vue';
import { roleKeys, type RoleKey } from '@/components/layout/profile/profileRoles';
import { shortDate } from '@/lib/time';
import PanelCard from '@/components/layout/PanelCard.vue';
import VirtualList from '@/components/layout/VirtualList.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
} from '@/components/ui/dialog';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { SAVE_FAILED_MESSAGE, SAVE_FAILED_TITLE } from '@/composables/useOptimisticSave';
import { useProfileDialogStore } from '@/stores/profileDialog';
import { useSaveErrorsStore } from '@/stores/saveErrors';

type ActivityLine = CommunityMember['recentActivity'][number];

// The header already shows every count, so no tab repeats them.
const TABS: readonly string[] = ['overview', 'chat', 'moderation'];

// Every panel stays mounted in one grid cell, so the dialog takes the tallest tab's size up front and never jumps.
const PANEL_CLASS = 'col-start-1 row-start-1 data-[state=inactive]:invisible';

const { t, locale } = useI18n();
const profile = useProfileDialogStore();
const errors = useSaveErrorsStore();
const { channelId, current } = useCurrentChannel();

const isOpen = computed(() => profile.userId !== null);
const tab = ref('overview');
const confirmBan = ref(false);

const member = useCommunityMember(channelId, () =>
  profile.userId);
const person = computed<CommunityMember | null>(() => member.data.value ?? null);
const viewer = useViewerProfile(channelId, () =>
  person.value?.internalUserId ?? null);
const viewerProfile = computed(() => viewer.data.value ?? null);
const roles = computed<RoleKey[]>(() =>
  person.value
    ? roleKeys(person.value, viewerProfile.value, current.value?.login)
    : []);
// Twitch refuses a ban or VIP on the channel's own account, so the dialog does not offer them.
const isOwner = computed<boolean>(() => roles.value.includes('broadcaster'));

// The mutations need the channel id when they are made; the dialog is mounted inside a signed-in channel.
const vipChange = useVipChange(channelId.value ?? '');
const ban = useBanMember(channelId.value ?? '');

// Twitch answers per person; null means Twitch could not be asked, so the dialog says unknown instead of guessing.
const isVip = computed<boolean | null>(() => person.value?.isVip ?? null);
const vipLabel = computed<string>(() => {
  if (isVip.value === null)
    return t('profile.vip.unknown');

  return isVip.value
    ? t('profile.vip.remove')
    : t('profile.vip.make');
});
const title = computed<string>(() => person.value?.displayName ?? t('profile.title'));
const lines = computed<ActivityLine[]>(() => person.value?.recentActivity ?? []);

watch(
  () => profile.userId,
  () => {
    tab.value = 'overview';
    confirmBan.value = false;
  },
);

function dateText(value: string): string {
  return shortDate(value, locale.value);
}

function setOpen(open: boolean): void {
  if (!open) {
    profile.close();
  }
}

function giveFocusBack(event: Event): void {
  event.preventDefault();
  profile.opener?.focus();
}

function toggleVip(): void {
  if (!profile.userId || isVip.value === null) return;

  vipChange.mutate({ userId: profile.userId, vip: !isVip.value });
}

async function banNow(): Promise<void> {
  if (!profile.userId) return;

  try {
    await ban.mutateAsync({ userId: profile.userId, reason: t('profile.ban.reason') });
  } catch {
    errors.report(SAVE_FAILED_TITLE, SAVE_FAILED_MESSAGE);
  }

  confirmBan.value = false;
}
</script>

<template>
  <Dialog :open="isOpen" @update:open="setOpen">
    <DialogContent
      data-slot="profile-dialog"
      :show-close-button="false"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 sm:max-w-3xl"
      @close-auto-focus="giveFocusBack"
    >
      <ProfileHeader :person="person" :profile="viewerProfile" :roles="roles" />
      <DialogClose as-child>
        <Button
          variant="ghost"
          size="icon-sm"
          class="absolute top-4 right-4"
          :aria-label="$t('common.close')"
        >
          <Cross />
        </Button>
      </DialogClose>

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
        <Tabs v-model="tab" class="flex min-h-0 flex-1 flex-col gap-0">
          <div class="flex-none px-6">
            <TabsList>
              <TabsTrigger v-for="name in TABS" :key="name" :value="name">{{
                $t(`profile.tabs.${name}`)
              }}</TabsTrigger>
            </TabsList>
          </div>
          <div class="grid min-h-0 flex-1 overflow-y-auto px-6 py-4">
            <TabsContent value="overview" force-mount :class="[PANEL_CLASS, 'grid gap-4']">
              <ProfileOverview
                :person="person"
                :profile="viewerProfile"
                :roles="roles"
                @show-chat="tab = 'chat'"
              />
            </TabsContent>

            <TabsContent value="chat" force-mount :class="PANEL_CLASS">
              <PanelCard
                class="h-64"
                :title="$t('profile.chat.title')"
                :state="lines.length > 0 ? 'ready' : 'empty'"
                :empty-text="$t('profile.chat.empty')"
              >
                <VirtualList
                  :items="lines"
                  :item-key="(line: ActivityLine) => `${line.timestamp}-${line.content}`"
                  :estimate-size="40"
                  :label="$t('profile.chat.title')"
                >
                  <template #default="{ item }">
                    <div class="grid gap-0.5 px-2 py-1.5 text-sm">
                      <span class="text-xs text-muted-foreground">{{
                        dateText(item.timestamp)
                      }}</span>
                      <span class="wrap-break-word">{{ item.content }}</span>
                    </div>
                  </template>
                </VirtualList>
              </PanelCard>
            </TabsContent>

            <TabsContent value="moderation" force-mount :class="PANEL_CLASS">
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
          </div>
        </Tabs>

        <DialogFooter v-if="!isOwner" class="flex-none border-t border-border px-6 py-4">
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
        </DialogFooter>
      </template>
    </DialogContent>
  </Dialog>

  <ConfirmDialog
    v-model:open="confirmBan"
    :title="$t('profile.ban.title', { name: title })"
    :description="$t('profile.ban.description', { name: title })"
    :action="$t('profile.ban.action', { name: title })"
    :pending="ban.isPending.value"
    @confirm="banNow"
  />
</template>

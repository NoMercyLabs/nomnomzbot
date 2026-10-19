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
// with nothing to show says so.
import { injectDialogRootContext } from 'reka-ui';
import { computed, nextTick, ref, useTemplateRef, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import { toast } from 'vue-sonner';
import ChevronDown from '~icons/nnz/chevron-down';

import { useChannelAccess } from '@/api/access';
import { useCurrentUser } from '@/api/auth';
import {
  useBanMember,
  useCommunityMember,
  useVipChange,
  type CommunityMember,
} from '@/api/community';
import { useShoutout, useTimeout, useUnban, useViewerProfile } from '@/api/people';
import ConfirmDialog from '@/components/layout/ConfirmDialog.vue';
import KeyValueList from '@/components/layout/KeyValueList.vue';
import type { KeyValueItem } from '@/components/layout/keyValueItem';
import ProfileHeader, { type ProfilePreview } from '@/components/layout/profile/ProfileHeader.vue';
import ProfileData from '@/components/layout/profile/ProfileData.vue';
import ProfileEmotes from '@/components/layout/profile/ProfileEmotes.vue';
import ProfileChat from '@/components/layout/profile/ProfileChat.vue';
import ProfileHistory from '@/components/layout/profile/ProfileHistory.vue';
import ProfileNotes from '@/components/layout/profile/ProfileNotes.vue';
import ProfileOverview from '@/components/layout/profile/ProfileOverview.vue';
import ProfilePoints from '@/components/layout/profile/ProfilePoints.vue';
import ProfileStanding from '@/components/layout/profile/ProfileStanding.vue';
import ProfileVoice from '@/components/layout/profile/ProfileVoice.vue';
import ProfileWarn from '@/components/layout/profile/ProfileWarn.vue';
import {
  canSanction,
  roleKeys,
  type RoleKey,
  hasNoTwitchEmotes,
  trustTier,
} from '@/components/layout/profile/profileRoles';
import { Button } from '@/components/ui/button';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { usePageView } from '@/composables/usePageView';
import { SAVE_FAILED_MESSAGE, SAVE_FAILED_TITLE } from '@/composables/useOptimisticSave';
import { merge } from '@/lib/array';

import { ALL_TABS, BASE_TABS, type ProfileTab, SETTINGS_TABS } from './profileTabs';
import { formatLength, shortDate, TIMEOUT_LENGTHS } from '@/lib/time';
import { useSaveErrorsStore } from '@/stores/saveErrors';

// preview: the name, login and avatar a list row or search hit already has, shown while the person loads.
const props = defineProps<{ userId: string; preview?: ProfilePreview | null }>();

// The header already shows every count, so no tab repeats them. Points and Data come from the bot's stored
// profile, so they show once it loaded.

// The dialog scrolls the tabs inside itself. On a page a phone scrolls the page once, never a box inside it.
const inDialog = injectDialogRootContext(null) !== null;

// Every panel stays mounted in one grid cell, so the view takes the tallest tab's size up front and never jumps.
// A phone page and the dialog scroll as a whole, so there a short tab drops the others' height instead of trailing
// empty space.
const PANEL_CLASS =
  'col-start-1 row-start-1 data-[state=inactive]:invisible max-md:data-[state=inactive]:hidden';
const fit = inDialog
  ? 'min-h-0'
  : 'md:min-h-0';
// Below md the dialog scrolls header and tabs as one, under a sticky tab strip, and keeps the actions pinned
// below: a short window (a phone, the chat popout) otherwise squeezed the tabs or pushed the actions out of reach.
// The desktop dialog keeps its full size.
// On the page the body scrolls as one from md up, so a short window never squeezes the panel under the actions.
const body = inDialog
  ? 'max-md:min-h-0 max-md:overflow-y-auto md:contents'
  : 'md:min-h-0 md:overflow-y-auto';
const inner = inDialog
  ? 'min-h-0'
  : '';
const strip = inDialog
  ? 'max-md:sticky max-md:top-0 max-md:z-10 max-md:bg-popover'
  : '';
const scroll = inDialog
  ? 'md:overflow-y-auto'
  : '';

const { t, n, locale } = useI18n();
const errors = useSaveErrorsStore();
const { channelId, current } = useCurrentChannel();

const tabStrip = useTemplateRef<HTMLElement>('tabStrip');
const confirmBan = ref(false);
const warnOpen = ref(false);

const member = useCommunityMember(channelId, () => props.userId);
const person = computed<CommunityMember | null>(() => member.data.value ?? null);
const viewer = useViewerProfile(channelId, () =>
  person.value?.internalUserId ?? null);
const viewerProfile = computed(() => viewer.data.value ?? null);
// The tier the bot resolved and the trust score it keeps: part of the bot standing, so they live in Settings.
const tier = computed(() => trustTier(viewerProfile.value));
const trustScore = computed(() => viewerProfile.value?.trust?.trustScore);
const roles = computed<RoleKey[]>(() =>
  person.value
    ? roleKeys(person.value, viewerProfile.value, current.value?.login)
    : [],
);
// Twitch refuses a ban or VIP on the channel's own account, so the view does not offer them.
const isOwner = computed<boolean>(() => roles.value.includes('broadcaster'));

// Timeout, Ban and Warn show only where Twitch accepts them for this person and the signed-in one.
const currentUser = useCurrentUser();
const sanctionable = computed<boolean>(
  () =>
    person.value !== null &&
    canSanction(currentUser.data.value?.username, current.value?.login, {
      username: person.value.username,
      roles: roles.value,
    }),
);

// The mutations need the channel id when they are made; the view is mounted inside a signed-in channel.
const vipChange = useVipChange(channelId.value ?? '');
const ban = useBanMember(channelId.value ?? '');
const timeouts = useTimeout(channelId);
const unban = useUnban(channelId);
const shoutout = useShoutout(channelId);

// Each control asks for the action key the bot's own gate checks, so a change that cannot save is never offered.
const access = useChannelAccess(channelId);
const canBan = computed(() => access.can('moderation:ban'));
const canUnban = computed(() => access.can('moderation:unban'));
const canVip = computed(() => access.can('moderation:vip'));
const canShoutout = computed(() => access.can('moderation:shoutout'));
const canWarn = computed(() => access.can('moderation:warn'));
const hasActions = computed(
  () => canShoutout.value || canVip.value || canWarn.value || canBan.value || canUnban.value,
);

// Twitch answers per person; null means Twitch could not be asked, so the view says unknown instead of guessing.
const isVip = computed<boolean | null>(() => person.value?.isVip ?? null);
const vipLabel = computed<string>(() => {
  if (isVip.value === null)
    return t('profile.vip.unknown');

  return isVip.value
    ? t('profile.vip.remove')
    : t('profile.vip.make');
});
const tabs = computed(() =>
  viewerProfile.value
    ? ALL_TABS
    : merge(BASE_TABS, SETTINGS_TABS));
// On a page the open tab is ?tab=, so a link opens it and Back returns to the last one. The dialog sits over a
// page whose query is not its own, so it keeps the tab as a model: its Open full page link carries it over.
const dialogTab = defineModel<ProfileTab>('tab', { default: 'overview' });
const pageTab = usePageView(ALL_TABS, 'tab', tabs);
const tab = inDialog
  ? dialogTab
  : pageTab;
const title = computed<string>(() => person.value?.displayName ?? t('profile.title'));

// What the bot counted of the moderation on this person, beside the bans it stored one by one.
const actions = computed<KeyValueItem[]>(() => {
  const history = viewerProfile.value?.moderationHistory;

  if (!history)
    return [];

  const last =
    history.lastActionType && history.lastActionAt
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

// A phone shows part of the last tab; choosing it slides the whole tab into view.
watch(tab, async () => {
  await nextTick();

  tabStrip.value
    ?.querySelector('[data-state="active"]')
    ?.scrollIntoView({ block: 'nearest', inline: 'nearest' });
});

// Every person opens on Overview, also when the dialog opens again: its tab outlives the view.
watch(
  () => props.userId,
  () => {
    dialogTab.value = 'overview';
    confirmBan.value = false;
    warnOpen.value = false;
  },
  { immediate: true },
);

function dateText(value: string): string {
  return shortDate(value, locale.value);
}

function toggleVip(): void {
  if (isVip.value === null) return;

  vipChange.mutate({ userId: props.userId, vip: !isVip.value });
}

async function timeoutFor(seconds: number): Promise<void> {
  const length = formatLength(seconds, locale.value);

  try {
    await timeouts.mutateAsync({ userId: props.userId, durationSeconds: seconds });
    toast.success(t('profile.timeout.done', { name: title.value, length }));
  } catch {
    toast.error(t('profile.timeout.failed', { name: title.value }));
  }
}

async function liftBan(): Promise<void> {
  try {
    await unban.mutateAsync({ userId: props.userId });
    toast.success(t('profile.unban.done', { name: title.value }));
  } catch {
    toast.error(t('profile.unban.failed', { name: title.value }));
  }
}

async function shoutOut(): Promise<void> {
  try {
    await shoutout.mutateAsync({ targetTwitchUserId: props.userId });
    toast.success(t('profile.shoutout.done', { name: title.value }));
  } catch {
    toast.error(t('profile.shoutout.failed', { name: title.value }));
  }
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
    <div data-profile-body class="flex flex-1 flex-col" :class="body">
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
        <span class="text-destructive">{{ $t("profile.error") }}</span>
        <Button variant="outline" size="sm" @click="member.refetch()">{{
          $t("common.panel.retry")
        }}</Button>
      </div>

      <Tabs v-else v-model="tab" class="flex min-w-0 flex-1 flex-col gap-0" :class="inner">
        <!-- A phone swipes the tabs sideways like the Twitch app: the cut-off last tab shows there are more. -->
        <div
          ref="tabStrip"
          class="flex-none scroll-px-6 overflow-x-auto px-6 scrollbar-none"
          :class="strip"
        >
          <TabsList>
            <TabsTrigger v-for="name in tabs" :key="name" :value="name">{{
              $t(`profile.tabs.${name}`)
            }}</TabsTrigger>
          </TabsList>
        </div>
        <!-- minmax(0,1fr): one long chat link in a panel stretched every panel in the shared cell past the edge. -->
        <div class="grid flex-1 grid-cols-[minmax(0,1fr)] px-6 py-4" :class="[inner, scroll]">
          <TabsContent value="overview" force-mount :class="PANEL_CLASS">
            <ProfileOverview
              :profile="viewerProfile"
              :pending="viewer.isPending.value"
              :owner="isOwner"
              :gifter="person?.gifted ? person.gifter : null"
            />
          </TabsContent>

          <TabsContent value="chat" force-mount :class="PANEL_CLASS">
            <ProfileChat :internal-user-id="person?.internalUserId ?? null" />
          </TabsContent>

          <TabsContent value="emotes" force-mount :class="PANEL_CLASS">
            <ProfileEmotes
              :user-id="person?.internalUserId ?? null"
              :no-twitch-emotes="hasNoTwitchEmotes(viewerProfile)"
            />
          </TabsContent>

          <!-- Flat sections like the Overview, no boxes in boxes (owner 2026-10-14: "the moderation section needs
               a better ui"). -->
          <TabsContent
            value="moderation"
            force-mount
            :class="[PANEL_CLASS, 'grid content-start gap-8']"
          >
            <ProfileNotes :user-id="userId" />
            <ProfileHistory :internal-user-id="person.internalUserId ?? null" />
            <section v-if="actions.length > 0" data-profile-actions class="grid gap-3">
              <h3 class="text-sm font-semibold">{{ $t("profile.moderation.counts") }}</h3>
              <KeyValueList :items="actions" />
            </section>
            <section data-profile-bans class="grid gap-3">
              <h3 class="text-sm font-semibold">{{ $t("profile.moderation.title") }}</h3>
              <p v-if="person.banHistory.length === 0" class="text-sm text-muted-foreground">
                {{ $t("profile.moderation.empty") }}
              </p>
              <ul v-else class="grid gap-3 text-sm">
                <li v-for="record in person.banHistory" :key="record.id" class="grid gap-0.5">
                  <span>{{ record.reason || $t("profile.moderation.noReason") }}</span>
                  <span class="text-xs text-muted-foreground">
                    {{
                      $t("profile.moderation.by", {
                        name: record.bannedBy,
                        date: dateText(record.bannedAt),
                      })
                    }}
                  </span>
                  <span
                    v-if="record.unbannedAt"
                    data-profile-ban-lifted
                    class="text-xs text-muted-foreground"
                  >{{
                    $t("profile.moderation.lifted", { date: dateText(record.unbannedAt) })
                  }}</span>
                </li>
              </ul>
            </section>
          </TabsContent>

          <template v-if="viewerProfile">
            <TabsContent value="points" force-mount :class="PANEL_CLASS">
              <ProfilePoints :profile="viewerProfile" />
            </TabsContent>

            <TabsContent value="data" force-mount :class="PANEL_CLASS">
              <ProfileData
                :profile="viewerProfile"
                :twitch-user-id="userId"
                :name="title"
                :commands-used="Number(person?.commandsUsed ?? 0)"
              />
            </TabsContent>
          </template>

          <TabsContent value="settings" force-mount :class="PANEL_CLASS">
            <!-- What the bot keeps about this person, one label column like the Overview (owner 2026-10-14). -->
            <dl
              data-profile-settings
              class="grid grid-cols-1 gap-x-4 gap-y-1 text-sm sm:grid-cols-[minmax(6rem,max-content)_minmax(0,1fr)] sm:items-center sm:gap-y-3"
            >
              <template v-if="!isOwner">
                <dt class="text-muted-foreground">{{ $t("profile.botStanding.title") }}</dt>
                <dd class="m-0 min-w-0">
                  <ProfileStanding :user-id="userId" :name="title" />
                </dd>
                <template v-if="tier">
                  <dt class="text-muted-foreground">{{ $t("profile.behavior.tier") }}</dt>
                  <dd data-profile-trust="tier" class="m-0 min-w-0 font-medium">
                    {{ $t(`profile.trustTier.${tier}`) }}
                  </dd>
                </template>
                <template v-if="trustScore !== undefined">
                  <dt class="text-muted-foreground">{{ $t("profile.behavior.trust") }}</dt>
                  <dd data-profile-trust="score" class="m-0 min-w-0 font-medium">
                    {{ n(Number(trustScore), { maximumFractionDigits: 0 }) }}
                  </dd>
                </template>
              </template>
              <dt class="text-muted-foreground">{{ $t("profile.voice.title") }}</dt>
              <dd class="m-0 min-w-0">
                <ProfileVoice :user-id="userId" :name="title" />
              </dd>
            </dl>
          </TabsContent>
        </div>
      </Tabs>
    </div>

    <!-- Every action in sight (owner 2026-10-14: "we have space to not collapse things"): every action is a
           bordered button (a ghost one read as a link), so Ban stays the one strong button in the row. -->
    <div
      v-if="person && !member.isPending.value && !isOwner && hasActions"
      class="flex flex-none flex-wrap items-center gap-2 border-t border-border px-6 py-4"
    >
      <Button
        v-if="canShoutout"
        data-profile-shoutout
        type="button"
        variant="outline"
        @click="shoutOut"
      >
        {{ $t("profile.shoutout.button") }}
      </Button>
      <Button
        v-if="canVip"
        data-profile-vip
        type="button"
        variant="outline"
        :disabled="isVip === null"
        @click="toggleVip"
      >
        {{ vipLabel }}
      </Button>
      <ProfileWarn v-model:open="warnOpen" :user-id="userId" :name="title" />

      <!-- The mod actions stay together, one look each, with Ban the one strong button (owner 2026-10-15:
             "ban timeout and warn are mod actions so why is one of them a link button?"). -->
      <div class="flex items-center gap-2 sm:ml-auto">
        <Button
          v-if="sanctionable && !person.isBanned && canWarn"
          data-profile-warn
          type="button"
          variant="outline"
          @click="warnOpen = true"
        >
          {{ $t("profile.warn.button") }}
        </Button>
        <Button
          v-if="person.isBanned && canUnban"
          data-profile-unban
          type="button"
          :disabled="unban.isPending.value"
          @click="liftBan"
        >
          {{ $t("profile.unban.button") }}
        </Button>
        <template v-if="!person.isBanned && sanctionable && canBan">
          <DropdownMenu>
            <DropdownMenuTrigger as-child>
              <Button data-profile-timeout type="button" variant="outline" class="group">
                {{ $t("profile.timeout.button") }}
                <ChevronDown
                  class="size-4 transition-transform group-data-[state=open]:rotate-180 motion-reduce:transition-none"
                  aria-hidden="true"
                />
              </Button>
            </DropdownMenuTrigger>
            <DropdownMenuContent align="end">
              <DropdownMenuLabel>{{ $t("profile.timeout.menu") }}</DropdownMenuLabel>
              <DropdownMenuItem
                v-for="seconds in TIMEOUT_LENGTHS"
                :key="seconds"
                :data-profile-timeout-length="seconds"
                @select="timeoutFor(seconds)"
              >
                {{ formatLength(seconds, locale) }}
              </DropdownMenuItem>
            </DropdownMenuContent>
          </DropdownMenu>
          <Button data-profile-ban type="button" variant="destructive" @click="confirmBan = true">
            {{ $t("profile.ban.button") }}
          </Button>
        </template>
      </div>
    </div>
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

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The one profile dialog of the frame (spec row ProfileDialog + UserName). A UserName opens it through
// the profileDialog store; a second name swaps the person. The profile itself is ProfileView, the same view
// the People page shows. Its bar (mockup profile.js pf-bar) links to that page, on the tab open here.
import { computed, ref } from 'vue';
import type { RouteLocationRaw } from 'vue-router';
import ArrowRight from '~icons/nnz/arrow-right';
import Cross from '~icons/nnz/cross';

import { useChannelAccess } from '@/api/access';
import { useCommunityMember } from '@/api/community';
import ProfilePublicView from '@/components/layout/profile/ProfilePublicView.vue';
import ProfileView from '@/components/layout/profile/ProfileView.vue';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent } from '@/components/ui/dialog';
import { useCurrentChannel } from '@/composables/useCurrentChannel';
import { useProfileDialogStore } from '@/stores/profileDialog';

const profile = useProfileDialogStore();
const { channelId } = useCurrentChannel();

const isOpen = computed(() => profile.userId !== null);
const tab = ref('overview');

// The same query the view runs, so this costs no second request; the page opens a person by login.
// A caller who may not read the community sees the public profile: no moderator page to link to, no request that 403s.
const access = useChannelAccess(channelId);
const isPublic = computed(() =>
  access.known.value && !access.can('community:read'));
const member = useCommunityMember(channelId, () =>
  access.known.value && !isPublic.value
    ? profile.userId
    : null);
const fullPage = computed<RouteLocationRaw | null>(() => {
  const login = member.data.value?.username;

  if (!login)
    return null;

  return {
    name: 'dashboard-people',
    query: {
      u: login,
      tab: tab.value === 'overview'
        ? undefined
        : tab.value,
    },
  };
});

function setOpen(open: boolean): void {
  if (!open) {
    profile.close();
  }
}

function giveFocusBack(event: Event): void {
  event.preventDefault();
  profile.opener?.focus();
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
      <div data-profile-bar class="flex flex-none items-center gap-2 pt-2 pr-2 pl-6">
        <span class="text-xs text-muted-foreground">{{ $t('profile.title') }}</span>
        <Button
          v-if="fullPage"
          data-profile-full-page
          as-child
          variant="ghost"
          size="sm"
          class="ml-auto"
        >
          <RouterLink :to="fullPage" @click="profile.close()">
            {{ $t('profile.fullPage') }}
            <ArrowRight aria-hidden="true" />
          </RouterLink>
        </Button>
        <DialogClose as-child>
          <Button
            variant="ghost"
            size="icon-sm"
            :class="!fullPage && 'ml-auto'"
            :aria-label="$t('common.close')"
          >
            <Cross />
          </Button>
        </DialogClose>
      </div>
      <template v-if="profile.userId && access.known.value">
        <ProfilePublicView v-if="isPublic" v-model:tab="tab" :user-id="profile.userId" />
        <ProfileView v-else v-model:tab="tab" :user-id="profile.userId" />
      </template>
    </DialogContent>
  </Dialog>
</template>

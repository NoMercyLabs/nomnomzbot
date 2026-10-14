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
// the People page shows.
import { computed } from 'vue';
import Cross from '~icons/nnz/cross';

import ProfileView from '@/components/layout/profile/ProfileView.vue';
import { Button } from '@/components/ui/button';
import { Dialog, DialogClose, DialogContent } from '@/components/ui/dialog';
import { useProfileDialogStore } from '@/stores/profileDialog';

const profile = useProfileDialogStore();

const isOpen = computed(() => profile.userId !== null);

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
      <ProfileView v-if="profile.userId" :user-id="profile.userId" />
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
    </DialogContent>
  </Dialog>
</template>

<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A new or rotated key shows here once. After the dialog closes the secret is gone from the page; the bot
// stores only its hash.
import CopyLinkButton from '@/components/layout/CopyLinkButton.vue';
import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';

defineProps<{ secret: string; name: string }>();

const open = defineModel<boolean>('open', { required: true });
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent
      data-dialog="token-secret"
      class="sm:max-w-130"
      :show-close-button="false"
    >
      <DialogTitle>{{ $t('dashboard.settings.apiDevices.secret.title', { name }) }}</DialogTitle>
      <DialogDescription>{{ $t('dashboard.settings.apiDevices.secret.description') }}</DialogDescription>
      <code data-secret class="block rounded-lg bg-muted p-3 font-mono text-sm break-all select-all">
        {{ secret }}
      </code>
      <DialogFooter>
        <CopyLinkButton :url="secret" :label="$t('dashboard.settings.apiDevices.secret.copy')" />
        <Button data-secret-done @click="open = false">
          {{ $t('dashboard.settings.apiDevices.secret.done') }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

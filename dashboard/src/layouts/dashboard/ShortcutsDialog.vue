<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// The list of keyboard shortcuts the page on screen offers, opened with "?" as on GitHub and Discord.
import { computed, ref } from 'vue';

import { Button } from '@/components/ui/button';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogTitle,
} from '@/components/ui/dialog';
import { Kbd, KbdGroup } from '@/components/ui/kbd';
import { keyCaps, useShortcut, useShortcutList, type Shortcut } from '@/composables/useShortcut';

const open = ref(false);
const list = useShortcutList();

// Two search fields on one page both answer "/", but the list names it once.
const shown = computed<Shortcut[]>(() =>
  [...new Map(list.value.map((shortcut) =>
    [`${shortcut.mod}${shortcut.key}${shortcut.label}`, shortcut])).values()]);

useShortcut({ key: '?', label: 'common.shortcuts.list' }, () => {
  open.value = !open.value;
});
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent data-shortcuts :show-close-button="false" class="sm:max-w-md">
      <DialogTitle>{{ $t('common.shortcuts.title') }}</DialogTitle>
      <DialogDescription>{{ $t('common.shortcuts.description') }}</DialogDescription>
      <dl class="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-4 gap-y-2 text-sm">
        <template v-for="shortcut in shown" :key="`${shortcut.mod}${shortcut.key}${shortcut.label}`">
          <dt data-shortcut-label>{{ $t(shortcut.label) }}</dt>
          <dd>
            <KbdGroup>
              <Kbd v-for="cap in keyCaps(shortcut)" :key="cap">{{ cap }}</Kbd>
            </KbdGroup>
          </dd>
        </template>
      </dl>
      <DialogFooter>
        <Button variant="outline" @click="open = false">{{ $t('common.close') }}</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>

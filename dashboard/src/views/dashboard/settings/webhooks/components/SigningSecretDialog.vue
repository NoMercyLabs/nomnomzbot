<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// Shows the signing secret of a new outbound webhook once. The bot keeps only a hash, so the secret cannot be
// read again: the dialog is an alert dialog (an outside click does not close it) and "I saved it" is the one button.
import { refAutoReset } from '@vueuse/core';
import AlertTriangle from '~icons/nnz/alert-triangle';
import Check from '~icons/nnz/check';
import Copy from '~icons/nnz/file-copy';

import {
  AlertDialog,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogTitle,
} from '@/components/ui/alert-dialog';
import { Button } from '@/components/ui/button';

const props = defineProps<{ secret: string | null }>();

const emit = defineEmits<{ done: [] }>();

const copied = refAutoReset(false, 2000);

async function copy(): Promise<void> {
  if (props.secret === null) return;

  await navigator.clipboard.writeText(props.secret);
  copied.value = true;
}
</script>

<template>
  <AlertDialog :open="secret !== null">
    <AlertDialogContent
      data-dialog="signing-secret"
      class="flex max-h-[90vh] min-h-0 flex-col gap-0 overflow-hidden p-0 data-[size=default]:sm:max-w-115"
    >
      <div class="grid flex-none gap-1.5 px-6 pt-6 pb-4">
        <AlertDialogTitle class="text-lg font-semibold">
          {{ $t("dashboard.settings.webhooks.outbound.secret.title") }}
        </AlertDialogTitle>
        <AlertDialogDescription class="text-sm text-muted-foreground">
          {{ $t("dashboard.settings.webhooks.outbound.secret.description") }}
        </AlertDialogDescription>
      </div>
      <div class="grid min-h-0 flex-1 gap-3 overflow-y-auto px-6 pb-4">
        <code
          data-signing-secret
          class="rounded-md border border-border bg-muted px-3 py-2 font-mono text-sm break-all select-all"
        >{{ secret }}</code>
        <p class="flex items-start gap-2 text-sm text-destructive">
          <AlertTriangle class="mt-0.5 size-4 flex-none" aria-hidden="true" />
          {{ $t("dashboard.settings.webhooks.outbound.secret.warning") }}
        </p>
      </div>
      <AlertDialogFooter class="flex-none border-t border-border px-6 py-4">
        <Button data-copy-secret variant="outline" @click="copy">
          <Check v-if="copied" class="size-5" aria-hidden="true" />
          <Copy v-else class="size-5" aria-hidden="true" />
          {{
            copied ? $t("common.copied") : $t("dashboard.settings.webhooks.outbound.secret.copy")
          }}
        </Button>
        <Button data-secret-done @click="emit('done')">
          {{ $t("dashboard.settings.webhooks.outbound.secret.done") }}
        </Button>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>

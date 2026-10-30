<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot. You may use, change and share it for free,
  for any noncommercial purpose, under the PolyForm Noncommercial License
  1.0.0. You may not earn money with it. Distributed WITHOUT ANY WARRANTY.
  See LICENSE for details, and TRADEMARKS.md for the name and logo.

  SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
-->
<script setup lang="ts">
// While an admin acts as someone, everything on screen is that person's. This strip is the one trace of the
// admin: the Exit. Leaving, or the session running out, brings the admin back to the page they left.
import { ref, watch } from 'vue';
import { useI18n } from 'vue-i18n';
import DoorOut from '~icons/nnz/door-out';

import { useCurrentUser } from '@/api/auth';
import { Button } from '@/components/ui/button';
import { useActAsExit } from '@/composables/useActAs';
import { errorText } from '@/lib/errors';

const { t } = useI18n();
const { acting, exit, returnToAdmin } = useActAsExit();
const { data: user } = useCurrentUser();

const leaving = ref(false);
const failure = ref('');

async function leave(): Promise<void> {
  leaving.value = true;
  failure.value = '';

  try {
    await exit();
  } catch (error) {
    failure.value = errorText(error, t);
  } finally {
    leaving.value = false;
  }
}

watch(acting, (now: boolean, before: boolean) => {
  if (before && !now) {
    void returnToAdmin();
  }
});
</script>

<template>
  <div
    v-if="acting"
    data-slot="act-as-bar"
    role="status"
    class="flex flex-none flex-wrap items-center gap-2 border-b border-border bg-muted px-3 py-1.5 text-sm"
  >
    <span class="min-w-0 flex-1 truncate">
      {{ t('frame.actAs.banner', { name: user?.displayName ?? '' }) }}
    </span>
    <span v-if="failure" role="alert" class="text-destructive">{{ failure }}</span>
    <Button
      variant="outline"
      size="sm"
      data-slot="act-as-exit"
      :disabled="leaving"
      @click="leave"
    >
      <DoorOut aria-hidden="true" />
      {{ t('frame.actAs.exit') }}
    </Button>
  </div>
</template>

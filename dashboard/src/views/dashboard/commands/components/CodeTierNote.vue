<!--
  Copyright (c) NoMercy Labs.

  This file is part of NomNomzBot, free software licensed under the GNU Affero
  General Public License v3.0 or later. You may redistribute and/or modify it
  under those terms. Distributed WITHOUT ANY WARRANTY. See LICENSE for details.

  SPDX-License-Identifier: AGPL-3.0-or-later
-->
<script setup lang="ts">
// A code command has no field to edit here: no endpoint links a script to a command yet.
import { useCodeScripts } from '@/api/codeScripts'

const scripts = useCodeScripts()
</script>

<template>
  <div class="grid gap-1 text-sm">
    <p class="text-muted-foreground">{{ $t('dashboard.commands.editor.codeNote') }}</p>
    <p v-if="scripts.isError.value" role="alert" class="text-destructive">
      {{ $t('dashboard.commands.editor.codeError', { message: scripts.error.value?.message ?? '' }) }}
    </p>
  </div>
</template>

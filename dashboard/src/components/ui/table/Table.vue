<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, provide } from 'vue'
import { cn } from '@/lib/utils'
import { tableStackedKey } from './stacked'

const props = defineProps<{
  class?: HTMLAttributes['class']
  stacked?: boolean
}>()

provide(tableStackedKey, computed(() => props.stacked === true))
</script>

<template>
  <div data-slot="table-container" :class="cn('relative w-full overflow-x-auto', stacked && 'max-md:overflow-visible')">
    <table data-slot="table" :class="cn('w-full caption-bottom text-sm', stacked && 'max-md:block', props.class)">
      <slot />
    </table>
  </div>
</template>

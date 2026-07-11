<script setup lang="ts">
import { computed } from 'vue'
import type { UsageTotalsDto } from '@/types/stats'
import { formatBytes, formatCpu, memoryPercent } from '@/utils/format'

const props = defineProps<{ totals: UsageTotalsDto }>()

const memPercent = computed(() =>
  memoryPercent(props.totals.memoryUsedBytes, props.totals.memoryLimitBytes),
)
</script>

<template>
  <div class="d-flex flex-wrap ga-6 align-center">
    <div class="metric">
      <div class="text-caption text-medium-emphasis">CPU</div>
      <div class="text-h6">{{ formatCpu(totals.cpuPercent) }}</div>
    </div>
    <div class="metric">
      <div class="text-caption text-medium-emphasis">Memory</div>
      <div class="text-h6">
        {{ formatBytes(totals.memoryUsedBytes) }}
        <span v-if="totals.memoryLimitBytes > 0" class="text-caption text-medium-emphasis">
          / {{ formatBytes(totals.memoryLimitBytes) }} ({{ memPercent.toFixed(0) }}%)
        </span>
      </div>
    </div>
    <div class="metric">
      <div class="text-caption text-medium-emphasis">Disk</div>
      <div class="text-h6">{{ formatBytes(totals.diskBytes) }}</div>
    </div>
    <div v-if="totals.objectStorageBytes > 0" class="metric">
      <div class="text-caption text-medium-emphasis">Object storage</div>
      <div class="text-h6">{{ formatBytes(totals.objectStorageBytes) }}</div>
    </div>
  </div>
</template>

<style scoped>
.metric {
  min-width: 90px;
}
</style>

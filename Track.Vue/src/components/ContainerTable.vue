<script setup lang="ts">
import type { ContainerStatsDto } from '@/types/stats'
import { formatBytes, formatCpu, memoryPercent } from '@/utils/format'

defineProps<{ containers: ContainerStatsDto[] }>()
</script>

<template>
  <v-table density="compact" class="container-table">
    <thead>
      <tr>
        <th>Container</th>
        <th>Service</th>
        <th>Node</th>
        <th class="text-right">CPU</th>
        <th class="text-right">Memory</th>
        <th class="text-right">Disk</th>
      </tr>
    </thead>
    <tbody>
      <tr v-for="c in containers" :key="c.nodeName + c.containerId">
        <td class="text-truncate" style="max-width: 260px">{{ c.containerName }}</td>
        <td class="text-medium-emphasis">{{ c.serviceName ?? '—' }}</td>
        <td class="text-medium-emphasis">{{ c.nodeName }}</td>
        <td class="text-right">{{ formatCpu(c.cpuPercent) }}</td>
        <td class="text-right">
          {{ formatBytes(c.memoryUsedBytes) }}
          <span v-if="c.memoryLimitBytes > 0" class="text-caption text-medium-emphasis">
            ({{ memoryPercent(c.memoryUsedBytes, c.memoryLimitBytes).toFixed(0) }}%)
          </span>
        </td>
        <td class="text-right">{{ formatBytes(c.diskBytes) }}</td>
      </tr>
    </tbody>
  </v-table>
</template>

<style scoped>
.container-table :deep(th) {
  font-weight: 600;
}
</style>

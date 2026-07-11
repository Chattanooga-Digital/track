<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { formatBytes } from '@/utils/format'

const snapshotStore = useSnapshotStore()
const { snapshot, customers } = storeToRefs(snapshotStore)

const cluster = computed(() => snapshot.value?.cluster)
const total = computed(() => snapshot.value?.totals)

// Rank customers by their blended cluster share, the billing figure.
const rows = computed(() =>
  [...customers.value]
    .filter((c) => c.customerId !== null)
    .sort((a, b) => b.totals.blendedPercentOfCluster - a.totals.blendedPercentOfCluster),
)

const unassigned = computed(() => customers.value.find((c) => c.customerId === null))

function pct(value: number): string {
  return `${value.toFixed(2)}%`
}
</script>

<template>
  <v-container fluid>
    <h1 class="text-h5 mb-4">Billing &mdash; Blended Cluster Utilization</h1>

    <v-row class="mb-2">
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Cluster capacity</div>
            <div class="text-h6">
              {{ cluster?.cpuCores ?? 0 }} vCPU &middot; {{ formatBytes(cluster?.memoryBytes) }}
            </div>
            <div class="text-caption text-medium-emphasis">
              {{ cluster?.nodeCount ?? 0 }} node(s)
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Total blended utilization</div>
            <div class="text-h4">{{ pct(total?.blendedPercentOfCluster ?? 0) }}</div>
            <div class="text-caption text-medium-emphasis">
              CPU {{ pct(total?.cpuPercentOfCluster ?? 0) }} &middot; Mem
              {{ pct(total?.memoryPercentOfCluster ?? 0) }}
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="12" md="4">
        <v-card variant="outlined">
          <v-card-text>
            <div class="text-caption text-medium-emphasis">Unassigned (unbilled)</div>
            <div class="text-h4">{{ pct(unassigned?.totals.blendedPercentOfCluster ?? 0) }}</div>
            <div class="text-caption text-medium-emphasis">
              {{ unassigned?.stacks.length ?? 0 }} stack(s) not mapped to a customer
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-card variant="outlined">
      <v-table>
        <thead>
          <tr>
            <th>Customer</th>
            <th class="text-right">Blended %</th>
            <th class="text-right">CPU %</th>
            <th class="text-right">Memory %</th>
            <th class="text-right">CPU</th>
            <th class="text-right">Memory</th>
            <th class="text-right">Disk</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="c in rows" :key="c.customerId ?? 'none'">
            <td class="font-weight-medium">{{ c.customerName }}</td>
            <td class="text-right font-weight-bold">{{ pct(c.totals.blendedPercentOfCluster) }}</td>
            <td class="text-right">{{ pct(c.totals.cpuPercentOfCluster) }}</td>
            <td class="text-right">{{ pct(c.totals.memoryPercentOfCluster) }}</td>
            <td class="text-right">{{ c.totals.cpuPercent.toFixed(1) }}%</td>
            <td class="text-right">{{ formatBytes(c.totals.memoryUsedBytes) }}</td>
            <td class="text-right">{{ formatBytes(c.totals.diskBytes) }}</td>
          </tr>
          <tr v-if="rows.length === 0">
            <td colspan="7" class="text-center text-medium-emphasis py-6">
              No customers yet. Assign stacks to customers to see billing shares.
            </td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <p class="text-caption text-medium-emphasis mt-3">
      Blended utilization is the mean of a customer's CPU share and memory share of total cluster
      capacity, measured at the latest poll. Use the Stacks page for historical trends.
    </p>
  </v-container>
</template>

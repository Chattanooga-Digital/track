<script setup lang="ts">
import { ref, computed } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import UsageTotals from '@/components/UsageTotals.vue'
import ContainerTable from '@/components/ContainerTable.vue'
import { formatBytes } from '@/utils/format'

const snapshotStore = useSnapshotStore()
const { customers, nodes, snapshot } = storeToRefs(snapshotStore)

const cluster = computed(() => snapshot.value?.cluster)
const total = computed(() => snapshot.value?.totals)

// Track which stacks are expanded to show their container table.
const expandedStacks = ref<Set<string>>(new Set())

function stackKey(customerId: number | null, stackName: string | null): string {
  return `${customerId ?? 'none'}::${stackName ?? 'none'}`
}

function toggleStack(key: string) {
  if (expandedStacks.value.has(key)) {
    expandedStacks.value.delete(key)
  } else {
    expandedStacks.value.add(key)
  }
  // Reassign to trigger reactivity on the Set.
  expandedStacks.value = new Set(expandedStacks.value)
}
</script>

<template>
  <v-container fluid>
    <div class="d-flex align-center mb-4 ga-4 flex-wrap">
      <h1 class="text-h5">Dashboard</h1>
      <v-spacer />
      <v-chip
        v-for="node in nodes"
        :key="node.name"
        size="small"
        :color="node.online ? 'success' : 'error'"
        variant="tonal"
      >
        <v-icon start icon="mdi-server" />
        {{ node.name }}
      </v-chip>
      <span v-if="snapshot" class="text-caption text-medium-emphasis">
        Updated {{ new Date(snapshot.timestamp).toLocaleTimeString() }}
      </span>
    </div>

    <v-row v-if="cluster" class="mb-2">
      <v-col cols="6" md="3">
        <v-card variant="tonal" color="primary">
          <v-card-text class="py-3">
            <div class="text-caption">Blended cluster load</div>
            <div class="text-h5">{{ (total?.blendedPercentOfCluster ?? 0).toFixed(1) }}%</div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="6" md="3">
        <v-card variant="tonal">
          <v-card-text class="py-3">
            <div class="text-caption text-medium-emphasis">CPU used</div>
            <div class="text-h5">{{ (total?.cpuPercentOfCluster ?? 0).toFixed(1) }}%</div>
            <div class="text-caption text-medium-emphasis">of {{ cluster.cpuCores }} vCPU</div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="6" md="3">
        <v-card variant="tonal">
          <v-card-text class="py-3">
            <div class="text-caption text-medium-emphasis">Memory used</div>
            <div class="text-h5">{{ (total?.memoryPercentOfCluster ?? 0).toFixed(1) }}%</div>
            <div class="text-caption text-medium-emphasis">
              of {{ formatBytes(cluster.memoryBytes) }}
            </div>
          </v-card-text>
        </v-card>
      </v-col>
      <v-col cols="6" md="3">
        <v-card variant="tonal">
          <v-card-text class="py-3">
            <div class="text-caption text-medium-emphasis">Disk used</div>
            <div class="text-h5">{{ formatBytes(total?.diskBytes) }}</div>
            <div class="text-caption text-medium-emphasis">
              {{ cluster.nodeCount }} node(s)<template v-if="(total?.objectStorageBytes ?? 0) > 0">
                · {{ formatBytes(total?.objectStorageBytes) }} object storage</template
              >
            </div>
          </v-card-text>
        </v-card>
      </v-col>
    </v-row>

    <v-alert v-if="customers.length === 0" type="info" variant="tonal">
      Waiting for the first poll to report container stats...
    </v-alert>

    <v-card
      v-for="group in customers"
      :key="group.customerId ?? 'unassigned'"
      class="mb-4"
      variant="outlined"
    >
      <v-card-title class="d-flex align-center ga-2">
        <v-icon :icon="group.customerId ? 'mdi-account-group' : 'mdi-help-circle-outline'" />
        <span>{{ group.customerName ?? 'Unassigned stacks' }}</span>
        <v-chip size="x-small" variant="tonal">{{ group.stacks.length }} stacks</v-chip>
      </v-card-title>
      <v-card-text>
        <UsageTotals :totals="group.totals" class="mb-4" />
        <v-divider class="mb-2" />

        <div
          v-for="stack in group.stacks"
          :key="stackKey(group.customerId, stack.stackName)"
          class="stack-row"
        >
          <div
            class="d-flex align-center ga-3 py-2 stack-header"
            @click="toggleStack(stackKey(group.customerId, stack.stackName))"
          >
            <v-icon
              :icon="
                expandedStacks.has(stackKey(group.customerId, stack.stackName))
                  ? 'mdi-chevron-down'
                  : 'mdi-chevron-right'
              "
            />
            <v-icon icon="mdi-layers" size="small" />
            <span class="font-weight-medium">{{ stack.stackName ?? 'No stack' }}</span>
            <v-chip size="x-small" variant="tonal">{{ stack.containers.length }}</v-chip>
            <v-spacer />
            <UsageTotals :totals="stack.totals" />
          </div>
          <v-expand-transition>
            <div
              v-if="expandedStacks.has(stackKey(group.customerId, stack.stackName))"
              class="pb-3"
            >
              <ContainerTable :containers="stack.containers" />
            </div>
          </v-expand-transition>
          <v-divider />
        </div>

        <div
          v-for="bucket in group.buckets"
          :key="`${group.customerId}::${bucket.sourceName}/${bucket.bucketName}`"
          class="d-flex align-center ga-3 py-2 bucket-row"
        >
          <v-icon icon="mdi-database" size="small" class="ms-8" />
          <span class="font-weight-medium">{{ bucket.bucketName }}</span>
          <span class="text-caption text-medium-emphasis">{{ bucket.sourceName }}</span>
          <v-chip v-if="bucket.objectCount > 0" size="x-small" variant="tonal">
            {{ bucket.objectCount.toLocaleString() }} objects
          </v-chip>
          <v-spacer />
          <div class="metric text-right">
            <div class="text-caption text-medium-emphasis">Object storage</div>
            <div class="text-h6">{{ formatBytes(bucket.sizeBytes) }}</div>
          </div>
          <v-divider />
        </div>
      </v-card-text>
    </v-card>
  </v-container>
</template>

<style scoped>
.stack-header {
  cursor: pointer;
}
.stack-header:hover {
  background: rgba(255, 255, 255, 0.03);
}
</style>

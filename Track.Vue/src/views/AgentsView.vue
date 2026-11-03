<script setup lang="ts">
import { computed, ref } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import { deleteAgent } from '@/api/agentsApi'
import { formatBytes, formatCpu, memoryPercent } from '@/utils/format'
import ConfirmDialog from '@/components/ConfirmDialog.vue'

const snapshotStore = useSnapshotStore()
const { nodes, snapshot } = storeToRefs(snapshotStore)
const centralVersion = computed(() => snapshot.value?.centralVersion)

const deleteTarget = ref<string | null>(null)
const deleting = ref(false)

async function confirmDelete() {
  if (!deleteTarget.value) return
  deleting.value = true
  try {
    // The server rebroadcasts the snapshot, so the row disappears once the live update arrives.
    await deleteAgent(deleteTarget.value)
    deleteTarget.value = null
  } finally {
    deleting.value = false
  }
}

const sortedNodes = computed(() => [...nodes.value].sort((a, b) => a.name.localeCompare(b.name)))

const onlineCount = computed(() => nodes.value.filter((n) => n.online).length)

function relativeTime(iso: string | null): string {
  if (!iso) return 'never'
  const seconds = Math.round((Date.now() - new Date(iso).getTime()) / 1000)
  if (seconds < 5) return 'just now'
  if (seconds < 60) return `${seconds}s ago`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ago`
  const hours = Math.floor(minutes / 60)
  if (hours < 24) return `${hours}h ago`
  return `${Math.floor(hours / 24)}d ago`
}

function absoluteTime(iso: string | null): string {
  return iso ? new Date(iso).toLocaleString() : '—'
}
</script>

<template>
  <div class="track-page">
    <div class="d-flex align-center mb-4 ga-3 flex-wrap">
      <h1 class="text-h5">Agents</h1>
      <v-chip
        size="small"
        :color="onlineCount === nodes.length ? 'success' : 'warning'"
        variant="flat"
      >
        {{ onlineCount }} / {{ nodes.length }} online
      </v-chip>
      <v-spacer />
      <v-chip size="small" :color="snapshotStore.connected ? 'success' : 'warning'" variant="tonal">
        <v-icon start :icon="snapshotStore.connected ? 'mdi-lan-connect' : 'mdi-lan-disconnect'" />
        {{ snapshotStore.connected ? 'Live' : 'Reconnecting' }}
      </v-chip>
    </div>

    <v-card variant="outlined">
      <v-table density="comfortable">
        <thead>
          <tr>
            <th>Agent</th>
            <th>Status</th>
            <th>Last seen</th>
            <th>Version</th>
            <th class="text-right">Containers</th>
            <th class="text-right">Unidentified</th>
            <th class="text-right">CPU cores</th>
            <th class="text-right">CPU used</th>
            <th class="text-right">Memory</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="node in sortedNodes" :key="node.name">
            <td class="font-weight-medium">
              <v-icon icon="mdi-server" size="small" class="mr-1 text-medium-emphasis" />
              {{ node.name }}
            </td>
            <td>
              <v-chip :color="node.online ? 'success' : 'error'" size="small" variant="tonal">
                <v-icon start :icon="node.online ? 'mdi-check-circle' : 'mdi-alert-circle'" />
                {{ node.online ? 'Online' : 'Offline' }}
              </v-chip>
              <span v-if="!node.online && node.error" class="text-caption text-error ml-2">
                {{ node.error }}
              </span>
            </td>
            <td>
              <span :title="absoluteTime(node.lastSeen)">{{ relativeTime(node.lastSeen) }}</span>
            </td>
            <td class="text-no-wrap">
              {{ node.agentVersion ?? 'Unknown' }}
              <v-chip
                v-if="node.agentVersion !== centralVersion"
                size="x-small"
                color="warning"
                variant="tonal"
                class="ml-1"
                >{{ node.agentVersion ? 'Updating' : 'Old agent' }}</v-chip
              >
            </td>
            <td class="text-right">{{ node.containersTracked }}</td>
            <td class="text-right">
              <span v-if="node.unidentifiedContainers > 0" class="text-warning">
                {{ node.unidentifiedContainers }}
              </span>
              <span v-else class="text-medium-emphasis">0</span>
            </td>
            <td class="text-right">{{ node.cpuCores || '—' }}</td>
            <td class="text-right">
              {{ formatCpu(node.cpuPercent) }}
              <span v-if="node.cpuCores > 0" class="text-caption text-medium-emphasis">
                ({{ (node.cpuPercent / node.cpuCores).toFixed(0) }}% of node)
              </span>
            </td>
            <td class="text-right">
              {{ formatBytes(node.memoryUsedBytes) }} / {{ formatBytes(node.memoryBytes) }}
              <span v-if="node.memoryBytes > 0" class="text-caption text-medium-emphasis">
                ({{ memoryPercent(node.memoryUsedBytes, node.memoryBytes).toFixed(0) }}%)
              </span>
            </td>
            <td class="text-right">
              <v-btn
                v-if="!node.online"
                icon="mdi-delete"
                size="x-small"
                variant="text"
                color="error"
                aria-label="Delete agent"
                @click="deleteTarget = node.name"
              />
            </td>
          </tr>
          <tr v-if="nodes.length === 0">
            <td colspan="10" class="text-center text-medium-emphasis py-6">No agents yet.</td>
          </tr>
        </tbody>
      </v-table>
    </v-card>

    <ConfirmDialog
      :model-value="!!deleteTarget"
      title="Delete agent"
      :busy="deleting"
      @update:model-value="deleteTarget = null"
      @confirm="confirmDelete"
    >
      Delete <strong>{{ deleteTarget }}</strong
      >? It comes back if it reports again.
    </ConfirmDialog>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type { BackupJobDto } from '@/types/backups'
import { cancelBackupJob, getBackupJob, getBackupJobs } from '@/api/backupsApi'
import { absoluteTime, formatBytes, relativeTime } from '@/utils/format'

const { backupsVersion } = storeToRefs(useSnapshotStore())

const jobs = ref<BackupJobDto[]>([])
const loading = ref(true)
const detail = ref<BackupJobDto | null>(null)
const cancelling = ref<number | null>(null)

async function load() {
  try {
    jobs.value = await getBackupJobs()
  } finally {
    loading.value = false
  }
  if (detail.value) detail.value = await getBackupJob(detail.value.id)
}

onMounted(load)
watch(backupsVersion, load)

async function openDetail(job: BackupJobDto) {
  detail.value = await getBackupJob(job.id)
}

async function cancel(job: BackupJobDto) {
  cancelling.value = job.id
  try {
    await cancelBackupJob(job.id)
    await load()
  } finally {
    cancelling.value = null
  }
}

const active = (job: BackupJobDto) => ['Queued', 'Dispatched', 'Running'].includes(job.status)

const kindLabel: Record<string, string> = {
  Backup: 'Backup',
  Restore: 'Restore',
  Hook: 'Hook',
  Scale: 'Scale',
  Prune: 'Prune',
  Check: 'Check',
  Replace: 'Replace live data',
}

function statusColor(job: BackupJobDto) {
  switch (job.status) {
    case 'Succeeded':
      return 'success'
    case 'Failed':
      return 'error'
    case 'Cancelled':
      return undefined
    default:
      return 'info'
  }
}

function duration(job: BackupJobDto) {
  if (!job.startedAt) return ''
  const end = job.finishedAt ? new Date(job.finishedAt).getTime() : Date.now()
  const seconds = Math.max(0, Math.round((end - new Date(job.startedAt).getTime()) / 1000))
  if (seconds < 60) return `${seconds}s`
  const minutes = Math.floor(seconds / 60)
  if (minutes < 60) return `${minutes}m ${seconds % 60}s`
  return `${Math.floor(minutes / 60)}h ${minutes % 60}m`
}
</script>

<template>
  <v-progress-linear v-if="loading" indeterminate />
  <v-card variant="outlined">
    <div class="table-scroll">
      <v-table density="comfortable" hover>
        <thead>
          <tr>
            <th>Job</th>
            <th>Node</th>
            <th>Queued</th>
            <th>Took</th>
            <th>Result</th>
            <th></th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="job in jobs" :key="job.id" class="job-row" @click="openDetail(job)">
            <td :class="{ 'pl-8': job.parentJobId }">
              <div class="font-weight-medium">
                {{ job.stepName ?? kindLabel[job.kind] }}
                <span v-if="job.trigger === 'Manual'" class="text-caption text-medium-emphasis"
                  >· {{ job.requestedBy ?? 'manual' }}</span
                >
              </div>
              <div class="text-caption text-medium-emphasis">
                {{ job.volumeName ?? job.repositoryKey }}
              </div>
            </td>
            <td>
              {{ job.nodeName ?? (job.kind === 'Prune' || job.kind === 'Check' ? 'central' : '') }}
            </td>
            <td :title="absoluteTime(job.queuedAt)">{{ relativeTime(job.queuedAt) }}</td>
            <td>{{ duration(job) }}</td>
            <td>
              <v-chip size="small" :color="statusColor(job)" variant="tonal">
                {{
                  job.status === 'Running' && job.percentDone != null
                    ? `Running ${Math.round(job.percentDone * 100)}%`
                    : job.status
                }}
              </v-chip>
              <span
                v-if="job.status === 'Succeeded' && job.bytesAdded !== null"
                class="text-caption text-medium-emphasis ml-2"
                >+{{ formatBytes(job.bytesAdded) }}</span
              >
              <div v-if="job.error && job.status !== 'Cancelled'" class="text-caption text-error">
                {{ job.error }}
              </div>
            </td>
            <td class="text-right">
              <v-btn
                v-if="active(job) && !job.cancelRequested"
                icon="mdi-close"
                size="small"
                variant="text"
                title="Cancel"
                :loading="cancelling === job.id"
                @click.stop="cancel(job)"
              />
            </td>
          </tr>
          <tr v-if="!loading && jobs.length === 0">
            <td colspan="6" class="text-center text-medium-emphasis py-6">No jobs yet.</td>
          </tr>
        </tbody>
      </v-table>
    </div>
  </v-card>

  <v-dialog :model-value="detail !== null" max-width="760" @update:model-value="detail = null">
    <v-card v-if="detail">
      <v-card-title>{{ detail.stepName ?? kindLabel[detail.kind] }} #{{ detail.id }}</v-card-title>
      <v-card-subtitle>
        {{
          [detail.volumeName ?? detail.repositoryKey, detail.nodeName, detail.status]
            .filter(Boolean)
            .join(' · ')
        }}
      </v-card-subtitle>
      <v-card-text>
        <v-alert v-if="detail.error" type="error" variant="tonal" density="compact" class="mb-3">
          {{ detail.error }}
        </v-alert>
        <div v-if="detail.snapshotId" class="text-body-2 mb-3">
          Snapshot {{ detail.snapshotId.slice(0, 8) }}
          <template v-if="detail.bytesAdded !== null">
            · {{ formatBytes(detail.bytesAdded) }} added</template
          >
          <template v-if="detail.filesNew !== null">
            · {{ detail.filesNew }} new, {{ detail.filesChanged ?? 0 }} changed files</template
          >
        </div>
        <pre v-if="detail.output" class="output">{{ detail.output }}</pre>
        <div v-else class="text-medium-emphasis">No output.</div>
      </v-card-text>
      <v-card-actions>
        <v-spacer />
        <v-btn variant="text" @click="detail = null">Close</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
.table-scroll {
  overflow-x: auto;
}

.job-row {
  cursor: pointer;
}

.output {
  max-height: 420px;
  overflow: auto;
  padding: 12px;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  background: rgba(var(--v-theme-on-surface), 0.05);
  border-radius: 4px;
}
</style>

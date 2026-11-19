<script setup lang="ts">
import { onMounted, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type { BackupJobDto } from '@/types/backups'
import { cancelBackupJob, getBackupJobs } from '@/api/backupsApi'
import { isJobActive as active, jobKindLabel as kindLabel } from '@/utils/backupJobs'
import BackupJobDialog from '@/components/BackupJobDialog.vue'
import { absoluteTime, formatBytes, relativeTime } from '@/utils/format'

const { backupsVersion } = storeToRefs(useSnapshotStore())

const jobs = ref<BackupJobDto[]>([])
const loading = ref(true)
const detailId = ref<number | null>(null)
const cancelling = ref<number | null>(null)

async function load() {
  try {
    jobs.value = await getBackupJobs()
  } finally {
    loading.value = false
  }
}

onMounted(load)
watch(backupsVersion, load)

function openDetail(job: BackupJobDto) {
  detailId.value = job.id
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
              <div
                v-else-if="job.status === 'Succeeded' && job.summary"
                class="text-caption text-medium-emphasis"
              >
                {{ job.summary }}
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

  <BackupJobDialog v-model:job-id="detailId" />
</template>

<style scoped>
.table-scroll {
  overflow-x: auto;
}

.job-row {
  cursor: pointer;
}
</style>

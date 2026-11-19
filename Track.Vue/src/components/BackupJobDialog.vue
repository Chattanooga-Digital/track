<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { useSnapshotStore } from '@/stores/snapshotStore'
import type { BackupJobDto } from '@/types/backups'
import { cancelBackupJob, getBackupJob } from '@/api/backupsApi'
import { formatBytes } from '@/utils/format'
import { isJobActive, jobKindLabel } from '@/utils/backupJobs'
import JobLog from '@/components/JobLog.vue'

const props = defineProps<{ jobId: number | null }>()

const emit = defineEmits<{ 'update:jobId': [value: number | null] }>()

const { backupsVersion } = storeToRefs(useSnapshotStore())
const job = ref<BackupJobDto | null>(null)
const cancelling = ref(false)

const open = computed({
  get: () => props.jobId !== null,
  set: (value) => {
    if (!value) emit('update:jobId', null)
  },
})

async function load() {
  job.value = props.jobId === null ? null : await getBackupJob(props.jobId)
}

watch(() => props.jobId, load, { immediate: true })
watch(backupsVersion, () => {
  if (props.jobId !== null) void load()
})

const active = computed(() => !!job.value && isJobActive(job.value))
const live = computed(() => !!job.value?.logSessionId && (active.value || !job.value.output))

async function cancel() {
  if (!job.value) return
  cancelling.value = true
  try {
    await cancelBackupJob(job.value.id)
    await load()
  } finally {
    cancelling.value = false
  }
}
</script>

<template>
  <v-dialog v-model="open" max-width="820">
    <v-card v-if="job">
      <v-card-title class="d-flex align-center">
        <span>{{ job.stepName ?? jobKindLabel[job.kind] }} #{{ job.id }}</span>
        <v-spacer />
        <v-btn icon="mdi-close" variant="text" size="small" @click="open = false" />
      </v-card-title>
      <v-card-subtitle class="mt-n2">
        {{
          [job.volumeName ?? job.repositoryKey, job.nodeName, job.status]
            .filter(Boolean)
            .join(' · ')
        }}
      </v-card-subtitle>
      <v-card-text>
        <v-alert v-if="job.error" type="error" variant="tonal" density="compact" class="mb-3">
          {{ job.error }}
        </v-alert>
        <div v-if="job.summary" class="text-body-2 mb-3">{{ job.summary }}</div>
        <div v-if="job.snapshotId" class="text-body-2 mb-3">
          Snapshot {{ job.snapshotId.slice(0, 8) }}
          <template v-if="job.bytesAdded !== null">
            · {{ formatBytes(job.bytesAdded) }} added</template
          >
          <template v-if="job.filesNew !== null">
            · {{ job.filesNew }} new, {{ job.filesChanged ?? 0 }} changed files</template
          >
        </div>
        <JobLog v-if="live && job.logSessionId" :session-id="job.logSessionId" />
        <pre v-else-if="job.output" class="output">{{ job.output }}</pre>
        <div v-else class="text-medium-emphasis">No output.</div>
      </v-card-text>
      <v-card-actions v-if="active && !job.cancelRequested">
        <v-spacer />
        <v-btn variant="text" color="error" :loading="cancelling" @click="cancel">Cancel job</v-btn>
      </v-card-actions>
    </v-card>
  </v-dialog>
</template>

<style scoped>
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

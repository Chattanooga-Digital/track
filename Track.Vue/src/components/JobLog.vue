<script setup lang="ts">
import { nextTick, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'

const props = defineProps<{ sessionId: string }>()

interface Line {
  message: string
  level: number
}

const lines = ref<Line[]>([])
const complete = ref(false)
const error = ref('')
const box = ref<HTMLElement | null>(null)
let connection: HubConnection | null = null
let following = true

function onScroll() {
  const el = box.value
  if (el) following = el.scrollHeight - el.scrollTop - el.clientHeight < 24
}

async function follow() {
  if (!following) return
  await nextTick()
  box.value?.scrollTo({ top: box.value.scrollHeight })
}

async function start() {
  lines.value = []
  complete.value = false
  error.value = ''
  following = true
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/job-log')
    .withAutomaticReconnect()
    .configureLogging(LogLevel.Warning)
    .build()
  connection.on('LogMessage', (message: string, level: number) => {
    lines.value.push({ message, level })
    void follow()
  })
  connection.on('SessionComplete', () => (complete.value = true))
  connection.onreconnected(async () => {
    lines.value = []
    await connection?.invoke('Watch', props.sessionId)
  })
  try {
    await connection.start()
    await connection.invoke('Watch', props.sessionId)
  } catch (e) {
    error.value = e instanceof Error ? e.message : String(e)
  }
}

async function stop() {
  if (connection && connection.state !== HubConnectionState.Disconnected) await connection.stop()
  connection = null
}

onMounted(start)
onBeforeUnmount(stop)
watch(
  () => props.sessionId,
  async () => {
    await stop()
    await start()
  },
)

const levelClass = (level: number) =>
  level >= 4 ? 'text-error' : level === 3 ? 'text-warning' : undefined
</script>

<template>
  <v-alert v-if="error" type="error" variant="tonal" density="compact">
    Couldn’t open the live log: {{ error }}
  </v-alert>
  <div v-else ref="box" class="log" @scroll="onScroll">
    <div v-for="(line, i) in lines" :key="i" :class="levelClass(line.level)">
      {{ line.message }}
    </div>
    <div v-if="lines.length === 0" class="text-medium-emphasis">
      {{ complete ? 'No output.' : 'Waiting for output…' }}
    </div>
  </div>
</template>

<style scoped>
.log {
  max-height: 420px;
  min-height: 120px;
  overflow: auto;
  padding: 12px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
  line-height: 1.5;
  white-space: pre-wrap;
  word-break: break-word;
  background: rgba(var(--v-theme-on-surface), 0.05);
  border-radius: 4px;
}
</style>

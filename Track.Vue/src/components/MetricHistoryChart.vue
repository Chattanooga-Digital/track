<script setup lang="ts">
import { computed } from 'vue'
import { Line } from 'vue-chartjs'
import {
  Chart as ChartJS,
  Title,
  Tooltip,
  Legend,
  LineElement,
  LinearScale,
  PointElement,
  TimeSeriesScale,
  CategoryScale,
  Filler,
} from 'chart.js'
import type { HistoryBucketDto } from '@/types/stats'
import { formatBytes } from '@/utils/format'

ChartJS.register(
  Title,
  Tooltip,
  Legend,
  LineElement,
  LinearScale,
  PointElement,
  TimeSeriesScale,
  CategoryScale,
  Filler,
)

const props = defineProps<{
  buckets: HistoryBucketDto[]
  metric: 'cpu' | 'memory' | 'disk'
}>()

const labels = computed(() =>
  props.buckets.map(b => new Date(b.bucketStart).toLocaleString([], { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' })),
)

const dataset = computed(() => {
  if (props.metric === 'cpu') {
    return { label: 'CPU %', color: '#42a5f5', values: props.buckets.map(b => b.cpuPercent) }
  }
  if (props.metric === 'memory') {
    return { label: 'Memory', color: '#66bb6a', values: props.buckets.map(b => b.memoryUsedBytes) }
  }
  return { label: 'Disk', color: '#ffa726', values: props.buckets.map(b => b.diskBytes ?? null) }
})

const chartData = computed(() => ({
  labels: labels.value,
  datasets: [
    {
      label: dataset.value.label,
      data: dataset.value.values,
      borderColor: dataset.value.color,
      backgroundColor: `${dataset.value.color}33`,
      fill: true,
      tension: 0.25,
      pointRadius: 0,
      spanGaps: true,
    },
  ],
}))

const isBytes = computed(() => props.metric !== 'cpu')

const chartOptions = computed(() => ({
  responsive: true,
  maintainAspectRatio: false,
  interaction: { mode: 'index' as const, intersect: false },
  scales: {
    y: {
      beginAtZero: true,
      ticks: {
        callback: (value: number | string) =>
          isBytes.value ? formatBytes(Number(value)) : `${value}%`,
      },
    },
    x: {
      ticks: { maxTicksLimit: 8, maxRotation: 0 },
    },
  },
  plugins: {
    legend: { display: false },
    tooltip: {
      callbacks: {
        label: (context: { parsed: { y: number | null } }) => {
          const y = context.parsed.y
          if (y === null) return 'no data'
          return isBytes.value ? formatBytes(y) : `${y.toFixed(1)}%`
        },
      },
    },
  },
}))
</script>

<template>
  <div class="chart-wrap">
    <Line :data="chartData" :options="chartOptions" />
  </div>
</template>

<style scoped>
.chart-wrap {
  position: relative;
  height: 260px;
}
</style>

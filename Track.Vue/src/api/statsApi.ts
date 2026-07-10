import { http } from './http'
import type { SnapshotDto, HistoryBucketDto } from '@/types/stats'

export async function getCurrent(): Promise<SnapshotDto> {
  const { data } = await http.get<SnapshotDto>('/stats/current')
  return data
}

export interface HistoryQuery {
  stackName?: string
  customerId?: number
  hours?: number
}

export async function getHistory(query: HistoryQuery): Promise<HistoryBucketDto[]> {
  const { data } = await http.get<HistoryBucketDto[]>('/stats/history', { params: query })
  return data
}

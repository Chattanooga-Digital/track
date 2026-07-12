import { http } from './http'
import type { SnapshotDto, HistoryBucketDto, BillingMonthDto } from '@/types/stats'

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

export async function getBillingMonths(): Promise<string[]> {
  const { data } = await http.get<string[]>('/stats/billing/months')
  return data
}

export async function getBillingMonth(month: string): Promise<BillingMonthDto> {
  const { data } = await http.get<BillingMonthDto>('/stats/billing', { params: { month } })
  return data
}

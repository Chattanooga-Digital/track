import { http } from './http'
import type { BucketDto } from '@/types/customer'

export async function getBuckets(): Promise<BucketDto[]> {
  const { data } = await http.get<BucketDto[]>('/buckets')
  return data
}

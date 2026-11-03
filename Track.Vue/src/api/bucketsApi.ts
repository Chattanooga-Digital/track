import { http } from './http'
import type { AssignmentDto, BucketDto, BucketRefDto } from '@/types/customer'

export async function getBuckets(): Promise<BucketDto[]> {
  const { data } = await http.get<BucketDto[]>('/buckets')
  return data
}

export async function setSharedBuckets(buckets: BucketRefDto[]): Promise<void> {
  await http.put('/buckets/shared', buckets)
}

export async function setBucketAssignment(
  bucket: BucketRefDto,
  assignment: AssignmentDto,
): Promise<void> {
  await http.put('/buckets/assignment', { ...bucket, ...assignment })
}

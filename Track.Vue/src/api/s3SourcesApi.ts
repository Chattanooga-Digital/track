import { http } from './http'
import type { S3SourceDto, S3SourceEditDto, S3SourceTestResultDto } from '@/types/s3'

export async function getS3Sources(): Promise<S3SourceDto[]> {
  const { data } = await http.get<S3SourceDto[]>('/s3-sources')
  return data
}

export async function createS3Source(model: S3SourceEditDto): Promise<S3SourceDto> {
  const { data } = await http.post<S3SourceDto>('/s3-sources', model)
  return data
}

export async function updateS3Source(id: number, model: S3SourceEditDto): Promise<void> {
  await http.put(`/s3-sources/${id}`, model)
}

export async function deleteS3Source(id: number): Promise<void> {
  await http.delete(`/s3-sources/${id}`)
}

export async function testS3Source(
  model: S3SourceEditDto,
  id?: number,
): Promise<S3SourceTestResultDto> {
  const { data } = await http.post<S3SourceTestResultDto>(
    id ? `/s3-sources/test?id=${id}` : '/s3-sources/test',
    model,
  )
  return data
}

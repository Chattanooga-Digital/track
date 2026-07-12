// Mirrors Track.Enums.S3SourceKind.
export type S3SourceKind = 'GenericS3' | 'Minio'

export interface S3SourceDto {
  id: number
  name: string
  endpoint: string
  kind: S3SourceKind
  accessKey: string
  region: string
  enabled: boolean
  hasSecret: boolean
  createdAt: string
}

export interface S3SourceEditDto {
  name: string
  endpoint: string
  kind: S3SourceKind
  accessKey: string
  secretKey: string | null
  region: string
  enabled: boolean
}

export interface S3SourceTestResultDto {
  ok: boolean
  bucketCount: number
  error: string | null
}

export interface S3SourceRefreshResultDto {
  bucketCount: number
}

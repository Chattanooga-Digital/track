export interface BucketRefDto {
  sourceName: string
  bucketName: string
}

export interface CustomerDto {
  id: number
  name: string
  notes: string | null
  createdAt: string
  stackNames: string[]
  buckets: BucketRefDto[]
}

export interface BucketDto {
  sourceName: string
  bucketName: string
  sizeBytes: number
  customerId: number | null
  customerName: string | null
  isActive: boolean
}

export interface CustomerEditDto {
  name: string
  notes: string | null
}

export interface StackDto {
  stackName: string
  customerId: number | null
  customerName: string | null
  containerCount: number
  isActive: boolean
}

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
  objectCount: number
  customerId: number | null
  customerName: string | null
  isShared: boolean
  isActive: boolean
}

export interface CustomerEditDto {
  name: string
  notes: string | null
}

export interface AssignmentDto {
  customerId: number | null
  isShared: boolean
}

export interface StackDto {
  stackName: string
  customerId: number | null
  customerName: string | null
  isShared: boolean
  containerCount: number
  isActive: boolean
}

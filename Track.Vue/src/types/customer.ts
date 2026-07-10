export interface CustomerDto {
  id: number
  name: string
  notes: string | null
  createdAt: string
  stackNames: string[]
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

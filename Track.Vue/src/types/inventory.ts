export interface UnidentifiedContainerDto {
  nodeName: string
  containerId: string
  containerName: string
  image: string | null
  envKeys: string[]
}

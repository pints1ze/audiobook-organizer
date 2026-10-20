// Mirrors Models/Api/ProcessingDtos.cs. Statuses are the API's text values.

export interface ProcessingJobFile {
  fileId: number
  sourcePath: string
  destinationPath: string
  status: string
  error: string | null
}

export interface ProcessingJob {
  id: number
  groupId: number | null
  title: string | null
  status: string
  createdAtUtc: string
  startedAtUtc: string | null
  finishedAtUtc: string | null
  error: string | null
  files: ProcessingJobFile[]
}

export type ApproveBookResult = { ok: true; job: ProcessingJob } | { ok: false; message: string }

export interface Settings {
  incomingPath: string | null
  libraryPath: string | null
  keepSourceFiles: boolean
}

export interface SettingsFieldErrors {
  incomingPath?: string[]
  libraryPath?: string[]
}

export type SaveSettingsResult =
  | { ok: true; settings: Settings }
  | { ok: false; errors: SettingsFieldErrors }

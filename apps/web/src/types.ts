export type KanitelState = {
  schemaVersion: string
  accounts?: unknown[]
  projects: Project[]
  columns: BoardColumn[]
  people: Person[]
  members: ProjectMember[]
  repositories: RepositoryLink[]
  agents: AgentProfile[]
  projectAgents: ProjectAgent[]
  tasks: TaskCard[]
  comments: TaskComment[]
  history: TaskHistoryEntry[]
  runs: AgentRun[]
}

export type Project = {
  id: string
  name: string
  description: string
  createdAt: string
}

export type BoardColumn = {
  id: string
  projectId: string
  name: string
  color: string
  position: number
  wipLimit?: number | null
}

export type Person = {
  id: string
  displayName: string
  email: string
  avatarUrl: string
  createdAt: string
}

export type ProjectMember = {
  id: string
  projectId: string
  personId: string
  addedAt: string
}

export type RepositoryLink = {
  id: string
  projectId: string
  name: string
  url: string
  branch: string
  authMode: string
  createdAt: string
}

export type AgentProfile = {
  id: string
  name: string
  agentType: string
  avatarUrl: string
  providerPresetId: string
  provider: string
  baseUrl: string
  model: string
  apiKeyEnvName: string
  containerImage: string
  commandTemplate: string
  systemPrompt: string
  enabled: boolean
  environment: Record<string, string>
  toolTags: string[]
  createdAt: string
}

export type ProjectAgent = {
  id: string
  projectId: string
  agentId: string
  addedAt: string
}

export type TaskCard = {
  id: string
  projectId: string
  columnId: string
  title: string
  description: string
  assigneeAgentId?: string | null
  assigneePersonId?: string | null
  position: number
  createdAt: string
  updatedAt: string
}

export type TaskComment = {
  id: string
  taskId: string
  authorType: string
  authorId: string
  body: string
  createdAt: string
}

export type TaskHistoryEntry = {
  id: string
  taskId: string
  authorType: string
  authorId: string
  action: string
  field: string
  from: string
  to: string
  createdAt: string
}

export type AgentRun = {
  id: string
  projectId: string
  taskId: string
  agentId: string
  triggerCommentId: string
  status: string
  workspacePath: string
  log: string
  exitCode?: number | null
  createdAt: string
  startedAt?: string | null
  finishedAt?: string | null
}

export type ProviderPreset = {
  id: string
  name: string
  provider: string
  baseUrl: string
  defaultModel: string
  apiKeyEnvName: string
  transport: string
  logoUrl: string
  requiresApiKey: boolean
  environment: Record<string, string>
}

export type AgentTemplate = {
  id: string
  name: string
  agentType: string
  whenToUse: string
  systemPrompt: string
  toolTags: string[]
}

export type BootstrapPayload = {
  state: KanitelState
  currentUser?: Person | null
  providerPresets: ProviderPreset[]
  agentTemplates: AgentTemplate[]
  scheduler: {
    runner: string
    intervalSeconds: number
  }
}

export type AuthResponse = {
  token: string
  person: Person
}

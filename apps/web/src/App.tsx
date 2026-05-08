import {
  Bot,
  CheckCircle2,
  GitBranch,
  LayoutDashboard,
  Loader2,
  MessageSquare,
  Moon,
  Play,
  Plus,
  RefreshCw,
  Save,
  Settings,
  Sun,
  Trash2,
  Users
} from 'lucide-react'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { deleteJson, loadBootstrap, patchJson, postJson } from './api'
import type {
  AgentProfile,
  AgentTemplate,
  BoardColumn,
  BootstrapPayload,
  Person,
  Project,
  ProjectAgent,
  ProviderPreset,
  TaskCard
} from './types'

type Tab = 'board' | 'project' | 'agents'
type Theme = 'light' | 'dark'

type TaskDraft = {
  title: string
  description: string
  columnId: string
  assigneeAgentId: string
  assignmentRole: string
  priority: string
  initialComment: string
}

type AgentDraft = {
  name: string
  templateId: string
  avatarUrl: string
  providerPresetId: string
  model: string
  baseUrl: string
  apiKeyEnvName: string
  containerImage: string
  commandTemplate: string
  systemPrompt: string
  enabled: boolean
  environmentText: string
}

const emptyTaskDraft: TaskDraft = {
  title: '',
  description: '',
  columnId: '',
  assigneeAgentId: '',
  assignmentRole: 'worker',
  priority: 'normal',
  initialComment: ''
}

export default function App() {
  const [bootstrap, setBootstrap] = useState<BootstrapPayload | null>(null)
  const [activeProjectId, setActiveProjectId] = useState('')
  const [activeTab, setActiveTab] = useState<Tab>('board')
  const [theme, setTheme] = useState<Theme>(() => readTheme())
  const [selectedTaskId, setSelectedTaskId] = useState('')
  const [newProjectOpen, setNewProjectOpen] = useState(false)
  const [projectDraft, setProjectDraft] = useState({ name: '', description: '' })
  const [newTaskColumnId, setNewTaskColumnId] = useState('')
  const [taskDraft, setTaskDraft] = useState<TaskDraft>(emptyTaskDraft)
  const [commentDraft, setCommentDraft] = useState('')
  const [columnDraft, setColumnDraft] = useState({ name: '', color: '#2563eb' })
  const [memberDraft, setMemberDraft] = useState({ displayName: '', email: '', avatarUrl: '', role: 'editor' })
  const [repoDraft, setRepoDraft] = useState({ name: '', url: '', branch: '', authMode: 'http' })
  const [projectAgentDraft, setProjectAgentDraft] = useState({ agentId: '', role: 'worker' })
  const [agentDraft, setAgentDraft] = useState<AgentDraft>(() => initialAgentDraft())
  const [selectedAgentId, setSelectedAgentId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')

  const refresh = useCallback(async () => {
    const payload = await loadBootstrap()
    setBootstrap(payload)
    setError('')
  }, [])

  useEffect(() => {
    refresh().catch((err: Error) => setError(err.message))
  }, [refresh])

  useEffect(() => {
    const timer = window.setInterval(() => {
      refresh().catch(() => undefined)
    }, 5000)
    return () => window.clearInterval(timer)
  }, [refresh])

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    window.localStorage.setItem('kanitel-theme', theme)
  }, [theme])

  const state = bootstrap?.state
  const projects = state?.projects ?? []

  useEffect(() => {
    if (!state) return
    if (!activeProjectId || !state.projects.some(project => project.id === activeProjectId)) {
      setActiveProjectId(state.projects[0]?.id ?? '')
    }
  }, [state, activeProjectId])

  const activeProject = projects.find(project => project.id === activeProjectId) ?? projects[0]
  const columns = useMemo(
    () => (state?.columns ?? [])
      .filter(column => column.projectId === activeProject?.id)
      .sort((a, b) => a.position - b.position),
    [state, activeProject?.id]
  )
  const tasks = useMemo(
    () => (state?.tasks ?? [])
      .filter(task => task.projectId === activeProject?.id)
      .sort((a, b) => a.position - b.position || b.updatedAt.localeCompare(a.updatedAt)),
    [state, activeProject?.id]
  )
  const projectAgents = useMemo(() => {
    if (!state || !activeProject) return []
    return state.projectAgents
      .filter(link => link.projectId === activeProject.id)
      .map(link => ({ link, agent: state.agents.find(agent => agent.id === link.agentId) }))
      .filter((item): item is { link: ProjectAgent; agent: AgentProfile } => Boolean(item.agent))
  }, [state, activeProject])
  const selectedTask = tasks.find(task => task.id === selectedTaskId)
  const selectedAgent = state?.agents.find(agent => agent.id === selectedAgentId)

  useEffect(() => {
    if (!selectedTask) return
    setTaskDraft({
      title: selectedTask.title,
      description: selectedTask.description,
      columnId: selectedTask.columnId,
      assigneeAgentId: selectedTask.assigneeAgentId ?? '',
      assignmentRole: selectedTask.assignmentRole || 'worker',
      priority: selectedTask.priority,
      initialComment: ''
    })
  }, [selectedTask])

  useEffect(() => {
    if (!selectedAgent) return
    setAgentDraft({
      name: selectedAgent.name,
      templateId: selectedAgent.agentType,
      avatarUrl: selectedAgent.avatarUrl ?? '',
      providerPresetId: selectedAgent.providerPresetId,
      model: selectedAgent.model,
      baseUrl: selectedAgent.baseUrl,
      apiKeyEnvName: selectedAgent.apiKeyEnvName,
      containerImage: selectedAgent.containerImage,
      commandTemplate: selectedAgent.commandTemplate,
      systemPrompt: selectedAgent.systemPrompt,
      enabled: selectedAgent.enabled,
      environmentText: envToText(selectedAgent.environment)
    })
  }, [selectedAgent])

  async function mutate(action: Promise<unknown>) {
    setBusy(true)
    setError('')
    try {
      await action
      await refresh()
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  async function createProject() {
    if (!projectDraft.name.trim()) return
    await mutate(postJson<Project>('/api/projects', projectDraft))
    setProjectDraft({ name: '', description: '' })
    setNewProjectOpen(false)
  }

  async function createTask(columnId: string) {
    if (!activeProject || !taskDraft.title.trim()) return
    await mutate(postJson<TaskCard>(`/api/projects/${activeProject.id}/tasks`, {
      ...taskDraft,
      columnId,
      authorId: state?.people[0]?.id ?? 'owner'
    }))
    setTaskDraft(emptyTaskDraft)
    setNewTaskColumnId('')
  }

  async function saveTask() {
    if (!selectedTask) return
    await mutate(patchJson<TaskCard>(`/api/tasks/${selectedTask.id}`, taskDraft))
  }

  async function addComment() {
    if (!selectedTask || !commentDraft.trim()) return
    await mutate(postJson(`/api/tasks/${selectedTask.id}/comments`, {
      body: commentDraft,
      authorType: 'person',
      authorId: state?.people[0]?.id ?? 'owner'
    }))
    setCommentDraft('')
  }

  async function createAgent() {
    await mutate(postJson('/api/agents', {
      name: agentDraft.name,
      templateId: agentDraft.templateId,
      avatarUrl: agentDraft.avatarUrl,
      providerPresetId: agentDraft.providerPresetId,
      model: agentDraft.model,
      baseUrl: agentDraft.baseUrl,
      apiKeyEnvName: agentDraft.apiKeyEnvName,
      containerImage: agentDraft.containerImage,
      commandTemplate: agentDraft.commandTemplate,
      systemPrompt: agentDraft.systemPrompt,
      enabled: agentDraft.enabled,
      environment: parseEnvText(agentDraft.environmentText)
    }))
  }

  async function saveAgent() {
    if (!selectedAgent) return
    await mutate(patchJson(`/api/agents/${selectedAgent.id}`, {
      name: agentDraft.name,
      avatarUrl: agentDraft.avatarUrl,
      providerPresetId: agentDraft.providerPresetId,
      model: agentDraft.model,
      baseUrl: agentDraft.baseUrl,
      apiKeyEnvName: agentDraft.apiKeyEnvName,
      containerImage: agentDraft.containerImage,
      commandTemplate: agentDraft.commandTemplate,
      systemPrompt: agentDraft.systemPrompt,
      enabled: agentDraft.enabled,
      environment: parseEnvText(agentDraft.environmentText)
    }))
  }

  function applyPresetToDraft(presetId: string) {
    const preset = bootstrap?.providerPresets.find(item => item.id === presetId)
    if (!preset) return
    setAgentDraft(current => ({
      ...current,
      avatarUrl: current.avatarUrl || preset.logoUrl || '',
      providerPresetId: preset.id,
      model: preset.defaultModel,
      baseUrl: preset.baseUrl,
      apiKeyEnvName: preset.apiKeyEnvName,
      environmentText: envToText(preset.environment)
    }))
  }

  function applyTemplateToDraft(templateId: string) {
    const template = bootstrap?.agentTemplates.find(item => item.id === templateId)
    if (!template) return
    setAgentDraft(current => ({
      ...current,
      templateId,
      name: current.name || template.name,
      systemPrompt: template.systemPrompt
    }))
  }

  if (!bootstrap || !state || !activeProject) {
    return (
      <main className="loading-page">
        <Loader2 className="spin" size={22} />
        <span>Загрузка Kanitel</span>
      </main>
    )
  }

  const selectedTaskComments = state.comments
    .filter(comment => comment.taskId === selectedTask?.id)
    .sort((a, b) => a.createdAt.localeCompare(b.createdAt))
  const selectedTaskRuns = state.runs
    .filter(run => run.taskId === selectedTask?.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const unlinkedAgents = state.agents.filter(agent => !projectAgents.some(item => item.agent.id === agent.id))

  return (
    <main className="app-shell">
      <header className="topbar">
        <div className="brand">
          <div className="brand-mark">K</div>
          <div>
            <h1>Kanitel</h1>
            <span>{bootstrap.scheduler.runner} · {bootstrap.scheduler.intervalSeconds}s · API /api/openapi.json</span>
          </div>
        </div>

        <div className="project-switcher">
          <select value={activeProject.id} onChange={event => setActiveProjectId(event.target.value)}>
            {projects.map(project => (
              <option key={project.id} value={project.id}>{project.name}</option>
            ))}
          </select>
          <button className="icon-button" title="Новый проект" onClick={() => setNewProjectOpen(value => !value)}>
            <Plus size={18} />
          </button>
          <button className="icon-button" title="Обновить" onClick={() => mutate(refresh())} disabled={busy}>
            <RefreshCw size={18} />
          </button>
          <button className="icon-button" title={theme === 'dark' ? 'Светлая тема' : 'Темная тема'} onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          <button className="primary-button" onClick={() => mutate(postJson('/api/scheduler/tick', {}))} disabled={busy}>
            <Play size={16} />
            Проверить
          </button>
        </div>
      </header>

      {newProjectOpen && (
        <section className="quick-create">
          <input value={projectDraft.name} onChange={event => setProjectDraft({ ...projectDraft, name: event.target.value })} placeholder="Название проекта" />
          <input value={projectDraft.description} onChange={event => setProjectDraft({ ...projectDraft, description: event.target.value })} placeholder="Описание" />
          <button className="primary-button" onClick={createProject} disabled={busy}>
            <Plus size={16} />
            Создать
          </button>
        </section>
      )}

      {error && <div className="error-line">{error}</div>}

      <nav className="tabs">
        <button className={activeTab === 'board' ? 'active' : ''} onClick={() => setActiveTab('board')}>
          <LayoutDashboard size={16} />
          Доска
        </button>
        <button className={activeTab === 'project' ? 'active' : ''} onClick={() => setActiveTab('project')}>
          <Settings size={16} />
          Проект
        </button>
        <button className={activeTab === 'agents' ? 'active' : ''} onClick={() => setActiveTab('agents')}>
          <Bot size={16} />
          Агенты
        </button>
      </nav>

      {activeTab === 'board' && (
        <section className="board-layout">
          <div className="board-scroll">
            {columns.map(column => (
              <KanbanColumn
                key={column.id}
                column={column}
                tasks={tasks.filter(task => task.columnId === column.id)}
                selectedTaskId={selectedTaskId}
                agents={state.agents}
                runs={state.runs}
                projectAgents={projectAgents}
                taskDraft={taskDraft}
                newTaskColumnId={newTaskColumnId}
                busy={busy}
                onSelectTask={setSelectedTaskId}
                onOpenNewTask={() => {
                  setNewTaskColumnId(column.id)
                  setTaskDraft({ ...emptyTaskDraft, columnId: column.id })
                }}
                onDraftChange={setTaskDraft}
                onCreateTask={() => createTask(column.id)}
              />
            ))}
          </div>

          <aside className="task-panel">
            {selectedTask ? (
              <>
                <div className="panel-title">
                  <h2>{selectedTask.title}</h2>
                  <span>{selectedTaskRuns[0]?.status ?? 'idle'}</span>
                </div>
                <label>
                  Заголовок
                  <input value={taskDraft.title} onChange={event => setTaskDraft({ ...taskDraft, title: event.target.value })} />
                </label>
                <label>
                  Описание
                  <textarea value={taskDraft.description} onChange={event => setTaskDraft({ ...taskDraft, description: event.target.value })} rows={5} />
                </label>
                <div className="form-grid two">
                  <label>
                    Статус
                    <select value={taskDraft.columnId} onChange={event => setTaskDraft({ ...taskDraft, columnId: event.target.value })}>
                      {columns.map(column => (
                        <option key={column.id} value={column.id}>{column.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Приоритет
                    <select value={taskDraft.priority} onChange={event => setTaskDraft({ ...taskDraft, priority: event.target.value })}>
                      <option value="low">Низкий</option>
                      <option value="normal">Обычный</option>
                      <option value="high">Высокий</option>
                      <option value="urgent">Срочный</option>
                    </select>
                  </label>
                </div>
                <div className="form-grid two">
                  <label>
                    Агент
                    <select value={taskDraft.assigneeAgentId} onChange={event => setTaskDraft({ ...taskDraft, assigneeAgentId: event.target.value })}>
                      <option value="">Не назначен</option>
                      {projectAgents.map(({ agent }) => (
                        <option key={agent.id} value={agent.id}>{agent.name}</option>
                      ))}
                    </select>
                  </label>
                  <label>
                    Роль в задаче
                    <select value={taskDraft.assignmentRole} onChange={event => setTaskDraft({ ...taskDraft, assignmentRole: event.target.value })}>
                      <option value="worker">worker</option>
                      <option value="manager">manager</option>
                      <option value="reviewer">reviewer</option>
                    </select>
                  </label>
                </div>
                <button className="primary-button full" onClick={saveTask} disabled={busy}>
                  <Save size={16} />
                  Сохранить задачу
                </button>

                <section className="comments">
                  <h3><MessageSquare size={16} /> Комментарии</h3>
                  {selectedTaskComments.map(comment => (
                    <article key={comment.id} className={`comment ${comment.authorType}`}>
                      <div>
                        <strong>{authorLabel(comment.authorType, comment.authorId, state.agents, state.people)}</strong>
                        <span>{formatDate(comment.createdAt)}</span>
                      </div>
                      <p>{comment.body}</p>
                    </article>
                  ))}
                  <textarea value={commentDraft} onChange={event => setCommentDraft(event.target.value)} rows={4} placeholder="Комментарий" />
                  <button className="secondary-button full" onClick={addComment} disabled={busy}>
                    <MessageSquare size={16} />
                    Отправить
                  </button>
                </section>
              </>
            ) : (
              <div className="empty-panel">
                <CheckCircle2 size={22} />
                <span>Выберите задачу</span>
              </div>
            )}
          </aside>
        </section>
      )}

      {activeTab === 'project' && (
        <section className="settings-grid">
          <Panel title="Колонки" icon={<LayoutDashboard size={17} />}>
            <div className="row-list">
              {columns.map(column => (
                <div className="settings-row" key={column.id}>
                  <input defaultValue={column.name} onBlur={event => {
                    if (event.currentTarget.value.trim() !== column.name) {
                      mutate(patchJson(`/api/columns/${column.id}`, { name: event.currentTarget.value }))
                    }
                  }} />
                  <input className="color-input" type="color" value={column.color} onChange={event => mutate(patchJson(`/api/columns/${column.id}`, { color: event.target.value }))} />
                  <button className="icon-button compact" title="Удалить колонку" onClick={() => mutate(deleteJson(`/api/columns/${column.id}`))}>
                    <Trash2 size={15} />
                  </button>
                </div>
              ))}
              <div className="settings-row">
                <input value={columnDraft.name} onChange={event => setColumnDraft({ ...columnDraft, name: event.target.value })} placeholder="Новая колонка" />
                <input className="color-input" type="color" value={columnDraft.color} onChange={event => setColumnDraft({ ...columnDraft, color: event.target.value })} />
                <button className="icon-button compact" title="Добавить колонку" onClick={() => {
                  mutate(postJson(`/api/projects/${activeProject.id}/columns`, columnDraft))
                  setColumnDraft({ name: '', color: '#2563eb' })
                }}>
                  <Plus size={15} />
                </button>
              </div>
            </div>
          </Panel>

          <Panel title="Доступ" icon={<Users size={17} />}>
            <div className="row-list">
              {state.members.filter(member => member.projectId === activeProject.id).map(member => {
                const person = state.people.find(item => item.id === member.personId)
                return (
                  <div className="person-row" key={member.id}>
                    <Avatar name={person?.displayName ?? member.personId} url={person?.avatarUrl} />
                    <span>{person?.displayName ?? member.personId}</span>
                    <span className="muted">{member.role}</span>
                    <button className="icon-button compact" title="Убрать доступ" onClick={() => mutate(deleteJson(`/api/members/${member.id}`))}>
                      <Trash2 size={15} />
                    </button>
                  </div>
                )
              })}
              <div className="stack-form">
                <input value={memberDraft.displayName} onChange={event => setMemberDraft({ ...memberDraft, displayName: event.target.value })} placeholder="Имя" />
                <input value={memberDraft.email} onChange={event => setMemberDraft({ ...memberDraft, email: event.target.value })} placeholder="email" />
                <input value={memberDraft.avatarUrl} onChange={event => setMemberDraft({ ...memberDraft, avatarUrl: event.target.value })} placeholder="avatar URL" />
                <select value={memberDraft.role} onChange={event => setMemberDraft({ ...memberDraft, role: event.target.value })}>
                  <option value="viewer">viewer</option>
                  <option value="editor">editor</option>
                  <option value="owner">owner</option>
                </select>
                <button className="secondary-button" onClick={() => {
                  mutate(postJson(`/api/projects/${activeProject.id}/members`, memberDraft))
                  setMemberDraft({ displayName: '', email: '', avatarUrl: '', role: 'editor' })
                }}>
                  <Plus size={16} />
                  Добавить
                </button>
              </div>
            </div>
          </Panel>

          <Panel title="Репозитории" icon={<GitBranch size={17} />}>
            <div className="row-list">
              {state.repositories.filter(repo => repo.projectId === activeProject.id).map(repo => (
                <div className="repo-row" key={repo.id}>
                  <strong>{repo.name}</strong>
                  <span>{repo.url}</span>
                  <em>{repo.branch || repo.authMode}</em>
                  <button className="icon-button compact" title="Удалить репозиторий" onClick={() => mutate(deleteJson(`/api/repositories/${repo.id}`))}>
                    <Trash2 size={15} />
                  </button>
                </div>
              ))}
              <div className="stack-form">
                <input value={repoDraft.name} onChange={event => setRepoDraft({ ...repoDraft, name: event.target.value })} placeholder="Название" />
                <input value={repoDraft.url} onChange={event => setRepoDraft({ ...repoDraft, url: event.target.value })} placeholder="https:// или git@host:path.git" />
                <input value={repoDraft.branch} onChange={event => setRepoDraft({ ...repoDraft, branch: event.target.value })} placeholder="branch" />
                <select value={repoDraft.authMode} onChange={event => setRepoDraft({ ...repoDraft, authMode: event.target.value })}>
                  <option value="http">http</option>
                  <option value="ssh">ssh</option>
                </select>
                <button className="secondary-button" onClick={() => {
                  mutate(postJson(`/api/projects/${activeProject.id}/repositories`, repoDraft))
                  setRepoDraft({ name: '', url: '', branch: '', authMode: 'http' })
                }}>
                  <Plus size={16} />
                  Добавить
                </button>
              </div>
            </div>
          </Panel>

          <Panel title="Агенты проекта" icon={<Bot size={17} />}>
            <div className="row-list">
              {projectAgents.map(({ link, agent }) => (
                <div className="agent-access-row" key={link.id}>
                  <Avatar name={agent.name} url={agent.avatarUrl} />
                  <span>{agent.name}</span>
                  <select value={link.role} onChange={event => mutate(patchJson(`/api/project-agents/${link.id}`, { role: event.target.value }))}>
                    <option value="worker">worker</option>
                    <option value="reviewer">reviewer</option>
                    <option value="manager">manager</option>
                    <option value="observer">observer</option>
                  </select>
                  <button className="icon-button compact" title="Убрать агента" onClick={() => mutate(deleteJson(`/api/project-agents/${link.id}`))}>
                    <Trash2 size={15} />
                  </button>
                </div>
              ))}
              <div className="settings-row">
                <select value={projectAgentDraft.agentId} onChange={event => setProjectAgentDraft({ ...projectAgentDraft, agentId: event.target.value })}>
                  <option value="">Выберите агента</option>
                  {unlinkedAgents.map(agent => (
                    <option key={agent.id} value={agent.id}>{agent.name}</option>
                  ))}
                </select>
                <select value={projectAgentDraft.role} onChange={event => setProjectAgentDraft({ ...projectAgentDraft, role: event.target.value })}>
                  <option value="worker">worker</option>
                  <option value="reviewer">reviewer</option>
                  <option value="manager">manager</option>
                  <option value="observer">observer</option>
                </select>
                <button className="icon-button compact" title="Добавить агента" onClick={() => mutate(postJson(`/api/projects/${activeProject.id}/agents`, projectAgentDraft))}>
                  <Plus size={15} />
                </button>
              </div>
            </div>
          </Panel>
        </section>
      )}

      {activeTab === 'agents' && (
        <section className="agents-layout">
          <div className="agent-list">
            {state.agents.map(agent => (
              <button key={agent.id} className={selectedAgentId === agent.id ? 'agent-item active' : 'agent-item'} onClick={() => setSelectedAgentId(agent.id)}>
                <Avatar name={agent.name} url={agent.avatarUrl} />
                <span>
                  <strong>{agent.name}</strong>
                  <em>{agent.providerPresetId} · {agent.model}</em>
                </span>
                <small>{agent.enabled ? 'on' : 'off'}</small>
              </button>
            ))}
          </div>
          <div className="agent-editor">
            <div className="panel-title">
              <h2>{selectedAgent ? 'Профиль агента' : 'Новый агент'}</h2>
              <button className="secondary-button" onClick={() => {
                setSelectedAgentId('')
                setAgentDraft(initialAgentDraft(bootstrap.providerPresets[0], bootstrap.agentTemplates[0]))
              }}>
                <Plus size={16} />
                Новый
              </button>
            </div>
            <AgentForm
              draft={agentDraft}
              presets={bootstrap.providerPresets}
              templates={bootstrap.agentTemplates}
              onChange={setAgentDraft}
              onPreset={applyPresetToDraft}
              onTemplate={applyTemplateToDraft}
            />
            <div className="button-row">
              {selectedAgent ? (
                <button className="primary-button" onClick={saveAgent} disabled={busy}>
                  <Save size={16} />
                  Сохранить
                </button>
              ) : (
                <button className="primary-button" onClick={createAgent} disabled={busy}>
                  <Plus size={16} />
                  Создать
                </button>
              )}
            </div>
          </div>
        </section>
      )}
    </main>
  )
}

function KanbanColumn({
  column,
  tasks,
  selectedTaskId,
  agents,
  runs,
  projectAgents,
  taskDraft,
  newTaskColumnId,
  busy,
  onSelectTask,
  onOpenNewTask,
  onDraftChange,
  onCreateTask
}: {
  column: BoardColumn
  tasks: TaskCard[]
  selectedTaskId: string
  agents: AgentProfile[]
  runs: Array<{ taskId: string; status: string; createdAt: string }>
  projectAgents: Array<{ link: ProjectAgent; agent: AgentProfile }>
  taskDraft: TaskDraft
  newTaskColumnId: string
  busy: boolean
  onSelectTask: (id: string) => void
  onOpenNewTask: () => void
  onDraftChange: (draft: TaskDraft) => void
  onCreateTask: () => void
}) {
  return (
    <section className="column">
      <header className="column-header" style={{ borderTopColor: column.color }}>
        <div>
          <h2>{column.name}</h2>
          <span>{tasks.length}{column.wipLimit ? ` / ${column.wipLimit}` : ''}</span>
        </div>
        <button className="icon-button compact" title="Добавить задачу" onClick={onOpenNewTask}>
          <Plus size={16} />
        </button>
      </header>

      {newTaskColumnId === column.id && (
        <div className="inline-form">
          <input value={taskDraft.title} onChange={event => onDraftChange({ ...taskDraft, title: event.target.value })} placeholder="Заголовок" />
          <textarea value={taskDraft.description} onChange={event => onDraftChange({ ...taskDraft, description: event.target.value })} placeholder="Описание" rows={3} />
          <div className="form-grid two">
            <select value={taskDraft.assigneeAgentId} onChange={event => onDraftChange({ ...taskDraft, assigneeAgentId: event.target.value })}>
              <option value="">Без агента</option>
              {projectAgents.map(({ agent }) => (
                <option key={agent.id} value={agent.id}>{agent.name}</option>
              ))}
            </select>
            <select value={taskDraft.assignmentRole} onChange={event => onDraftChange({ ...taskDraft, assignmentRole: event.target.value })}>
              <option value="worker">worker</option>
              <option value="manager">manager</option>
              <option value="reviewer">reviewer</option>
            </select>
          </div>
          <button className="primary-button" onClick={onCreateTask} disabled={busy}>
            <Plus size={16} />
            Добавить
          </button>
        </div>
      )}

      <div className="task-list">
        {tasks.map(task => (
          <button
            key={task.id}
            className={`task-card ${selectedTaskId === task.id ? 'selected' : ''}`}
            onClick={() => onSelectTask(task.id)}
          >
            <span className={`priority ${task.priority}`}>{priorityLabel(task.priority)}</span>
            <strong>{task.title}</strong>
            <span className="task-description">{task.description || 'Без описания'}</span>
            <TaskMeta task={task} agents={agents} runs={runs} />
          </button>
        ))}
      </div>
    </section>
  )
}

function Panel({ title, icon, children }: { title: string; icon: ReactNode; children: ReactNode }) {
  return (
    <section className="settings-panel">
      <header>
        {icon}
        <h2>{title}</h2>
      </header>
      {children}
    </section>
  )
}

function AgentForm({
  draft,
  presets,
  templates,
  onChange,
  onPreset,
  onTemplate
}: {
  draft: AgentDraft
  presets: ProviderPreset[]
  templates: AgentTemplate[]
  onChange: (draft: AgentDraft) => void
  onPreset: (presetId: string) => void
  onTemplate: (templateId: string) => void
}) {
  return (
    <div className="agent-form">
      <div className="form-grid two">
        <label>
          Имя
          <input value={draft.name} onChange={event => onChange({ ...draft, name: event.target.value })} />
        </label>
        <label>
          Шаблон
          <select value={draft.templateId} onChange={event => onTemplate(event.target.value)}>
            {templates.map(template => (
              <option key={template.id} value={template.id}>{template.name}</option>
            ))}
          </select>
        </label>
      </div>
      <label>
        Аватар или логотип
        <input value={draft.avatarUrl} onChange={event => onChange({ ...draft, avatarUrl: event.target.value })} placeholder="https://..." />
      </label>
      <div className="form-grid two">
        <label>
          Провайдер
          <select value={draft.providerPresetId} onChange={event => onPreset(event.target.value)}>
            {presets.map(preset => (
              <option key={preset.id} value={preset.id}>{preset.name}</option>
            ))}
          </select>
        </label>
        <label>
          Модель
          <input value={draft.model} onChange={event => onChange({ ...draft, model: event.target.value })} />
        </label>
      </div>
      <label>
        Base URL
        <input value={draft.baseUrl} onChange={event => onChange({ ...draft, baseUrl: event.target.value })} />
      </label>
      <div className="form-grid two">
        <label>
          API env
          <input value={draft.apiKeyEnvName} onChange={event => onChange({ ...draft, apiKeyEnvName: event.target.value })} />
        </label>
        <label>
          Image
          <input value={draft.containerImage} onChange={event => onChange({ ...draft, containerImage: event.target.value })} />
        </label>
      </div>
      <label>
        Command
        <textarea value={draft.commandTemplate} onChange={event => onChange({ ...draft, commandTemplate: event.target.value })} rows={3} />
      </label>
      <label>
        System prompt
        <textarea value={draft.systemPrompt} onChange={event => onChange({ ...draft, systemPrompt: event.target.value })} rows={5} />
      </label>
      <label>
        Environment
        <textarea value={draft.environmentText} onChange={event => onChange({ ...draft, environmentText: event.target.value })} rows={6} />
      </label>
      <label className="toggle-row">
        <input type="checkbox" checked={draft.enabled} onChange={event => onChange({ ...draft, enabled: event.target.checked })} />
        Enabled
      </label>
    </div>
  )
}

function Avatar({ name, url }: { name: string; url?: string | null }) {
  if (url) {
    return <img className="avatar" src={url} alt="" />
  }

  const initials = name
    .split(/\s+/)
    .filter(Boolean)
    .slice(0, 2)
    .map(part => part[0]?.toUpperCase())
    .join('') || '?'

  return (
    <span className="avatar fallback">
      {initials}
    </span>
  )
}

function TaskMeta({ task, agents, runs }: { task: TaskCard; agents: AgentProfile[]; runs: Array<{ taskId: string; status: string; createdAt: string }> }) {
  const agent = agents.find(item => item.id === task.assigneeAgentId)
  const run = runs
    .filter(item => item.taskId === task.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0]
  return (
    <span className="task-meta">
      <span><Bot size={13} /> {agent?.name ?? 'нет агента'}</span>
      <span>{task.assigneeAgentId ? task.assignmentRole : 'unassigned'}</span>
      <span>{run?.status ?? 'idle'}</span>
    </span>
  )
}

function initialAgentDraft(preset?: ProviderPreset, template?: AgentTemplate): AgentDraft {
  const selectedPreset = preset ?? {
    id: 'codex',
    name: 'Codex / OpenClaude',
    provider: 'codex',
    defaultModel: 'codexplan',
    baseUrl: 'https://chatgpt.com/backend-api/codex',
    apiKeyEnvName: 'CODEX_API_KEY',
    transport: 'codex-responses',
    logoUrl: 'https://www.google.com/s2/favicons?sz=128&domain=chatgpt.com',
    requiresApiKey: true,
    environment: { OPENAI_MODEL: 'codexplan', OPENAI_BASE_URL: 'https://chatgpt.com/backend-api/codex' }
  }
  const selectedTemplate = template ?? {
    id: 'general-purpose',
    name: 'General Purpose',
    agentType: 'general-purpose',
    whenToUse: '',
    systemPrompt: 'You are a practical coding agent.',
    toolTags: ['*']
  }
  return {
    name: selectedTemplate.name,
    templateId: selectedTemplate.id,
    avatarUrl: selectedPreset.logoUrl ?? '',
    providerPresetId: selectedPreset.id,
    model: selectedPreset.defaultModel,
    baseUrl: selectedPreset.baseUrl,
    apiKeyEnvName: selectedPreset.apiKeyEnvName,
    containerImage: 'node:22-bookworm',
    commandTemplate: 'npx -y @gitlawb/openclaude@latest --print "$(cat \\"$KANITEL_TASK_PROMPT_FILE\\")"',
    systemPrompt: selectedTemplate.systemPrompt,
    enabled: true,
    environmentText: envToText(selectedPreset.environment)
  }
}

function parseEnvText(value: string): Record<string, string> {
  return Object.fromEntries(
    value
      .split('\n')
      .map(line => line.trim())
      .filter(line => line && !line.startsWith('#'))
      .map(line => {
        const index = line.indexOf('=')
        return index === -1 ? [line, ''] : [line.slice(0, index).trim(), line.slice(index + 1).trim()]
      })
  )
}

function envToText(env: Record<string, string> = {}) {
  return Object.entries(env)
    .map(([key, value]) => `${key}=${value}`)
    .join('\n')
}

function priorityLabel(priority: string) {
  return {
    low: 'low',
    normal: 'normal',
    high: 'high',
    urgent: 'urgent'
  }[priority] ?? priority
}

function authorLabel(authorType: string, authorId: string, agents: AgentProfile[], people: Person[]) {
  if (authorType === 'agent') {
    return agents.find(agent => agent.id === authorId)?.name ?? authorId
  }
  if (authorType === 'person') {
    return people.find(person => person.id === authorId)?.displayName ?? authorId
  }
  return authorType
}

function formatDate(value: string) {
  return new Intl.DateTimeFormat('ru', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function readTheme(): Theme {
  const stored = window.localStorage.getItem('kanitel-theme')
  if (stored === 'light' || stored === 'dark') return stored
  return window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light'
}

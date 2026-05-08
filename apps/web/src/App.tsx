import {
  Bot,
  CheckCircle2,
  Folder,
  GitBranch,
  GripVertical,
  History as HistoryIcon,
  Languages,
  LayoutDashboard,
  Loader2,
  LogIn,
  LogOut,
  MessageSquare,
  Moon,
  Plus,
  Save,
  Settings,
  Sun,
  Trash2,
  Upload,
  UserPlus,
  Users,
  X
} from 'lucide-react'
import type { ReactNode } from 'react'
import { useCallback, useEffect, useMemo, useState } from 'react'
import { deleteJson, loadBootstrap, patchJson, postJson, readAuthToken, storeAuthToken, uploadAvatarFile } from './api'
import type {
  AgentProfile,
  AgentTemplate,
  AuthResponse,
  BoardColumn,
  BootstrapPayload,
  KanitelState,
  Person,
  Project,
  ProjectAgent,
  ProviderPreset,
  TaskCard,
  TaskComment,
  TaskHistoryEntry
} from './types'

type Tab = 'board' | 'projects' | 'settings'
type Theme = 'light' | 'dark'
type Locale = 'ru' | 'en'
type ParticipantKind = 'person' | 'agent'
type AssigneeFilter = 'all' | 'unassigned' | string

type EnvPair = {
  id: string
  key: string
  value: string
}

type TaskDraft = {
  title: string
  description: string
  columnId: string
  assigneeId: string
  initialComment: string
}

type AuthDraft = {
  displayName: string
  email: string
  password: string
  confirmPassword: string
  avatarUrl: string
}

type ProfileDraft = {
  displayName: string
  email: string
  avatarUrl: string
  currentPassword: string
  newPassword: string
  confirmNewPassword: string
}

type ParticipantDraft = {
  kind: ParticipantKind
  personId: string
  agentId: string
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
  environment: EnvPair[]
}

type Participant = {
  key: string
  kind: ParticipantKind
  id: string
  linkId: string
  name: string
  email?: string
  avatarUrl?: string | null
}

const labels = {
  ru: {
    loading: 'Загрузка Kanitel',
    loginTitle: 'Вход в Kanitel',
    registerTitle: 'Регистрация',
    login: 'Войти',
    register: 'Создать аккаунт',
    haveAccount: 'Уже есть аккаунт',
    needAccount: 'Нужен аккаунт',
    displayName: 'Имя',
    email: 'Email',
    password: 'Пароль',
    confirmPassword: 'Повторите пароль',
    currentPassword: 'Текущий пароль',
    newPassword: 'Новый пароль',
    confirmNewPassword: 'Повторите новый пароль',
    passwordsDoNotMatch: 'Пароли не совпадают',
    passwordChangeFieldsRequired: 'Для смены пароля заполните текущий пароль, новый пароль и повтор нового пароля',
    avatarUrl: 'Аватар URL',
    uploadAvatar: 'Загрузить файл',
    navBoard: 'Доска',
    navProjects: 'Проекты',
    navSettings: 'Настройки',
    project: 'Проект',
    darkThemeTitle: 'Темная тема',
    lightThemeTitle: 'Светлая тема',
    languageTitle: 'Switch to English',
    languageButton: 'EN',
    logout: 'Выйти',
    schedulerInfo: (runner: string, seconds: number) => `${runner} · ${seconds}s · API /api/openapi.json`,
    newProject: 'Новый проект',
    projectName: 'Название проекта',
    description: 'Описание',
    create: 'Создать',
    save: 'Сохранить',
    activeProject: 'Активный проект',
    columns: 'Колонки',
    participants: 'Участники',
    repositories: 'Репозитории',
    addColumn: 'Добавить колонку',
    columnName: 'Новая колонка',
    deleteColumn: 'Удалить колонку',
    deleteProject: 'Удалить проект',
    confirmDeleteProject: (name: string) => `Удалить проект «${name}»? Все задачи, комментарии, история, участники и репозитории проекта будут удалены.`,
    addParticipant: 'Добавить участника',
    participantType: 'Тип участника',
    person: 'Человек',
    agent: 'Агент',
    selectPerson: 'Выбрать человека',
    selectAgent: 'Выбрать агента',
    remove: 'Убрать',
    deleteAgent: 'Удалить агента',
    envKey: 'Переменная',
    envValue: 'Значение',
    addEnv: 'Добавить переменную',
    removeEnv: 'Удалить переменную',
    noEnv: 'Переменных окружения нет',
    repositoryName: 'Название',
    repositoryUrl: 'https:// или git@host:path.git',
    branch: 'Ветка',
    authMode: 'Доступ',
    deleteRepository: 'Удалить репозиторий',
    addTask: 'Добавить задачу',
    assigneeFilter: 'Фильтр по исполнителю',
    allAssignees: 'Все исполнители',
    title: 'Заголовок',
    assignee: 'Исполнитель',
    noAssignee: 'Без исполнителя',
    noDescription: 'Без описания',
    noTasks: 'Пока пусто',
    taskDetails: 'Задача',
    status: 'Статус',
    author: 'Автор',
    comments: 'Комментарии',
    history: 'История',
    noHistory: 'Истории пока нет',
    createdTask: 'создал задачу',
    changed: 'изменил',
    emptyValue: 'пусто',
    fieldTitle: 'заголовок',
    fieldDescription: 'описание',
    fieldStatus: 'статус',
    fieldAssignee: 'исполнителя',
    commentPlaceholder: 'Комментарий',
    send: 'Отправить',
    runs: 'Запуски',
    profile: 'Профиль',
    globalAgents: 'Глобальные агенты',
    newAgent: 'Новый агент',
    agentProfile: 'Профиль агента',
    newButton: 'Новый',
    agentName: 'Имя агента',
    template: 'Шаблон',
    avatarOrLogo: 'Аватар или логотип',
    provider: 'Провайдер',
    model: 'Модель',
    baseUrl: 'Base URL',
    apiEnv: 'API env',
    image: 'Образ',
    command: 'Команда',
    systemPrompt: 'Системный промпт',
    environment: 'Окружение',
    enabled: 'Включен',
    appearance: 'Вид',
    theme: 'Тема',
    language: 'Язык',
    noRuns: 'запусков нет',
    system: 'система'
  },
  en: {
    loading: 'Loading Kanitel',
    loginTitle: 'Sign in to Kanitel',
    registerTitle: 'Create account',
    login: 'Sign in',
    register: 'Create account',
    haveAccount: 'I have an account',
    needAccount: 'Create an account',
    displayName: 'Name',
    email: 'Email',
    password: 'Password',
    confirmPassword: 'Repeat password',
    currentPassword: 'Current password',
    newPassword: 'New password',
    confirmNewPassword: 'Repeat new password',
    passwordsDoNotMatch: 'Passwords do not match',
    passwordChangeFieldsRequired: 'To change the password, fill current password, new password, and repeat new password',
    avatarUrl: 'Avatar URL',
    uploadAvatar: 'Upload file',
    navBoard: 'Board',
    navProjects: 'Projects',
    navSettings: 'Settings',
    project: 'Project',
    darkThemeTitle: 'Dark theme',
    lightThemeTitle: 'Light theme',
    languageTitle: 'Переключить на русский',
    languageButton: 'RU',
    logout: 'Log out',
    schedulerInfo: (runner: string, seconds: number) => `${runner} · ${seconds}s · API /api/openapi.json`,
    newProject: 'New project',
    projectName: 'Project name',
    description: 'Description',
    create: 'Create',
    save: 'Save',
    activeProject: 'Active project',
    columns: 'Columns',
    participants: 'Participants',
    repositories: 'Repositories',
    addColumn: 'Add column',
    columnName: 'New column',
    deleteColumn: 'Delete column',
    deleteProject: 'Delete project',
    confirmDeleteProject: (name: string) => `Delete project "${name}"? All project tasks, comments, history, participants, and repositories will be removed.`,
    addParticipant: 'Add participant',
    participantType: 'Participant type',
    person: 'Person',
    agent: 'Agent',
    selectPerson: 'Select person',
    selectAgent: 'Select agent',
    remove: 'Remove',
    deleteAgent: 'Delete agent',
    envKey: 'Variable',
    envValue: 'Value',
    addEnv: 'Add variable',
    removeEnv: 'Remove variable',
    noEnv: 'No environment variables',
    repositoryName: 'Name',
    repositoryUrl: 'https:// or git@host:path.git',
    branch: 'Branch',
    authMode: 'Access',
    deleteRepository: 'Delete repository',
    addTask: 'Add task',
    assigneeFilter: 'Filter by assignee',
    allAssignees: 'All assignees',
    title: 'Title',
    assignee: 'Assignee',
    noAssignee: 'Unassigned',
    noDescription: 'No description',
    noTasks: 'Nothing here yet',
    taskDetails: 'Task',
    status: 'Status',
    author: 'Author',
    comments: 'Comments',
    history: 'History',
    noHistory: 'No history yet',
    createdTask: 'created task',
    changed: 'changed',
    emptyValue: 'empty',
    fieldTitle: 'title',
    fieldDescription: 'description',
    fieldStatus: 'status',
    fieldAssignee: 'assignee',
    commentPlaceholder: 'Comment',
    send: 'Send',
    runs: 'Runs',
    profile: 'Profile',
    globalAgents: 'Global agents',
    newAgent: 'New agent',
    agentProfile: 'Agent profile',
    newButton: 'New',
    agentName: 'Agent name',
    template: 'Template',
    avatarOrLogo: 'Avatar or logo',
    provider: 'Provider',
    model: 'Model',
    baseUrl: 'Base URL',
    apiEnv: 'API env',
    image: 'Image',
    command: 'Command',
    systemPrompt: 'System prompt',
    environment: 'Environment',
    enabled: 'Enabled',
    appearance: 'Appearance',
    theme: 'Theme',
    language: 'Language',
    noRuns: 'no runs',
    system: 'system'
  }
} as const

type Labels = (typeof labels)[Locale]

const emptyTaskDraft: TaskDraft = {
  title: '',
  description: '',
  columnId: '',
  assigneeId: '',
  initialComment: ''
}

const emptyAuthDraft: AuthDraft = {
  displayName: '',
  email: '',
  password: '',
  confirmPassword: '',
  avatarUrl: ''
}

const emptyParticipantDraft: ParticipantDraft = {
  kind: 'person',
  personId: '',
  agentId: ''
}

export default function App() {
  const [bootstrap, setBootstrap] = useState<BootstrapPayload | null>(null)
  const [currentUser, setCurrentUser] = useState<Person | null>(null)
  const [authToken, setAuthToken] = useState(() => readAuthToken())
  const [authMode, setAuthMode] = useState<'login' | 'register'>('login')
  const [authDraft, setAuthDraft] = useState<AuthDraft>(emptyAuthDraft)
  const [profileDraft, setProfileDraft] = useState<ProfileDraft>(() => emptyProfileDraft())
  const [activeProjectId, setActiveProjectId] = useState('')
  const [activeTab, setActiveTab] = useState<Tab>('board')
  const [theme, setTheme] = useState<Theme>(() => readTheme())
  const [locale, setLocale] = useState<Locale>(() => readLocale())
  const [selectedTaskId, setSelectedTaskId] = useState('')
  const [assigneeFilter, setAssigneeFilter] = useState<AssigneeFilter>(() => readAssigneeFilter())
  const [newTaskColumnId, setNewTaskColumnId] = useState('')
  const [draggedTaskId, setDraggedTaskId] = useState('')
  const [dragOverColumnId, setDragOverColumnId] = useState('')
  const [taskDraft, setTaskDraft] = useState<TaskDraft>(emptyTaskDraft)
  const [commentDraft, setCommentDraft] = useState('')
  const [projectDraft, setProjectDraft] = useState({ name: '', description: '' })
  const [projectEditDraft, setProjectEditDraft] = useState({ name: '', description: '' })
  const [columnDraft, setColumnDraft] = useState({ name: '', color: '#d89b72' })
  const [participantDraft, setParticipantDraft] = useState<ParticipantDraft>(emptyParticipantDraft)
  const [repoDraft, setRepoDraft] = useState({ name: '', url: '', branch: '', authMode: 'http' })
  const [agentDraft, setAgentDraft] = useState<AgentDraft>(() => initialAgentDraft())
  const [selectedAgentId, setSelectedAgentId] = useState('')
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const t = labels[locale]

  const refresh = useCallback(async () => {
    const payload = await loadBootstrap()
    if (readAuthToken() && !payload.currentUser) {
      storeAuthToken('')
      setAuthToken('')
      setBootstrap(null)
      setCurrentUser(null)
      return
    }

    setBootstrap(payload)
    setCurrentUser(payload.currentUser ?? null)
    if (payload.currentUser) {
      setProfileDraft(draft => ({
        displayName: draft.displayName || payload.currentUser?.displayName || '',
        email: draft.email || payload.currentUser?.email || '',
        avatarUrl: draft.avatarUrl || payload.currentUser?.avatarUrl || '',
        currentPassword: '',
        newPassword: '',
        confirmNewPassword: ''
      }))
    }
    setError('')
  }, [])

  useEffect(() => {
    document.documentElement.dataset.theme = theme
    window.localStorage.setItem('kanitel-theme', theme)
  }, [theme])

  useEffect(() => {
    document.documentElement.lang = locale
    window.localStorage.setItem('kanitel-locale', locale)
  }, [locale])

  useEffect(() => {
    window.localStorage.setItem('kanitel-assignee-filter', assigneeFilter)
  }, [assigneeFilter])

  useEffect(() => {
    if (!authToken) {
      setBootstrap(null)
      setCurrentUser(null)
      return
    }

    refresh().catch((err: Error) => {
      setError(err.message)
      storeAuthToken('')
      setAuthToken('')
    })
  }, [authToken, refresh])

  useEffect(() => {
    if (!authToken) return
    const timer = window.setInterval(() => {
      refresh().catch(() => undefined)
    }, 5000)
    return () => window.clearInterval(timer)
  }, [authToken, refresh])

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
  const participants = useMemo(
    () => state && activeProject ? buildParticipants(state, activeProject.id) : [],
    [state, activeProject]
  )
  const filteredTasks = useMemo(
    () => tasks.filter(task => matchesAssigneeFilter(task, assigneeFilter)),
    [tasks, assigneeFilter]
  )
  const selectedTask = tasks.find(task => task.id === selectedTaskId)
  const selectedAgent = state?.agents.find(agent => agent.id === selectedAgentId)

  useEffect(() => {
    if (!state || !activeProject) return
    if (assigneeFilter === 'all' || assigneeFilter === 'unassigned') return
    if (!participants.some(participant => participant.key === assigneeFilter)) {
      setAssigneeFilter('all')
    }
  }, [state, activeProject, assigneeFilter, participants])

  useEffect(() => {
    if (!activeProject) return
    setProjectEditDraft({
      name: activeProject.name,
      description: activeProject.description
    })
  }, [activeProject?.id])

  useEffect(() => {
    if (!selectedTask) return
    setTaskDraft({
      title: selectedTask.title,
      description: selectedTask.description,
      columnId: selectedTask.columnId,
      assigneeId: assigneeValue(selectedTask),
      initialComment: ''
    })
    setCommentDraft('')
  }, [selectedTaskId])

  useEffect(() => {
    if (!selectedTask || !taskDraft.title.trim()) return

    const draftMatchesTask =
      taskDraft.title === selectedTask.title &&
      taskDraft.description === selectedTask.description &&
      taskDraft.columnId === selectedTask.columnId &&
      taskDraft.assigneeId === assigneeValue(selectedTask)

    if (draftMatchesTask) return

    const timer = window.setTimeout(async () => {
      const assignment = splitAssignee(taskDraft.assigneeId)
      try {
        setError('')
        await patchJson<TaskCard>(`/api/tasks/${selectedTask.id}`, {
          title: taskDraft.title,
          description: taskDraft.description,
          columnId: taskDraft.columnId,
          assigneeAgentId: assignment.assigneeAgentId,
          assigneePersonId: assignment.assigneePersonId
        })
        await refresh()
      } catch (err) {
        setError(err instanceof Error ? err.message : String(err))
      }
    }, 700)

    return () => window.clearTimeout(timer)
  }, [
    selectedTask?.id,
    selectedTask?.title,
    selectedTask?.description,
    selectedTask?.columnId,
    selectedTask?.assigneeAgentId,
    selectedTask?.assigneePersonId,
    taskDraft.title,
    taskDraft.description,
    taskDraft.columnId,
    taskDraft.assigneeId,
    refresh
  ])

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
      environment: envToPairs(selectedAgent.environment)
    })
  }, [selectedAgent])

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === 'Escape') {
        setSelectedTaskId('')
      }
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [])

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

  async function uploadAvatar(file?: File) {
    if (!file) return ''

    setBusy(true)
    setError('')
    try {
      const result = await uploadAvatarFile(file)
      return result.url
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
      return ''
    } finally {
      setBusy(false)
    }
  }

  async function authenticate() {
    setBusy(true)
    setError('')
    try {
      if (authMode === 'register' && authDraft.password !== authDraft.confirmPassword) {
        setError(t.passwordsDoNotMatch)
        return
      }

      const path = authMode === 'login' ? '/api/auth/login' : '/api/auth/register'
      const payload = authMode === 'login'
        ? { email: authDraft.email, password: authDraft.password }
        : authDraft
      const result = await postJson<AuthResponse>(path, payload)
      storeAuthToken(result.token)
      setAuthToken(result.token)
      setCurrentUser(result.person)
      setProfileDraft(toProfileDraft(result.person))
      setAuthDraft(emptyAuthDraft)
    } catch (err) {
      setError(err instanceof Error ? err.message : String(err))
    } finally {
      setBusy(false)
    }
  }

  function logout() {
    storeAuthToken('')
    setAuthToken('')
    setCurrentUser(null)
    setBootstrap(null)
  }

  async function updateProfile() {
    const wantsPasswordChange = Boolean(
      profileDraft.currentPassword ||
      profileDraft.newPassword ||
      profileDraft.confirmNewPassword
    )

    if (wantsPasswordChange) {
      if (!profileDraft.currentPassword || !profileDraft.newPassword || !profileDraft.confirmNewPassword) {
        throw new Error(t.passwordChangeFieldsRequired)
      }

      if (profileDraft.newPassword !== profileDraft.confirmNewPassword) {
        throw new Error(t.passwordsDoNotMatch)
      }
    }

    const result = await postProfile(profileDraft)
    setCurrentUser(result.person)
    setProfileDraft(toProfileDraft(result.person))
    await refresh()
  }

  async function createProject() {
    if (!projectDraft.name.trim()) return
    await mutate(postJson<Project>('/api/projects', projectDraft))
    setProjectDraft({ name: '', description: '' })
  }

  async function saveProject() {
    if (!activeProject) return
    await mutate(patchJson<Project>(`/api/projects/${activeProject.id}`, projectEditDraft))
  }

  async function deleteProject() {
    if (!activeProject) return
    if (!window.confirm(t.confirmDeleteProject(activeProject.name))) return

    const deletedProjectId = activeProject.id
    await mutate(deleteJson(`/api/projects/${deletedProjectId}`))
    setSelectedTaskId('')
    setNewTaskColumnId('')
    setDraggedTaskId('')
    setDragOverColumnId('')
    setProjectEditDraft({ name: '', description: '' })
    setActiveProjectId(current => current === deletedProjectId ? '' : current)
  }

  async function createTask(columnId: string) {
    if (!activeProject || !taskDraft.title.trim()) return
    const assignment = splitAssignee(taskDraft.assigneeId)
    await mutate(postJson<TaskCard>(`/api/projects/${activeProject.id}/tasks`, {
      ...taskDraft,
      columnId,
      assigneeAgentId: assignment.assigneeAgentId,
      assigneePersonId: assignment.assigneePersonId,
      authorType: 'person',
      authorId: currentUser?.id ?? 'owner'
    }))
    setTaskDraft(emptyTaskDraft)
    setNewTaskColumnId('')
  }

  async function moveTask(taskId: string, columnId: string) {
    if (!activeProject) return
    const targetPosition = tasks.filter(task => task.columnId === columnId && task.id !== taskId).length
    await mutate(patchJson<TaskCard>(`/api/tasks/${taskId}`, { columnId, position: targetPosition }))
  }

  async function updateColumn(columnId: string, draft: { name: string }) {
    if (!draft.name.trim()) return
    await mutate(patchJson<BoardColumn>(`/api/columns/${columnId}`, {
      name: draft.name.trim()
    }))
  }

  async function addComment() {
    if (!selectedTask || !commentDraft.trim()) return
    await mutate(postJson(`/api/tasks/${selectedTask.id}/comments`, {
      body: commentDraft,
      authorType: 'person',
      authorId: currentUser?.id ?? 'owner'
    }))
    setCommentDraft('')
  }

  async function addParticipant() {
    if (!activeProject) return
    if (participantDraft.kind === 'agent') {
      if (!participantDraft.agentId) return
      await mutate(postJson(`/api/projects/${activeProject.id}/agents`, {
        agentId: participantDraft.agentId
      }))
      setParticipantDraft(emptyParticipantDraft)
      return
    }

    if (!participantDraft.personId) return
    await mutate(postJson(`/api/projects/${activeProject.id}/members`, {
      personId: participantDraft.personId
    }))
    setParticipantDraft(emptyParticipantDraft)
  }

  async function removeParticipant(participant: Participant) {
    await mutate(deleteJson(participant.kind === 'agent'
      ? `/api/project-agents/${participant.linkId}`
      : `/api/members/${participant.linkId}`))
  }

  async function createAgent() {
    await mutate(postJson('/api/agents', agentPayload(agentDraft)))
  }

  async function saveAgent() {
    if (!selectedAgent) return
    await mutate(patchJson(`/api/agents/${selectedAgent.id}`, agentPayload(agentDraft)))
  }

  async function deleteAgent() {
    if (!selectedAgent) return
    await mutate(deleteJson(`/api/agents/${selectedAgent.id}`))
    setSelectedAgentId('')
    setAgentDraft(initialAgentDraft(bootstrap?.providerPresets[0], bootstrap?.agentTemplates[0]))
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
      environment: envToPairs(preset.environment)
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

  if (!authToken) {
    return (
      <AuthScreen
        labels={t}
        theme={theme}
        locale={locale}
        mode={authMode}
        draft={authDraft}
        busy={busy}
        error={error}
        onModeChange={setAuthMode}
        onDraftChange={setAuthDraft}
        onAvatarFile={async (file) => {
          const avatarUrl = await uploadAvatar(file)
          if (avatarUrl) {
            setAuthDraft(draft => ({ ...draft, avatarUrl }))
          }
        }}
        onSubmit={authenticate}
        onTheme={() => setTheme(theme === 'dark' ? 'light' : 'dark')}
        onLocale={() => setLocale(locale === 'ru' ? 'en' : 'ru')}
      />
    )
  }

  if (!bootstrap || !state || !currentUser) {
    return (
      <main className="loading-page">
        <Loader2 className="spin" size={22} />
        <span>{t.loading}</span>
      </main>
    )
  }

  const selectedTaskComments = state.comments
    .filter(comment => comment.taskId === selectedTask?.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const selectedTaskHistory = (state.history ?? [])
    .filter(entry => entry.taskId === selectedTask?.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const selectedTaskRuns = state.runs
    .filter(run => run.taskId === selectedTask?.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))
  const unlinkedAgents = state.agents.filter(agent =>
    !participants.some(participant => participant.kind === 'agent' && participant.id === agent.id))

  return (
    <main className="app-shell">
      <header className="app-header">
        <div className="brand">
          <div className="brand-mark">K</div>
          <div>
            <h1>Kanitel</h1>
            <span>{t.schedulerInfo(bootstrap.scheduler.runner, bootstrap.scheduler.intervalSeconds)}</span>
          </div>
        </div>

        <nav className="main-nav" aria-label="Main">
          <button className={activeTab === 'board' ? 'active' : ''} onClick={() => setActiveTab('board')}>
            <LayoutDashboard size={17} />
            {t.navBoard}
          </button>
          <button className={activeTab === 'projects' ? 'active' : ''} onClick={() => setActiveTab('projects')}>
            <Folder size={17} />
            {t.navProjects}
          </button>
          <button className={activeTab === 'settings' ? 'active' : ''} onClick={() => setActiveTab('settings')}>
            <Settings size={17} />
            {t.navSettings}
          </button>
        </nav>

        <div className="header-actions">
          {activeProject && (
            <select value={activeProject.id} onChange={event => setActiveProjectId(event.target.value)} aria-label={t.project}>
              {projects.map(project => (
                <option key={project.id} value={project.id}>{project.name}</option>
              ))}
            </select>
          )}
          <button className="icon-button" title={theme === 'dark' ? t.lightThemeTitle : t.darkThemeTitle} onClick={() => setTheme(theme === 'dark' ? 'light' : 'dark')}>
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          <button className="language-button" title={t.languageTitle} onClick={() => setLocale(locale === 'ru' ? 'en' : 'ru')}>
            <Languages size={16} />
            {t.languageButton}
          </button>
          <div className="user-chip">
            <Avatar name={currentUser.displayName} url={currentUser.avatarUrl} />
            <span>{currentUser.displayName}</span>
          </div>
          <button className="icon-button" title={t.logout} onClick={logout}>
            <LogOut size={18} />
          </button>
        </div>
      </header>

      {error && <div className="error-line">{error}</div>}

      {activeTab === 'board' && activeProject && (
        <BoardPage
          labels={t}
          project={activeProject}
          columns={columns}
          tasks={filteredTasks}
          participants={participants}
          runs={state.runs}
          taskDraft={taskDraft}
          assigneeFilter={assigneeFilter}
          newTaskColumnId={newTaskColumnId}
          draggedTaskId={draggedTaskId}
          dragOverColumnId={dragOverColumnId}
          busy={busy}
          onOpenTask={setSelectedTaskId}
          onAssigneeFilterChange={setAssigneeFilter}
          onOpenNewTask={(columnId) => {
            setNewTaskColumnId(columnId)
            setTaskDraft({ ...emptyTaskDraft, columnId })
          }}
          onDraftChange={setTaskDraft}
          onCreateTask={createTask}
          onDragStart={setDraggedTaskId}
          onDragOverColumn={setDragOverColumnId}
          onDropTask={(columnId) => {
            const taskId = draggedTaskId
            setDraggedTaskId('')
            setDragOverColumnId('')
            if (taskId) {
              moveTask(taskId, columnId)
            }
          }}
        />
      )}

      {activeTab === 'projects' && (
        <ProjectsPage
          labels={t}
          state={state}
          activeProject={activeProject}
          projects={projects}
          participants={participants}
          unlinkedAgents={unlinkedAgents}
          projectDraft={projectDraft}
          projectEditDraft={projectEditDraft}
          columnDraft={columnDraft}
          participantDraft={participantDraft}
          repoDraft={repoDraft}
          busy={busy}
          onSelectProject={setActiveProjectId}
          onProjectDraftChange={setProjectDraft}
          onProjectEditDraftChange={setProjectEditDraft}
          onCreateProject={createProject}
          onSaveProject={saveProject}
          onDeleteProject={deleteProject}
          onColumnDraftChange={setColumnDraft}
          onAddColumn={() => {
            if (!activeProject) return
            mutate(postJson(`/api/projects/${activeProject.id}/columns`, columnDraft))
            setColumnDraft({ name: '', color: '#d89b72' })
          }}
          onUpdateColumn={updateColumn}
          onDeleteColumn={(columnId) => mutate(deleteJson(`/api/columns/${columnId}`))}
          onParticipantDraftChange={setParticipantDraft}
          onAddParticipant={addParticipant}
          onRemoveParticipant={removeParticipant}
          onRepoDraftChange={setRepoDraft}
          onAddRepository={() => {
            if (!activeProject) return
            mutate(postJson(`/api/projects/${activeProject.id}/repositories`, repoDraft))
            setRepoDraft({ name: '', url: '', branch: '', authMode: 'http' })
          }}
          onDeleteRepository={(repoId) => mutate(deleteJson(`/api/repositories/${repoId}`))}
        />
      )}

      {activeTab === 'settings' && (
        <SettingsPage
          labels={t}
          theme={theme}
          locale={locale}
          profileDraft={profileDraft}
          agents={state.agents}
          selectedAgentId={selectedAgentId}
          selectedAgent={selectedAgent}
          agentDraft={agentDraft}
          presets={bootstrap.providerPresets}
          templates={bootstrap.agentTemplates}
          busy={busy}
          onThemeChange={setTheme}
          onLocaleChange={setLocale}
          onProfileDraftChange={setProfileDraft}
          onProfileAvatarFile={async (file) => {
            const avatarUrl = await uploadAvatar(file)
            if (avatarUrl) {
              setProfileDraft(draft => ({ ...draft, avatarUrl }))
            }
          }}
          onSaveProfile={() => mutate(updateProfile())}
          onSelectAgent={setSelectedAgentId}
          onNewAgent={() => {
            setSelectedAgentId('')
            setAgentDraft(initialAgentDraft(bootstrap.providerPresets[0], bootstrap.agentTemplates[0]))
          }}
          onAgentDraftChange={setAgentDraft}
          onAgentAvatarFile={async (file) => {
            const avatarUrl = await uploadAvatar(file)
            if (avatarUrl) {
              setAgentDraft(draft => ({ ...draft, avatarUrl }))
            }
          }}
          onPreset={applyPresetToDraft}
          onTemplate={applyTemplateToDraft}
          onCreateAgent={createAgent}
          onSaveAgent={saveAgent}
          onDeleteAgent={deleteAgent}
        />
      )}

      {selectedTask && (
        <TaskModal
          labels={t}
          locale={locale}
          task={selectedTask}
          draft={taskDraft}
          columns={columns}
          participants={participants}
          comments={selectedTaskComments}
          history={selectedTaskHistory}
          runs={selectedTaskRuns}
          agents={state.agents}
          people={state.people}
          busy={busy}
          commentDraft={commentDraft}
          onClose={() => setSelectedTaskId('')}
          onDraftChange={setTaskDraft}
          onCommentDraftChange={setCommentDraft}
          onAddComment={addComment}
        />
      )}
    </main>
  )
}

function AuthScreen({
  labels: t,
  theme,
  locale,
  mode,
  draft,
  busy,
  error,
  onModeChange,
  onDraftChange,
  onAvatarFile,
  onSubmit,
  onTheme,
  onLocale
}: {
  labels: Labels
  theme: Theme
  locale: Locale
  mode: 'login' | 'register'
  draft: AuthDraft
  busy: boolean
  error: string
  onModeChange: (mode: 'login' | 'register') => void
  onDraftChange: (draft: AuthDraft) => void
  onAvatarFile: (file?: File) => void
  onSubmit: () => void
  onTheme: () => void
  onLocale: () => void
}) {
  return (
    <main className="auth-page">
      <section className="auth-panel">
        <div className="auth-brand">
          <div className="brand-mark">K</div>
          <div>
            <h1>Kanitel</h1>
            <p>{mode === 'login' ? t.loginTitle : t.registerTitle}</p>
          </div>
        </div>

        <div className="auth-actions">
          <button className="icon-button" title={theme === 'dark' ? t.lightThemeTitle : t.darkThemeTitle} onClick={onTheme}>
            {theme === 'dark' ? <Sun size={18} /> : <Moon size={18} />}
          </button>
          <button className="language-button" title={t.languageTitle} onClick={onLocale}>
            <Languages size={16} />
            {locale === 'ru' ? 'EN' : 'RU'}
          </button>
        </div>

        <div className="stack-form">
          {mode === 'register' && (
            <>
              <label>
                {t.displayName}
                <input value={draft.displayName} onChange={event => onDraftChange({ ...draft, displayName: event.target.value })} />
              </label>
              <AvatarField
                labels={t}
                name={draft.displayName || t.displayName}
                value={draft.avatarUrl}
                onChange={avatarUrl => onDraftChange({ ...draft, avatarUrl })}
                onFile={onAvatarFile}
              />
            </>
          )}
          <label>
            {t.email}
            <input value={draft.email} onChange={event => onDraftChange({ ...draft, email: event.target.value })} />
          </label>
          <label>
            {t.password}
            <input type="password" value={draft.password} onChange={event => onDraftChange({ ...draft, password: event.target.value })} />
          </label>
          {mode === 'register' && (
            <label>
              {t.confirmPassword}
              <input type="password" value={draft.confirmPassword} onChange={event => onDraftChange({ ...draft, confirmPassword: event.target.value })} />
            </label>
          )}
          {error && <div className="error-line compact-error">{error}</div>}
          <button className="primary-button full" onClick={onSubmit} disabled={busy}>
            <LogIn size={16} />
            {mode === 'login' ? t.login : t.register}
          </button>
          <button className="secondary-button full" onClick={() => onModeChange(mode === 'login' ? 'register' : 'login')}>
            {mode === 'login' ? t.needAccount : t.haveAccount}
          </button>
        </div>
      </section>
    </main>
  )
}

function BoardPage({
  labels: t,
  project,
  columns,
  tasks,
  participants,
  runs,
  taskDraft,
  assigneeFilter,
  newTaskColumnId,
  draggedTaskId,
  dragOverColumnId,
  busy,
  onOpenTask,
  onAssigneeFilterChange,
  onOpenNewTask,
  onDraftChange,
  onCreateTask,
  onDragStart,
  onDragOverColumn,
  onDropTask
}: {
  labels: Labels
  project: Project
  columns: BoardColumn[]
  tasks: TaskCard[]
  participants: Participant[]
  runs: Array<{ taskId: string; status: string; createdAt: string }>
  taskDraft: TaskDraft
  assigneeFilter: AssigneeFilter
  newTaskColumnId: string
  draggedTaskId: string
  dragOverColumnId: string
  busy: boolean
  onOpenTask: (id: string) => void
  onAssigneeFilterChange: (filter: AssigneeFilter) => void
  onOpenNewTask: (columnId: string) => void
  onDraftChange: (draft: TaskDraft) => void
  onCreateTask: (columnId: string) => void
  onDragStart: (id: string) => void
  onDragOverColumn: (id: string) => void
  onDropTask: (columnId: string) => void
}) {
  const activeFilterParticipant = participants.find(participant => participant.key === assigneeFilter)
  const filterLabel = activeFilterParticipant?.name
    ?? (assigneeFilter === 'unassigned' ? t.noAssignee : t.allAssignees)

  return (
    <section className="board-page">
      <div className="page-heading">
        <div>
          <h2>{project.name}</h2>
          <p>{project.description || t.noDescription}</p>
        </div>
        <div className="board-heading-actions">
          <div className="board-filter" aria-label={t.assigneeFilter}>
            <span className="filter-caption">{t.assigneeFilter}</span>
            <div className="filter-control">
              {activeFilterParticipant ? (
                <Avatar name={activeFilterParticipant.name} url={activeFilterParticipant.avatarUrl} />
              ) : (
                <span className="filter-all-avatar">{assigneeFilter === 'unassigned' ? '-' : '*'}</span>
              )}
              <strong>{filterLabel}</strong>
              <span className="filter-chevron">⌄</span>
            </div>
            <select value={assigneeFilter} onChange={event => onAssigneeFilterChange(event.target.value)}>
              <option value="all">{t.allAssignees}</option>
              <option value="unassigned">{t.noAssignee}</option>
              {participants.map(participant => (
                <option key={participant.key} value={participant.key}>{participant.name}</option>
              ))}
            </select>
          </div>
          <span className="board-task-count">{tasks.length}</span>
        </div>
      </div>

      <div className="board-scroll">
        {columns.map(column => (
          <KanbanColumn
            key={column.id}
            labels={t}
            column={column}
            tasks={tasks.filter(task => task.columnId === column.id)}
            participants={participants}
            runs={runs}
            taskDraft={taskDraft}
            newTaskColumnId={newTaskColumnId}
            draggedTaskId={draggedTaskId}
            isDragOver={dragOverColumnId === column.id}
            busy={busy}
            onOpenTask={onOpenTask}
            onOpenNewTask={() => onOpenNewTask(column.id)}
            onDraftChange={onDraftChange}
            onCreateTask={() => onCreateTask(column.id)}
            onDragStart={onDragStart}
            onDragOver={() => onDragOverColumn(column.id)}
            onDrop={() => onDropTask(column.id)}
          />
        ))}
      </div>
    </section>
  )
}

function KanbanColumn({
  labels: t,
  column,
  tasks,
  participants,
  runs,
  taskDraft,
  newTaskColumnId,
  draggedTaskId,
  isDragOver,
  busy,
  onOpenTask,
  onOpenNewTask,
  onDraftChange,
  onCreateTask,
  onDragStart,
  onDragOver,
  onDrop
}: {
  labels: Labels
  column: BoardColumn
  tasks: TaskCard[]
  participants: Participant[]
  runs: Array<{ taskId: string; status: string; createdAt: string }>
  taskDraft: TaskDraft
  newTaskColumnId: string
  draggedTaskId: string
  isDragOver: boolean
  busy: boolean
  onOpenTask: (id: string) => void
  onOpenNewTask: () => void
  onDraftChange: (draft: TaskDraft) => void
  onCreateTask: () => void
  onDragStart: (id: string) => void
  onDragOver: () => void
  onDrop: () => void
}) {
  return (
    <section
      className={`column ${isDragOver ? 'drop-target' : ''}`}
      onDragOver={event => {
        event.preventDefault()
        onDragOver()
      }}
      onDrop={event => {
        event.preventDefault()
        onDrop()
      }}
    >
      <header className="column-header" style={{ borderTopColor: column.color }}>
        <div>
          <h2>{column.name}</h2>
          <span>{tasks.length}{column.wipLimit ? ` / ${column.wipLimit}` : ''}</span>
        </div>
        <button className="icon-button compact" title={t.addTask} onClick={onOpenNewTask}>
          <Plus size={16} />
        </button>
      </header>

      {newTaskColumnId === column.id && (
        <div className="inline-form">
          <input value={taskDraft.title} onChange={event => onDraftChange({ ...taskDraft, title: event.target.value })} placeholder={t.title} />
          <textarea value={taskDraft.description} onChange={event => onDraftChange({ ...taskDraft, description: event.target.value })} placeholder={t.description} rows={3} />
          <AssigneePicker
            labels={t}
            value={taskDraft.assigneeId}
            participants={participants}
            onChange={assigneeId => onDraftChange({ ...taskDraft, assigneeId })}
          />
          <button className="primary-button" onClick={onCreateTask} disabled={busy}>
            <Plus size={16} />
            {t.create}
          </button>
        </div>
      )}

      <div className="task-list">
        {tasks.length === 0 && <div className="empty-column">{t.noTasks}</div>}
        {tasks.map(task => (
          <button
            key={task.id}
            className={`task-card ${draggedTaskId === task.id ? 'dragging' : ''}`}
            draggable
            onDragStart={event => {
              event.dataTransfer.effectAllowed = 'move'
              onDragStart(task.id)
            }}
            onDragEnd={() => onDragStart('')}
            onClick={() => onOpenTask(task.id)}
          >
            <strong>{task.title}</strong>
            <span className="task-description">{task.description || t.noDescription}</span>
            <TaskMeta labels={t} task={task} participants={participants} runs={runs} />
            <GripVertical className="drag-handle" size={16} />
          </button>
        ))}
      </div>
    </section>
  )
}

function TaskModal({
  labels: t,
  locale,
  task,
  draft,
  columns,
  participants,
  comments,
  history,
  runs,
  agents,
  people,
  busy,
  commentDraft,
  onClose,
  onDraftChange,
  onCommentDraftChange,
  onAddComment
}: {
  labels: Labels
  locale: Locale
  task: TaskCard
  draft: TaskDraft
  columns: BoardColumn[]
  participants: Participant[]
  comments: TaskComment[]
  history: TaskHistoryEntry[]
  runs: Array<{ id: string; status: string; createdAt: string; finishedAt?: string | null; log: string }>
  agents: AgentProfile[]
  people: Person[]
  busy: boolean
  commentDraft: string
  onClose: () => void
  onDraftChange: (draft: TaskDraft) => void
  onCommentDraftChange: (value: string) => void
  onAddComment: () => void
}) {
  const [activityTab, setActivityTab] = useState<'comments' | 'history'>('comments')
  const authorComment = comments.reduce<TaskComment | null>((oldest, comment) => {
    if (!oldest) return comment
    return comment.createdAt.localeCompare(oldest.createdAt) < 0 ? comment : oldest
  }, null)

  return (
    <div className="modal-backdrop" onMouseDown={onClose}>
      <section className="task-modal" onMouseDown={event => event.stopPropagation()}>
        <header className="modal-header">
          <div>
            <span>{t.taskDetails}</span>
            <h2>{task.title}</h2>
          </div>
          <button className="icon-button modal-close-button" onClick={onClose}>
            <X size={18} />
          </button>
        </header>

        <div className="task-modal-body">
          <div className="task-edit">
            <label>
              {t.title}
              <input value={draft.title} onChange={event => onDraftChange({ ...draft, title: event.target.value })} />
            </label>
            <label>
              {t.description}
              <textarea value={draft.description} onChange={event => onDraftChange({ ...draft, description: event.target.value })} rows={6} />
            </label>
            <section className="comments task-comments">
              <div className="activity-tabs" role="tablist" aria-label={t.comments}>
                <button className={activityTab === 'comments' ? 'active' : ''} onClick={() => setActivityTab('comments')} type="button">
                  <MessageSquare size={16} />
                  {t.comments}
                </button>
                <button className={activityTab === 'history' ? 'active' : ''} onClick={() => setActivityTab('history')} type="button">
                  <HistoryIcon size={16} />
                  {t.history}
                </button>
              </div>

              {activityTab === 'comments' ? (
                <>
                  <div className="comment-form">
                    <textarea value={commentDraft} onChange={event => onCommentDraftChange(event.target.value)} placeholder={t.commentPlaceholder} rows={3} />
                    <button className="secondary-button" onClick={onAddComment} disabled={busy}>
                      <MessageSquare size={16} />
                      {t.send}
                    </button>
                  </div>
                  {comments.map(comment => (
                    <article className={`comment ${comment.authorType}`} key={comment.id}>
                      <div>
                        <AuthorBadge comment={comment} agents={agents} people={people} labels={t} />
                        <time>{formatDate(comment.createdAt, locale)}</time>
                      </div>
                      <p>{comment.body}</p>
                    </article>
                  ))}
                </>
              ) : (
                <div className="history-list">
                  {history.length === 0 && <p className="muted">{t.noHistory}</p>}
                  {history.map(entry => (
                    <article className="history-entry" key={entry.id}>
                      <div>
                        <ActorBadge authorType={entry.authorType} authorId={entry.authorId} agents={agents} people={people} labels={t} />
                        <time>{formatDate(entry.createdAt, locale)}</time>
                      </div>
                      <HistoryEntryBody entry={entry} labels={t} />
                    </article>
                  ))}
                </div>
              )}
            </section>
          </div>

          <aside className="task-side-panel">
            <label>
              {t.status}
              <select value={draft.columnId} onChange={event => onDraftChange({ ...draft, columnId: event.target.value })}>
                {columns.map(column => (
                  <option key={column.id} value={column.id}>{column.name}</option>
                ))}
              </select>
            </label>
            <label>
              {t.assignee}
              <AssigneePicker
                labels={t}
                value={draft.assigneeId}
                participants={participants}
                onChange={assigneeId => onDraftChange({ ...draft, assigneeId })}
              />
            </label>
            <div className="task-author">
              <span>{t.author}</span>
              {authorComment ? (
                <AuthorBadge comment={authorComment} agents={agents} people={people} labels={t} />
              ) : (
                <strong>{t.system}</strong>
              )}
            </div>
            <section className="runs-panel">
              <h3><CheckCircle2 size={16} />{t.runs}</h3>
              {runs.length === 0 && <p className="muted">{t.noRuns}</p>}
              {runs.slice(0, 4).map(run => (
                <div className="run-row" key={run.id}>
                  <strong>{run.status}</strong>
                  <span>{formatDate(run.finishedAt || run.createdAt, locale)}</span>
                </div>
              ))}
            </section>
          </aside>
        </div>
      </section>
    </div>
  )
}

function ColumnSettingsRow({
  labels: t,
  column,
  busy,
  onUpdateColumn,
  onDeleteColumn
}: {
  labels: Labels
  column: BoardColumn
  busy: boolean
  onUpdateColumn: (columnId: string, draft: { name: string }) => void
  onDeleteColumn: (columnId: string) => void
}) {
  const [name, setName] = useState(column.name)

  useEffect(() => {
    setName(column.name)
  }, [column.id, column.name])

  const trimmedName = name.trim()
  const canSave = Boolean(trimmedName) && trimmedName !== column.name

  function saveColumnName() {
    if (!canSave) {
      setName(column.name)
      return
    }

    onUpdateColumn(column.id, { name: trimmedName })
  }

  return (
    <div className="settings-row column-settings-row">
      <span className="color-dot" style={{ background: column.color }} />
      <input
        value={name}
        onChange={event => setName(event.target.value)}
        onKeyDown={event => {
          if (event.key === 'Enter') {
            saveColumnName()
          }

          if (event.key === 'Escape') {
            setName(column.name)
          }
        }}
        aria-label={t.columnName}
      />
      <button className="icon-button compact" title={t.save} onClick={saveColumnName} disabled={busy || !canSave}>
        <Save size={15} />
      </button>
      <button className="icon-button compact" title={t.deleteColumn} onClick={() => onDeleteColumn(column.id)} disabled={busy}>
        <Trash2 size={15} />
      </button>
    </div>
  )
}

function ProjectsPage({
  labels: t,
  state,
  activeProject,
  projects,
  participants,
  unlinkedAgents,
  projectDraft,
  projectEditDraft,
  columnDraft,
  participantDraft,
  repoDraft,
  busy,
  onSelectProject,
  onProjectDraftChange,
  onProjectEditDraftChange,
  onCreateProject,
  onSaveProject,
  onDeleteProject,
  onColumnDraftChange,
  onAddColumn,
  onUpdateColumn,
  onDeleteColumn,
  onParticipantDraftChange,
  onAddParticipant,
  onRemoveParticipant,
  onRepoDraftChange,
  onAddRepository,
  onDeleteRepository
}: {
  labels: Labels
  state: KanitelState
  activeProject?: Project
  projects: Project[]
  participants: Participant[]
  unlinkedAgents: AgentProfile[]
  projectDraft: { name: string; description: string }
  projectEditDraft: { name: string; description: string }
  columnDraft: { name: string; color: string }
  participantDraft: ParticipantDraft
  repoDraft: { name: string; url: string; branch: string; authMode: string }
  busy: boolean
  onSelectProject: (id: string) => void
  onProjectDraftChange: (draft: { name: string; description: string }) => void
  onProjectEditDraftChange: (draft: { name: string; description: string }) => void
  onCreateProject: () => void
  onSaveProject: () => void
  onDeleteProject: () => void
  onColumnDraftChange: (draft: { name: string; color: string }) => void
  onAddColumn: () => void
  onUpdateColumn: (columnId: string, draft: { name: string }) => void
  onDeleteColumn: (columnId: string) => void
  onParticipantDraftChange: (draft: ParticipantDraft) => void
  onAddParticipant: () => void
  onRemoveParticipant: (participant: Participant) => void
  onRepoDraftChange: (draft: { name: string; url: string; branch: string; authMode: string }) => void
  onAddRepository: () => void
  onDeleteRepository: (repoId: string) => void
}) {
  const columns = activeProject ? state.columns.filter(column => column.projectId === activeProject.id).sort((a, b) => a.position - b.position) : []
  const repositories = activeProject ? state.repositories.filter(repo => repo.projectId === activeProject.id) : []

  return (
    <section className="projects-page">
      <aside className="project-list">
        <div className="page-heading compact">
          <h2>{t.navProjects}</h2>
          <span>{projects.length}</span>
        </div>
        {projects.map(project => (
          <button key={project.id} className={activeProject?.id === project.id ? 'project-card active' : 'project-card'} onClick={() => onSelectProject(project.id)}>
            <strong>{project.name}</strong>
            <span>{project.description || t.noDescription}</span>
          </button>
        ))}
        <Panel title={t.newProject} icon={<Plus size={17} />}>
          <div className="stack-form">
            <input value={projectDraft.name} onChange={event => onProjectDraftChange({ ...projectDraft, name: event.target.value })} placeholder={t.projectName} />
            <textarea value={projectDraft.description} onChange={event => onProjectDraftChange({ ...projectDraft, description: event.target.value })} placeholder={t.description} rows={3} />
            <button className="primary-button full" onClick={onCreateProject} disabled={busy}>
              <Plus size={16} />
              {t.create}
            </button>
          </div>
        </Panel>
      </aside>

      {activeProject && (
        <div className="project-settings">
          <Panel title={t.activeProject} icon={<Folder size={17} />}>
            <div className="form-grid two">
              <input value={projectEditDraft.name} onChange={event => onProjectEditDraftChange({ ...projectEditDraft, name: event.target.value })} placeholder={t.projectName} />
              <input value={projectEditDraft.description} onChange={event => onProjectEditDraftChange({ ...projectEditDraft, description: event.target.value })} placeholder={t.description} />
            </div>
            <div className="button-row">
              <button className="danger-button" onClick={onDeleteProject} disabled={busy}>
                <Trash2 size={16} />
                {t.deleteProject}
              </button>
              <button className="primary-button" onClick={onSaveProject} disabled={busy}>
                <Save size={16} />
                {t.save}
              </button>
            </div>
          </Panel>

          <div className="settings-grid">
            <Panel title={t.columns} icon={<LayoutDashboard size={17} />}>
              <div className="row-list">
                {columns.map(column => (
                  <ColumnSettingsRow
                    key={column.id}
                    labels={t}
                    column={column}
                    busy={busy}
                    onUpdateColumn={onUpdateColumn}
                    onDeleteColumn={onDeleteColumn}
                  />
                ))}
                <div className="settings-row add-row">
                  <input className="color-input" type="color" value={columnDraft.color} onChange={event => onColumnDraftChange({ ...columnDraft, color: event.target.value })} />
                  <input value={columnDraft.name} onChange={event => onColumnDraftChange({ ...columnDraft, name: event.target.value })} placeholder={t.columnName} />
                  <button className="icon-button compact" title={t.addColumn} onClick={onAddColumn}>
                    <Plus size={15} />
                  </button>
                </div>
              </div>
            </Panel>

            <Panel title={t.participants} icon={<Users size={17} />}>
              <div className="row-list">
                {participants.map(participant => (
                  <div className="participant-row" key={participant.key}>
                    <Avatar name={participant.name} url={participant.avatarUrl} />
                    <span>
                      <strong>{participant.name}</strong>
                      <em>{participant.kind === 'agent' ? t.agent : participant.email || t.person}</em>
                    </span>
                    <button className="icon-button compact" title={t.remove} onClick={() => onRemoveParticipant(participant)}>
                      <Trash2 size={15} />
                    </button>
                  </div>
                ))}
                <div className="participant-form">
                  <label>
                    {t.participantType}
                    <select value={participantDraft.kind} onChange={event => onParticipantDraftChange({ ...participantDraft, kind: event.target.value as ParticipantKind })}>
                      <option value="person">{t.person}</option>
                      <option value="agent">{t.agent}</option>
                    </select>
                  </label>
                  {participantDraft.kind === 'person' ? (
                    <label className="participant-wide">
                      {t.selectPerson}
                      <select value={participantDraft.personId} onChange={event => onParticipantDraftChange({ ...participantDraft, personId: event.target.value })}>
                        <option value="">{t.selectPerson}</option>
                        {state.people.map(person => (
                          <option key={person.id} value={person.id}>{person.displayName}</option>
                        ))}
                      </select>
                      {(() => {
                        const person = state.people.find(item => item.id === participantDraft.personId)
                        return (
                          <IdentityBadge
                            name={person?.displayName}
                            avatarUrl={person?.avatarUrl}
                            detail={person?.email}
                            fallback={t.selectPerson}
                          />
                        )
                      })()}
                    </label>
                  ) : (
                    <label className="participant-wide">
                      {t.selectAgent}
                      <select value={participantDraft.agentId} onChange={event => onParticipantDraftChange({ ...participantDraft, agentId: event.target.value })}>
                        <option value="">{t.selectAgent}</option>
                        {unlinkedAgents.map(agent => (
                          <option key={agent.id} value={agent.id}>{agent.name}</option>
                        ))}
                      </select>
                      {(() => {
                        const agent = unlinkedAgents.find(item => item.id === participantDraft.agentId)
                        return (
                          <IdentityBadge
                            name={agent?.name}
                            avatarUrl={agent?.avatarUrl}
                            detail={agent?.providerPresetId}
                            fallback={t.selectAgent}
                          />
                        )
                      })()}
                    </label>
                  )}
                  <button className="secondary-button" onClick={onAddParticipant} disabled={busy}>
                    <UserPlus size={16} />
                    {t.addParticipant}
                  </button>
                </div>
              </div>
            </Panel>

            <Panel title={t.repositories} icon={<GitBranch size={17} />}>
              <div className="row-list">
                {repositories.map(repo => (
                  <div className="repo-row" key={repo.id}>
                    <strong>{repo.name}</strong>
                    <span>{repo.url}</span>
                    <em>{repo.branch || repo.authMode}</em>
                    <button className="icon-button compact" title={t.deleteRepository} onClick={() => onDeleteRepository(repo.id)}>
                      <Trash2 size={15} />
                    </button>
                  </div>
                ))}
                <div className="stack-form">
                  <input value={repoDraft.name} onChange={event => onRepoDraftChange({ ...repoDraft, name: event.target.value })} placeholder={t.repositoryName} />
                  <input value={repoDraft.url} onChange={event => onRepoDraftChange({ ...repoDraft, url: event.target.value })} placeholder={t.repositoryUrl} />
                  <div className="form-grid two">
                    <input value={repoDraft.branch} onChange={event => onRepoDraftChange({ ...repoDraft, branch: event.target.value })} placeholder={t.branch} />
                    <select value={repoDraft.authMode} onChange={event => onRepoDraftChange({ ...repoDraft, authMode: event.target.value })}>
                      <option value="http">http</option>
                      <option value="ssh">ssh</option>
                    </select>
                  </div>
                  <button className="secondary-button" onClick={onAddRepository} disabled={busy}>
                    <Plus size={16} />
                    {t.create}
                  </button>
                </div>
              </div>
            </Panel>
          </div>
        </div>
      )}
    </section>
  )
}

function SettingsPage({
  labels: t,
  theme,
  locale,
  profileDraft,
  agents,
  selectedAgentId,
  selectedAgent,
  agentDraft,
  presets,
  templates,
  busy,
  onThemeChange,
  onLocaleChange,
  onProfileDraftChange,
  onProfileAvatarFile,
  onSaveProfile,
  onSelectAgent,
  onNewAgent,
  onAgentDraftChange,
  onAgentAvatarFile,
  onPreset,
  onTemplate,
  onCreateAgent,
  onSaveAgent,
  onDeleteAgent
}: {
  labels: Labels
  theme: Theme
  locale: Locale
  profileDraft: ProfileDraft
  agents: AgentProfile[]
  selectedAgentId: string
  selectedAgent?: AgentProfile
  agentDraft: AgentDraft
  presets: ProviderPreset[]
  templates: AgentTemplate[]
  busy: boolean
  onThemeChange: (theme: Theme) => void
  onLocaleChange: (locale: Locale) => void
  onProfileDraftChange: (draft: ProfileDraft) => void
  onProfileAvatarFile: (file?: File) => void
  onSaveProfile: () => void
  onSelectAgent: (id: string) => void
  onNewAgent: () => void
  onAgentDraftChange: (draft: AgentDraft) => void
  onAgentAvatarFile: (file?: File) => void
  onPreset: (presetId: string) => void
  onTemplate: (templateId: string) => void
  onCreateAgent: () => void
  onSaveAgent: () => void
  onDeleteAgent: () => void
}) {
  return (
    <section className="settings-page">
      <div className="settings-grid top">
        <Panel title={t.profile} icon={<Users size={17} />}>
          <div className="form-grid two">
            <label>
              {t.displayName}
              <input value={profileDraft.displayName} onChange={event => onProfileDraftChange({ ...profileDraft, displayName: event.target.value })} />
            </label>
            <label>
              {t.email}
              <input value={profileDraft.email} onChange={event => onProfileDraftChange({ ...profileDraft, email: event.target.value })} />
            </label>
          </div>
          <AvatarField
            labels={t}
            name={profileDraft.displayName || t.profile}
            value={profileDraft.avatarUrl}
            onChange={avatarUrl => onProfileDraftChange({ ...profileDraft, avatarUrl })}
            onFile={onProfileAvatarFile}
          />
          <div className="form-grid two">
            <label>
              {t.currentPassword}
              <input type="password" value={profileDraft.currentPassword} onChange={event => onProfileDraftChange({ ...profileDraft, currentPassword: event.target.value })} />
            </label>
            <label>
              {t.newPassword}
              <input type="password" value={profileDraft.newPassword} onChange={event => onProfileDraftChange({ ...profileDraft, newPassword: event.target.value })} />
            </label>
          </div>
          <label>
            {t.confirmNewPassword}
            <input type="password" value={profileDraft.confirmNewPassword} onChange={event => onProfileDraftChange({ ...profileDraft, confirmNewPassword: event.target.value })} />
          </label>
          <button className="primary-button" onClick={onSaveProfile} disabled={busy}>
            <Save size={16} />
            {t.save}
          </button>
        </Panel>

        <Panel title={t.appearance} icon={<Settings size={17} />}>
          <div className="appearance-form">
            <label>
              {t.theme}
              <select value={theme} onChange={event => onThemeChange(event.target.value as Theme)}>
                <option value="light">{t.lightThemeTitle}</option>
                <option value="dark">{t.darkThemeTitle}</option>
              </select>
            </label>
            <label>
              {t.language}
              <select value={locale} onChange={event => onLocaleChange(event.target.value as Locale)}>
                <option value="ru">Русский</option>
                <option value="en">English</option>
              </select>
            </label>
          </div>
        </Panel>
      </div>

      <Panel title={t.globalAgents} icon={<Bot size={17} />}>
        <section className="agents-layout">
          <div className="agent-list">
            {agents.map(agent => (
              <button key={agent.id} className={selectedAgentId === agent.id ? 'agent-item active' : 'agent-item'} onClick={() => onSelectAgent(agent.id)}>
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
              <h2>{selectedAgent ? t.agentProfile : t.newAgent}</h2>
              <button className="secondary-button" onClick={onNewAgent}>
                <Plus size={16} />
                {t.newButton}
              </button>
            </div>
            <AgentForm
              labels={t}
              draft={agentDraft}
              presets={presets}
              templates={templates}
              onChange={onAgentDraftChange}
              onAvatarFile={onAgentAvatarFile}
              onPreset={onPreset}
              onTemplate={onTemplate}
            />
            <div className="button-row">
              {selectedAgent ? (
                <>
                  <button className="danger-button" onClick={onDeleteAgent} disabled={busy}>
                    <Trash2 size={16} />
                    {t.deleteAgent}
                  </button>
                  <button className="primary-button" onClick={onSaveAgent} disabled={busy}>
                    <Save size={16} />
                    {t.save}
                  </button>
                </>
              ) : (
                <button className="primary-button" onClick={onCreateAgent} disabled={busy}>
                  <Plus size={16} />
                  {t.create}
                </button>
              )}
            </div>
          </div>
        </section>
      </Panel>
    </section>
  )
}

function AgentForm({
  labels: t,
  draft,
  presets,
  templates,
  onChange,
  onAvatarFile,
  onPreset,
  onTemplate
}: {
  labels: Labels
  draft: AgentDraft
  presets: ProviderPreset[]
  templates: AgentTemplate[]
  onChange: (draft: AgentDraft) => void
  onAvatarFile: (file?: File) => void
  onPreset: (presetId: string) => void
  onTemplate: (templateId: string) => void
}) {
  return (
    <div className="agent-form">
      <div className="form-grid two">
        <label>
          {t.agentName}
          <input value={draft.name} onChange={event => onChange({ ...draft, name: event.target.value })} />
        </label>
        <label>
          {t.template}
          <select value={draft.templateId} onChange={event => onTemplate(event.target.value)}>
            {templates.map(template => (
              <option key={template.id} value={template.id}>{template.name}</option>
            ))}
          </select>
        </label>
      </div>
      <AvatarField
        labels={t}
        label={t.avatarOrLogo}
        name={draft.name || t.agent}
        value={draft.avatarUrl}
        onChange={avatarUrl => onChange({ ...draft, avatarUrl })}
        onFile={onAvatarFile}
      />
      <div className="form-grid two">
        <label>
          {t.provider}
          <select value={draft.providerPresetId} onChange={event => onPreset(event.target.value)}>
            {presets.map(preset => (
              <option key={preset.id} value={preset.id}>{preset.name}</option>
            ))}
          </select>
        </label>
        <label>
          {t.model}
          <input value={draft.model} onChange={event => onChange({ ...draft, model: event.target.value })} />
        </label>
      </div>
      <label>
        {t.baseUrl}
        <input value={draft.baseUrl} onChange={event => onChange({ ...draft, baseUrl: event.target.value })} />
      </label>
      <div className="form-grid two">
        <label>
          {t.apiEnv}
          <input value={draft.apiKeyEnvName} onChange={event => onChange({ ...draft, apiKeyEnvName: event.target.value })} />
        </label>
        <label>
          {t.image}
          <input value={draft.containerImage} onChange={event => onChange({ ...draft, containerImage: event.target.value })} />
        </label>
      </div>
      <label>
        {t.command}
        <textarea value={draft.commandTemplate} onChange={event => onChange({ ...draft, commandTemplate: event.target.value })} rows={3} />
      </label>
      <label>
        {t.systemPrompt}
        <textarea value={draft.systemPrompt} onChange={event => onChange({ ...draft, systemPrompt: event.target.value })} rows={5} />
      </label>
      <EnvEditor labels={t} draft={draft} onChange={onChange} />
      <label className="toggle-row">
        <input type="checkbox" checked={draft.enabled} onChange={event => onChange({ ...draft, enabled: event.target.checked })} />
        {t.enabled}
      </label>
    </div>
  )
}

function EnvEditor({
  labels: t,
  draft,
  onChange
}: {
  labels: Labels
  draft: AgentDraft
  onChange: (draft: AgentDraft) => void
}) {
  const rows = draft.environment

  function updateRow(id: string, patch: Partial<EnvPair>) {
    onChange({
      ...draft,
      environment: rows.map(row => row.id === id ? { ...row, ...patch } : row)
    })
  }

  function removeRow(id: string) {
    onChange({
      ...draft,
      environment: rows.filter(row => row.id !== id)
    })
  }

  return (
    <section className="env-editor">
      <header>
        <strong>{t.environment}</strong>
        <button className="secondary-button compact-text" type="button" onClick={() => onChange({ ...draft, environment: [...rows, newEnvPair()] })}>
          <Plus size={15} />
          {t.addEnv}
        </button>
      </header>
      <div className="env-list">
        {rows.length === 0 && (
          <div className="empty-column">{t.noEnv}</div>
        )}
        {rows.map(row => (
          <div className="env-row" key={row.id}>
            <label>
              {t.envKey}
              <input value={row.key} onChange={event => updateRow(row.id, { key: event.target.value })} placeholder="OPENAI_API_KEY" />
            </label>
            <label>
              {t.envValue}
              <input value={row.value} onChange={event => updateRow(row.id, { value: event.target.value })} placeholder="..." />
            </label>
            <button className="icon-button compact env-remove" type="button" title={t.removeEnv} onClick={() => removeRow(row.id)}>
              <Trash2 size={15} />
            </button>
          </div>
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

function AvatarField({
  labels: t,
  label,
  name,
  value,
  onChange,
  onFile
}: {
  labels: Labels
  label?: string
  name: string
  value: string
  onChange: (value: string) => void
  onFile: (file?: File) => void
}) {
  return (
    <div className="avatar-field">
      <Avatar name={name} url={value} />
      <label>
        {label ?? t.avatarUrl}
        <input value={value} onChange={event => onChange(event.target.value)} placeholder="https://..." />
      </label>
      <label className="secondary-button avatar-upload-button">
        <Upload size={16} />
        {t.uploadAvatar}
        <input
          type="file"
          accept="image/png,image/jpeg,image/webp,image/gif"
          onChange={event => {
            onFile(event.target.files?.[0])
            event.currentTarget.value = ''
          }}
        />
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

function IdentityBadge({
  name,
  avatarUrl,
  detail,
  fallback
}: {
  name?: string
  avatarUrl?: string | null
  detail?: string
  fallback: string
}) {
  if (!name) {
    return <span className="identity-badge empty">{fallback}</span>
  }

  return (
    <span className="identity-badge">
      <Avatar name={name} url={avatarUrl} />
      <span>
        <strong>{name}</strong>
        {detail && <em>{detail}</em>}
      </span>
    </span>
  )
}

function ParticipantBadge({ participant, fallback }: { participant?: Participant; fallback: string }) {
  return (
    <IdentityBadge
      name={participant?.name}
      avatarUrl={participant?.avatarUrl}
      detail={participant?.kind === 'agent' ? undefined : participant?.email}
      fallback={fallback}
    />
  )
}

function AssigneePicker({
  labels: t,
  value,
  participants,
  onChange
}: {
  labels: Labels
  value: string
  participants: Participant[]
  onChange: (value: string) => void
}) {
  const participant = participants.find(item => item.key === value)
  return (
    <div className="assignee-picker">
      <select value={value} onChange={event => onChange(event.target.value)}>
        <option value="">{t.noAssignee}</option>
        {participants.map(item => (
          <option key={item.key} value={item.key}>{item.name}</option>
        ))}
      </select>
      <ParticipantBadge participant={participant} fallback={t.noAssignee} />
    </div>
  )
}

function TaskMeta({ labels: t, task, participants, runs }: { labels: Labels; task: TaskCard; participants: Participant[]; runs: Array<{ taskId: string; status: string; createdAt: string }> }) {
  const participant = participants.find(item => item.key === assigneeValue(task))
  const run = runs
    .filter(item => item.taskId === task.id)
    .sort((a, b) => b.createdAt.localeCompare(a.createdAt))[0]
  return (
    <span className="task-meta">
      <ParticipantBadge participant={participant} fallback={t.noAssignee} />
      <span>{run?.status ?? t.noRuns}</span>
    </span>
  )
}

function AuthorBadge({ comment, agents, people, labels: t }: { comment: TaskComment; agents: AgentProfile[]; people: Person[]; labels: Labels }) {
  return <ActorBadge authorType={comment.authorType} authorId={comment.authorId} agents={agents} people={people} labels={t} />
}

function ActorBadge({ authorType, authorId, agents, people, labels: t }: { authorType: string; authorId: string; agents: AgentProfile[]; people: Person[]; labels: Labels }) {
  const agent = authorType === 'agent'
    ? agents.find(item => item.id === authorId)
    : undefined
  const person = authorType === 'person'
    ? people.find(item => item.id === authorId)
    : undefined
  const authorName = agent?.name ?? person?.displayName ?? t.system
  const avatarUrl = agent?.avatarUrl ?? person?.avatarUrl
  return (
    <span className="author-badge">
      <Avatar name={authorName} url={avatarUrl} />
      <strong>{authorName}</strong>
    </span>
  )
}

function HistoryEntryBody({ entry, labels: t }: { entry: TaskHistoryEntry; labels: Labels }) {
  if (entry.action === 'created') {
    return <p>{t.createdTask}</p>
  }

  return (
    <>
      <p>{t.changed} {historyFieldLabel(entry.field, t)}</p>
      <span className="history-change">
        <span>{historyValue(entry.from, t)}</span>
        <span>-&gt;</span>
        <span>{historyValue(entry.to, t)}</span>
      </span>
    </>
  )
}

function historyFieldLabel(field: string, t: Labels) {
  switch (field) {
    case 'title':
      return t.fieldTitle
    case 'description':
      return t.fieldDescription
    case 'status':
      return t.fieldStatus
    case 'assignee':
      return t.fieldAssignee
    default:
      return field
  }
}

function historyValue(value: string, t: Labels) {
  return value.trim() || t.emptyValue
}

function buildParticipants(state: KanitelState, projectId: string): Participant[] {
  const participants: Participant[] = []

  for (const member of state.members.filter(item => item.projectId === projectId)) {
    const person = state.people.find(item => item.id === member.personId)
    if (!person) continue
    participants.push({
      key: `person:${person.id}`,
      kind: 'person',
      id: person.id,
      linkId: member.id,
      name: person.displayName,
      email: person.email,
      avatarUrl: person.avatarUrl
    })
  }

  for (const link of state.projectAgents.filter(item => item.projectId === projectId)) {
    const agent = state.agents.find(item => item.id === link.agentId)
    if (!agent) continue
    participants.push({
      key: `agent:${agent.id}`,
      kind: 'agent',
      id: agent.id,
      linkId: link.id,
      name: agent.name,
      avatarUrl: agent.avatarUrl
    })
  }

  return participants.sort((a, b) => a.name.localeCompare(b.name))
}

function assigneeValue(task: TaskCard) {
  if (task.assigneeAgentId) return `agent:${task.assigneeAgentId}`
  if (task.assigneePersonId) return `person:${task.assigneePersonId}`
  return ''
}

function splitAssignee(value: string) {
  if (value.startsWith('agent:')) {
    return { assigneeAgentId: value.slice('agent:'.length), assigneePersonId: '' }
  }
  if (value.startsWith('person:')) {
    return { assigneeAgentId: '', assigneePersonId: value.slice('person:'.length) }
  }
  return { assigneeAgentId: '', assigneePersonId: '' }
}

function matchesAssigneeFilter(task: TaskCard, filter: AssigneeFilter) {
  if (filter === 'all') return true
  const assignee = assigneeValue(task)
  if (filter === 'unassigned') return !assignee
  return assignee === filter
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
    commandTemplate: 'npx -y @gitlawb/openclaude@latest --print "$(cat "$KANITEL_TASK_PROMPT_FILE")"',
    systemPrompt: selectedTemplate.systemPrompt,
    enabled: true,
    environment: envToPairs(selectedPreset.environment)
  }
}

function agentPayload(draft: AgentDraft) {
  return {
    name: draft.name,
    templateId: draft.templateId,
    avatarUrl: draft.avatarUrl,
    providerPresetId: draft.providerPresetId,
    model: draft.model,
    baseUrl: draft.baseUrl,
    apiKeyEnvName: draft.apiKeyEnvName,
    containerImage: draft.containerImage,
    commandTemplate: draft.commandTemplate,
    systemPrompt: draft.systemPrompt,
    enabled: draft.enabled,
    environment: envPairsToRecord(draft.environment)
  }
}

async function postProfile(profileDraft: ProfileDraft) {
  return patchJson<AuthResponse>('/api/auth/me', {
    displayName: profileDraft.displayName,
    email: profileDraft.email,
    avatarUrl: profileDraft.avatarUrl,
    currentPassword: profileDraft.currentPassword,
    newPassword: profileDraft.newPassword,
    confirmNewPassword: profileDraft.confirmNewPassword
  })
}

function emptyProfileDraft(): ProfileDraft {
  return {
    displayName: '',
    email: '',
    avatarUrl: '',
    currentPassword: '',
    newPassword: '',
    confirmNewPassword: ''
  }
}

function toProfileDraft(person: Person): ProfileDraft {
  return {
    displayName: person.displayName,
    email: person.email,
    avatarUrl: person.avatarUrl,
    currentPassword: '',
    newPassword: '',
    confirmNewPassword: ''
  }
}

function envToPairs(env: Record<string, string> = {}): EnvPair[] {
  return Object.entries(env).map(([key, value]) => ({
    id: newEnvId(),
    key,
    value
  }))
}

function envPairsToRecord(rows: EnvPair[]): Record<string, string> {
  return Object.fromEntries(
    rows
      .map(row => [row.key.trim(), row.value.trim()] as const)
      .filter(([key]) => key)
  )
}

function newEnvPair(): EnvPair {
  return { id: newEnvId(), key: '', value: '' }
}

function newEnvId() {
  return `env_${Math.random().toString(36).slice(2, 10)}`
}

function formatDate(value: string, locale: Locale) {
  return new Intl.DateTimeFormat(locale === 'ru' ? 'ru-RU' : 'en-US', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit'
  }).format(new Date(value))
}

function readTheme(): Theme {
  const stored = window.localStorage.getItem('kanitel-theme')
  if (stored === 'light' || stored === 'dark') return stored
  return 'light'
}

function readLocale(): Locale {
  const stored = window.localStorage.getItem('kanitel-locale')
  if (stored === 'ru' || stored === 'en') return stored
  return window.navigator.language.toLowerCase().startsWith('ru') ? 'ru' : 'en'
}

function readAssigneeFilter(): AssigneeFilter {
  return window.localStorage.getItem('kanitel-assignee-filter') || 'all'
}

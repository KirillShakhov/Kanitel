# Kanitel

Kanitel is a single-container kanban workspace for assigning repository tasks to AI agents. It ships as a monorepo:

- `apps/api` - ASP.NET Core API, static frontend host, JSON persistence, scheduler, Docker runner.
- `apps/web` - React + Vite board UI.

The UI supports multiple projects, configurable columns, project members, linked HTTP/SSH repositories, global agent profiles, per-project agent roles, task comments, agent avatars/logos, and light/dark themes.

## Run With Docker

```powershell
docker compose up --build
```

Open `http://localhost:8080`.

Agent execution uses the Docker CLI from inside the Kanitel container. The compose file mounts `/var/run/docker.sock`; keep that mount if you want agents to run in fresh work containers.

Useful environment variables:

- `KANITEL_DATA_PATH=/data/kanitel.json`
- `KANITEL_WORKSPACES_PATH=/data/workspaces`
- `KANITEL_AGENT_RUNNER=docker` or `mock`
- `KANITEL_AGENT_POLL_INTERVAL_SECONDS=20`
- `KANITEL_AGENT_TIMEOUT_MINUTES=30`
- `KANITEL_PUBLIC_API_URL=http://host.docker.internal:8080`
- `KANITEL_DOCKER_ADD_HOST_GATEWAY=true`

Provider API keys are not stored by default. Agent profiles refer to environment variable names like `OPENAI_API_KEY`, `ANTHROPIC_API_KEY`, or `CODEX_API_KEY`; pass those variables into the Kanitel container and they will be forwarded to agent containers.

## Agent And Manager API

Kanitel exposes a simple JSON API at `/api/openapi.json`. Agent containers receive:

- `KANITEL_API_URL`
- `KANITEL_PROJECT_ID`
- `KANITEL_TASK_ID`
- `KANITEL_AGENT_ID`

Useful callbacks from inside an agent container:

```bash
curl -s -X POST "$KANITEL_API_URL/api/agent/tasks/$KANITEL_TASK_ID/comments" \
  -H "Content-Type: application/json" \
  -d "{\"agentId\":\"$KANITEL_AGENT_ID\",\"body\":\"Working on this now.\"}"

curl -s -X PATCH "$KANITEL_API_URL/api/agent/tasks/$KANITEL_TASK_ID" \
  -H "Content-Type: application/json" \
  -d "{\"agentId\":\"$KANITEL_AGENT_ID\",\"columnName\":\"Review\",\"assignmentRole\":\"manager\",\"body\":\"Moved to review.\"}"
```

The scheduler starts an agent when a task is assigned to that agent and the latest task comment is not authored by that same agent. Agents can comment, move task status by column name/id, assign another linked agent, set `assignmentRole` to `manager`, or clear assignment with `unassignAgent=true`.

## Local Development

API:

```powershell
dotnet run --project apps/api/Kanitel.Api.csproj
```

Web:

```powershell
cd apps/web
npm install
npm run dev
```

The Vite dev server proxies `/api` to `http://localhost:8080`.

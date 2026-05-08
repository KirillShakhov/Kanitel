# Kanitel

Kanitel is a single-container kanban workspace for assigning repository tasks to AI agents. It ships as a monorepo:

- `apps/api` - ASP.NET Core API, static frontend host, JSON persistence, scheduler, Docker runner.
- `apps/web` - React + Vite board UI.

The UI supports multiple projects, configurable columns, project members, linked HTTP/SSH repositories, global agent profiles, task comments, agent avatars/logos, and light/dark themes.

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

Initial global agents can be declared in `.env` with indexed blocks:

```dotenv
AGENT_0_NAME=Codex
AGENT_0_PROVIDER=codex
AGENT_0_API_URL=https://chatgpt.com/backend-api/codex
AGENT_0_API_KEY=
AGENT_0_MODEL=codexplan

AGENT_1_NAME=Claude
AGENT_1_PROVIDER=anthropic
AGENT_1_API_URL=https://api.anthropic.com
AGENT_1_API_KEY=
AGENT_1_MODEL=claude-sonnet-4-6
```

Use `AGENT_2_*`, `AGENT_3_*`, and so on for more agents. Empty agent blocks are ignored. These env agents are imported when Kanitel creates a new data file; after that, manage agents in the UI.
`AGENT_N_API_KEY` is forwarded from the Kanitel host process to the agent container under the provider's expected key name; use `AGENT_N_API_KEY_ENV` only when you need to override that target variable name.

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
  -d "{\"agentId\":\"$KANITEL_AGENT_ID\",\"columnName\":\"Review\",\"body\":\"Moved to review.\"}"
```

The scheduler starts an agent when a task is assigned to that agent and the latest task comment is not authored by that same agent. Agents can comment, move task status by column name/id, assign another linked agent, or clear assignment with `unassignAgent=true`.

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

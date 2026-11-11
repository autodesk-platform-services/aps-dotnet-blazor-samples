# Assistants

The Assistants module lets project members chat with role-based AI assistants that act on an Autodesk Forma project. They can list and publish Revit models, create folders, manage project users, work with issues, and hand tasks to each other. This page covers setup, management, chat, the tools, and how the module works inside. For the rest of the sample, see the [README](../README.md).

---

## Overview

- An assistant belongs to one project and one project role. A project can have one assistant per role, named "{Role} Assistant" (for example "BIM Manager Assistant").
- Each assistant acts as its own **secure service account (SSA)**. The SSA is created and added to the project when the assistant is created. Forma records the assistant, not the signed-in user, as the author of its changes.
- Project administrators create assistants under **Settings → Assistants** and choose the Forma products and tools each one gets.
- Users chat only with the assistants created for their own project roles, on the **Assistants** page (`/assistants`).
- Assistants run on a local or remote [Ollama](https://ollama.com/) model through [Microsoft.Extensions.AI](https://devblogs.microsoft.com/dotnet/introducing-microsoft-extensions-ai-preview/) and Microsoft.Agents.AI.

---

## Setup

### Ollama and the model

Install [Ollama](https://ollama.com/), keep it running, and pull a model that supports function calling (for example `llama3.1:8b`). Then set these keys in `appsettings.json` or user secrets:

| Key | C# default | Notes |
|---|---|---|
| `Agent:ModelName` | `llama3` | **Must be set.** The shipped `appsettings.json` has `""`, which replaces the C# default. |
| `Agent:OllamaEndpoint` | `http://localhost:11434` | **Must be set.** The shipped `appsettings.json` has `""`, which replaces the C# default, and the app can't create the chat client from an empty URL. |
| `Agent:MaxToolIterations` | `10` | Optional. The most rounds of tool calls the model can make while answering one message. |

```bash
dotnet user-secrets set "Agent:ModelName" "llama3.1:8b"
dotnet user-secrets set "Agent:OllamaEndpoint" "http://localhost:11434"
dotnet user-secrets set "Agent:MaxToolIterations" "10"
```

### APS apps for assistants

Service accounts are owned by APS apps that project administrators add per project under **Settings → Assistants → APS apps for assistants**. No `appsettings.json` entry is needed.

- Each app must be a **server-to-server** APS app. **Add APS app** checks the client ID and secret by requesting a 2-legged token with the `application:service_account:*` and `application:service_account_key:*` scopes. **Update secret** runs the same check before it replaces a rotated secret.
- A client ID can be registered in one project only.
- One APS app can own at most **10 SSAs**, so each app adds room for 10 assistants. New assistants are created in the first app that has a free slot. When every app is full, add another app.
- The grid's **SSAs** column ("N / 10 SSAs") is counted live from APS. It includes every SSA the app owns, also SSAs that this sample didn't create.
- **Remove** opens a dialog that lists the assistants the app owns, then deletes each of them and its SSA. The app is removed only when every assistant was deleted, so a failed removal can be retried.
- When you add an app, assistants and stored keys from before per-project apps whose SSAs belong to that app are linked to it automatically.
- Client secrets are stored in **plaintext** in `Data/ssa-apps.json`. See [Data files and security](#data-files-and-security).

> **Upgrading from the single `SsaApp` configuration:** the `SsaApp` section in `appsettings.json` is no longer read. In each project, add your former `SsaApp` client ID and secret under **Settings → Assistants**. Existing assistants whose service accounts belong to that app are linked to it automatically. Assistants that aren't linked can't chat and should be deleted.

For SSA naming and key handling, see the APS [secure service account best practices](https://aps.autodesk.com/en/docs/ssa/v1/developers_guide/best-practices/#naming-conventions). For the rest of the APS APIs, see the [APS documentation](https://aps.autodesk.com/developer/documentation).

### Extra setup for some tools

- **Hub user search** (`search-hub-users`) uses a 2-legged `account:read` token of the main app (`Forge:ClientId`), not the SSA. The APS app must be provisioned as a **custom integration** in the Forma account.
- **Adding and updating project users** needs the assistant's SSA to have project administrator access. Grant it the **Project Administration** product in the wizard.
- **Issues** through an SSA needs a **Forma Build** subscription on that SSA and access to Issues in the project.
- **Folders** needs the SSA to have Edit or Manage permission on the parent folder.

### Publish tracking

The `publish-revit-model` tool tracks a publish through a Data Management `dm.version.added` webhook. Set `Webhooks:CallbackUrl` to the full public URL of the webhook endpoint, for example through [ngrok](https://ngrok.com/):

```bash
dotnet user-secrets set "Webhooks:CallbackUrl" "https://<ngrok-host>/api/webhooks/version-added"
```

Use an `https` URL. The app redirects plain HTTP requests to HTTPS on every path except `/da` and `/api/revit-webhooks`, so an `http` callback under `/api/webhooks` is redirected. Without `Webhooks:CallbackUrl`, publish tasks stay **Running** (see [Tasks panel and publish tracking](#tasks-panel-and-publish-tracking)).

---

## Managing assistants

### Who can manage assistants

Only users with the Forma **Project Administrator** access level on the selected project can open **Settings** (`/settings`) and **Settings → Assistants** (`/settings/assistants`). This is an access level, not a project role name. The Home page shows the **Settings** card only to these users.

### Create an assistant

Select **Create New Assistant**. The button is disabled until the project has an APS app with a free slot. The wizard has three steps:

1. **Assistant Details**
   - **Project Role**: lists only roles that don't have an assistant yet, so a project has at most one assistant per role.
   - **Assistant Name**: generated as "{Role} Assistant" and can't be changed.
   - **Company (optional)**: the company the SSA joins the project with.
   - **Instructions**: required. Pre-filled with "You are a {Role} assistant scoped to this project..." when you pick a role. This text is the assistant's whole system prompt.
2. **Forma Products**
   - **Data Management** is always shown as granted. The app sends `docs` to APS only when no other product is ticked.
   - Optional products: `designCollaboration`, `modelCoordination`, `autoSpecs`, `build`, `cost`, `insight`, `projectAdministration` and `takeoff`. **Project Administration** gives administrator access to that module; the others give member access.
3. **Tools**
   - Individual tool checkboxes, grouped by module (Revit Models, Folders, Project Users, Issues, Delegation, Revit Automation), with a search box and a **details** link per tool.
   - At least one tool is required.

**Finish** then runs these steps in order:

1. Creates the SSA in the first APS app of the project that has fewer than 10 SSAs. If APS refuses with a 403, the app is treated as full and the next one is tried.
2. Creates a key for the SSA and stores its private key in `Data/ssa-keys.json`.
3. Adds the SSA to the project with the role, company and products, using the **administrator's** 3-legged token.
4. Saves the assistant (an `AgentDefinition`) to `Data/agents.json`.

If a later step fails, earlier steps aren't rolled back. For example, an SSA can exist in the APS app, and count toward its 10 SSAs, without an assistant.

### Edit an assistant

There is no edit. The grid shows only **Assistant Name**, **Role**, **Created At** and **Delete**. To change an assistant's instructions, products or tools, delete it and create it again.

### Delete an assistant

**Delete** runs at once, with no confirmation dialog:

1. Removes the SSA from the project (a user that is already gone counts as removed).
2. Deletes the SSA through its owning APS app and removes its stored key.
3. Deletes the assistant record.

If removing the SSA from the project fails, the delete stops and the SSA is kept, so you can retry. The assistant's conversations and tasks remain. Its conversations become read-only and show "Unavailable assistant".

### Temporary delete-all button

A temporary **DEV: Delete All SSA Accounts** button deletes **every SSA** owned by the project's APS apps, with no confirmation. SSAs that belong to an assistant of this project are removed from the project first and their assistant is deleted. Other SSAs, including ones this sample didn't create, are deleted directly. Use it only on test apps.

---

## Chatting

### Who sees which assistant

The **Assistants** page (`/assistants`) shows the project's assistants whose role matches one of the signed-in user's Forma project roles, ignoring case. If none match, the page shows "No assistants are configured for your role. Contact your project admin."

- With no conversations and one available assistant, a conversation starts at once.
- With several assistants, pick one under **Select an assistant to start a conversation**. That assistant is the conversation's primary assistant.
- **New conversation** starts another one.

### Conversations

- The sidebar lists the user's conversations in the project, with **Search conversations...** (matches the title or the assistant name) and the groups Today, Yesterday and Earlier. A dot marks conversations with an active task.
- A conversation becomes read-only when its assistant was deleted ("The assistant is not available anymore") or the user no longer has its role ("You don't have this role anymore").
- **Clear chat** removes the conversation's messages and each assistant's session. Its tasks are kept.
- Conversations can't be deleted.

### Mentions

Type `@` and an assistant's handle to send one message to another assistant.

- The handle is the assistant name, lowercased, letters and digits only. For example, "BIM Manager Assistant" becomes `@bimmanagerassistant`.
- An `@` preceded by a letter, a digit or another `@` isn't a mention, so email addresses are ignored.
- Only assistants available to the user can be mentioned. If a message has several mentions, the first known one wins.
- A mention routes only that message. The conversation's primary assistant doesn't change.

### Chips

Above the input box:

- Quick-action chips fill in a prompt when the primary assistant has the tool: **Find unpublished models**, **List Revit models**, **Show open issues** and **Find a project user**.
- Mention chips insert the handle of each other available assistant.
- **Show failed tasks** filters the Tasks panel when a task failed.

Press Enter to send and Shift+Enter for a new line.

---

## Tool reference

The model calls each tool by its function name, which is the C# method name without `Async` (for example `ListRevitModels`). Tool IDs are what the wizard stores in `Data/agents.json`.

### Revit Models

| Tool ID (function) | What it does | APS API | Limits and permissions |
|---|---|---|---|
| `list-revit-models` (`ListRevitModels`) | Lists the `.rvt` models in the project's folders, recursively, with name and item ID. | Data Management (top folders, folder contents) | Skips names that contain "template". The SSA needs access to the folders. |
| `publish-revit-model` (`PublishRevitModel`) | Publishes a Revit cloud worksharing model and creates a tracked task. | Data Management `C4RModelPublish` command; Webhooks `dm.version.added` | See [Tasks panel and publish tracking](#tasks-panel-and-publish-tracking). |
| `check-model-publish-eligibility` (`CheckModelPublishEligibility`) | Says whether a model was never published, published, failed its last publish or has a publish in progress. | Data Management `C4RModelGetPublishJob` command | Any error from the command counts as "never published". |
| `list-unpublished-models` (`ListUnpublishedModels`) | Lists the models whose last publish job is not found or failed. | Same as above, per model | Runs one command per model. |
| `get-linked-revit-files` (`GetLinkedRevitFiles`) | Lists the models linked to a host model's latest published version, with name, item ID, version ID and publish status. | Data Management item tip; Forma Revit cloud model (RCM) linked files | Covers only models published after 2025-02-07. Links the SSA can't download are left out. |

### Folders

| Tool ID (function) | What it does | APS API | Limits and permissions |
|---|---|---|---|
| `list-folders` (`ListFolders`) | Lists the subfolders of a folder, or of "Project Files" when no parent is given. | Data Management | |
| `create-folder` (`CreateFolder`) | Creates one folder, under "Project Files" unless a parent is given. Reuses a folder with the same name (ignoring case). | Data Management | Name rules below. |
| `create-folder-structure` (`CreateFolderStructure`) | Creates folders from `/`-separated paths, for example `01 WIP/Architecture`, and reports per path what was created, what existed, or the error. | Data Management | At most 200 paths per call and 25 levels per path. |

- Names are trimmed and can have at most 255 characters. These characters are blocked: `< > : " / \ | ? * ' ¢ ™ $ ®` and control characters.
- If creating a folder returns 409, the tool lists the parent again and reuses the existing folder, so repeating a request creates no duplicates.
- The SSA needs Edit or Manage permission on the parent folder.

### Project Users

| Tool ID (function) | What it does | APS API | Limits and permissions |
|---|---|---|---|
| `search-hub-users` (`SearchHubUsers`) | Searches the account's users by part of a name or email. | Forma Account Admin (account users) | App 2-legged `account:read` token, not the SSA. US region only, up to 50 results. Needs a name or an email. The app must be a custom integration in the Forma account. |
| `find-project-users` (`FindProjectUsers`) | Finds project users by part of a name or email, with company, roles, access levels and products. | Forma Account Admin (project users) | Up to 50 results. |
| `list-project-roles-companies` (`ListProjectRolesAndCompanies`) | Lists the roles and companies that can be assigned in the project. | Forma Account Admin | |
| `add-project-user` (`AddUserToProject`) | Adds a user by email with a role and, optionally, a company and products (Docs as member by default). | Forma Account Admin (assign project user) | Does nothing if the email is already a project user. The SSA needs project administrator access. |
| `update-project-user` (`UpdateProjectUser`) | Changes a project user's role or company, found by exact email. A new role replaces the current roles. | Forma Account Admin | The SSA needs project administrator access. |

There is no tool to remove a project user.

### Issues

| Tool ID (function) | What it does | APS API | Limits and permissions |
|---|---|---|---|
| `search-issues` (`SearchIssues`) | Searches issues by text, status and assignee email. | Forma Issues v1 | 1 to 100 results, 25 by default. |
| `get-issue` (`GetIssue`) | Gets an issue's details and comments. | Forma Issues v1 | |
| `list-issue-types` (`ListIssueTypes`) | Lists the active issue types and subtypes. | Forma Issues v1 | |
| `create-issue` (`CreateIssue`) | Creates an issue with a title and subtype, and optionally a description, assignee, due date and status (`open` by default). | Forma Issues v1 | Assignees must be project users with an Autodesk ID. |
| `update-issue` (`UpdateIssue`) | Changes an issue's title, description, status, assignee or due date. | Forma Issues v1 | |
| `add-issue-comment` (`AddIssueComment`) | Adds a comment to an issue. | Forma Issues v1 | |

- Titles can have at most 100 characters and descriptions at most 1000.
- Statuses: `draft`, `open`, `pending`, `in_progress`, `completed`, `in_review`, `not_approved`, `in_dispute` and `closed`.
- Due dates use `yyyy-MM-dd`, for example `2026-12-31`.
- Every Issues tool needs a Forma Build subscription on the SSA.
- The Forma Issues API has no delete. Assistants close an issue by setting its status to `closed`; deleting it must be done in the Forma UI.

### Delegation

| Tool ID (function) | What it does | APS API | Limits and permissions |
|---|---|---|---|
| `list-project-assistants` (`ListProjectAssistants`) | Lists the other assistants of the project that have an SSA, with ID, name, role and tool modules. | None | |
| `delegate-task` (`DelegateTask`) | Queues a task for another assistant, optionally after earlier tasks, and returns a task ID at once. | None | See the [Delegation](#delegation-1) section. |
| `get-delegated-tasks` (`GetDelegatedTasks`) | Lists the tasks delegated in this conversation with status, dependencies, result or error. | None | |

### Revit Automation (not connected)

The wizard lists three Revit Automation tools: `link-modification`, `create-model` and `create-sheets`. They aren't connected to any code yet, so an assistant with only these tools gets no functions. Use the **Create Revit Models**, **Manage Revit Links** and **Create Sheets** pages instead.

### Identity and tokens

- Tools never take hub or project IDs. They read the hub, project, conversation and assistant from a per-session `AgentToolContext`, so the model can't point a call at another project.
- Tools act as the answering assistant's SSA, with a token for the scopes `data:read data:write data:create account:read account:write`.
- Two exceptions use an app-level 2-legged token of the main app (`Forge:ClientId`): hub user search (`account:read`), and the publish webhook management (`data:read data:write`), which must also work from background callbacks with no user session.
- When an API call returns 403, the tool adds a setup hint to its result. For example: "The agent's service account needs Edit or Manage permission on the parent folder."

---

## Tasks panel and publish tracking

The **Tasks** panel on the right of the **Assistants** page reloads the project's tasks every 5 seconds.

- It starts filtered to the open conversation. **Show all** shows every task in the project, including other users' tasks.
- The tabs are **All**, **Active** (Pending and Running), **Completed** and **Failed**, with counts above them.
- Each row shows the task name and status, the start time, "Delegated by X to Y" for delegated tasks, "Waiting on N task(s)" while dependencies are pending, the completion time and duration, the progress detail, the error, and the result.
- Statuses are `Pending`, `Running`, `Completed`, `Failed` and `Cancelled`. Nothing sets `Cancelled` today.

### Publish tasks

`publish-revit-model` creates a task named "Publish: {model}" and submits the publish command.

- With `Webhooks:CallbackUrl` set, the tool finds or creates a `dm.version.added` hook on the model's parent folder. The hook is shared by tasks that publish from the same folder and is deleted once no task waits on it.
- When `POST /api/webhooks/version-added` receives an event whose source file name matches the model, the task becomes **Completed** and `✅ **Publish: {model}** — the model was published successfully.` is posted in the conversation.
- If no event arrives within 15 minutes, the task becomes **Failed** with "Timed out waiting for the publish to complete."
- If no hook could be set up, for example because `Webhooks:CallbackUrl` is empty, there is no timeout either, so the task stays **Running**.

---

## Delegation

An assistant with the Delegation tools (for example a BIM Manager assistant) can hand tasks to the other assistants of the project.

- **Targets** are all project assistants that have an SSA, other than the delegating one. They aren't filtered by the signed-in user's roles. The model can name a target by ID, name or role.
- **Asynchronous**: `delegate-task` returns a task ID at once. The delegate runs the task in the background, as itself, with its own SSA and its own tools.
- **No further delegation**: a delegate never gets the Delegation tools, and `delegate-task` returns "Delegated assistants can't delegate further." inside a delegated run.
- **Dependencies**: a task can depend on earlier delegated tasks in the same conversation. It waits until they complete and fails if one of them doesn't. Dependencies are checked every 5 seconds for up to 60 minutes.
- **Concurrency and time**: at most 2 delegated tasks run at once, because they share one model. Each run is limited to 30 minutes.
- **Fresh context**: a delegate starts a new session with no conversation history and no results of earlier tasks. It gets only the task title and the instructions written by the delegating assistant.
- **Results** are posted in the originating conversation by the delegate as `✅ **{Task}** — {Result}` or `⚠️ **{Task}** — {Error}`, and are shown in the Tasks panel.
- **Restart**: the queue is in memory. On startup, delegated tasks that were Running are marked Failed with "Interrupted by an app restart.", so their work isn't repeated, and Pending ones are queued again.

---

## How it works

- **Chat client**: one singleton `IChatClient`, an `OllamaApiClient` (OllamaSharp) for `Agent:OllamaEndpoint` and `Agent:ModelName`, wrapped with `UseFunctionInvocation` and `MaximumIterationsPerRequest = Agent:MaxToolIterations`.
- **Agents**: each assistant is built with `AsAIAgent` from Microsoft.Agents.AI 1.13.0, on Microsoft.Extensions.AI 10.8.0. Its system prompt is only the administrator's **Instructions** text. Its tools are the enabled tools that are connected (see [Tool reference](#tool-reference)).
- **Streaming**: replies stream to the page token by token. Assistant messages are rendered as Markdown with Markdig, with HTML disabled. User messages are shown as plain text.
- **Sessions**: a conversation keeps a separate session per assistant. Sessions are serialized to `Data/agent-conversations.json` after each turn and reloaded when needed.
- **Shared transcript**: when an assistant answers, it receives the messages written since its own last reply. Messages by other assistants are passed as `[Name]: text`.
- **Errors**: if a turn fails, the reply shows `[Error: ...]` with the exception message.

---

## Data files and security

All files are in `Data/` under the app's working directory, which is git-ignored. This is a **demo-only file store**: data is lost if the files are deleted, and it isn't suitable for concurrent multi-user production deployments. Production forks should replace `AgentConversationService` with a database-backed implementation.

| File | Content | Secrets |
|---|---|---|
| `agents.json` | Assistant definitions: project, role, name, instructions, tools, products, company, SSA ID, owning app client ID, key ID, SSA email, project user ID | None |
| `agent-conversations.json` | Conversations, messages and each assistant's serialized session | None, but holds the full chat history |
| `agent-tasks.json` | Publish and delegated tasks | None |
| `ssa-apps.json` | The APS apps for assistants per project | **Client secrets in plaintext** |
| `ssa-keys.json` | One record per SSA: owning app, key ID and private key | **SSA private keys in plaintext** |
| `selected-projects.json` | Each user's selected hub and project | None |

Protect `Data/` on any host that runs the sample. Anyone who can read `ssa-apps.json` and `ssa-keys.json` can act as every assistant.

---

## Limitations and troubleshooting

### Limitations

- Assistants can't delete issues (they close them with the status `closed`), delete or rename folders, manage folder permissions, or remove project users.
- The Revit Automation tools (`link-modification`, `create-model`, `create-sheets`) appear in the wizard but aren't connected.
- Assistants can't be edited. Delete and recreate them.
- Hub user search covers the US region only.
- Publish timeouts are scheduled in memory. After a restart, a publish task that was Running completes only if its webhook still arrives.
- Local models vary in how well they call tools. Multi-step and delegated work depends on the model.
- Only Ollama is supported as a model provider.

### Troubleshooting

| Symptom | Cause and fix |
|---|---|
| The app fails when it creates the chat client, or assistants never answer | Set `Agent:ModelName` and `Agent:OllamaEndpoint`, and make sure Ollama is running and reachable at that endpoint. |
| "No assistants are configured for your role. Contact your project admin." | No assistant matches any of your project roles. Ask a project administrator to create one for your role. |
| "Access denied" on **Settings** | You don't have the Project Administrator access level on the selected project. |
| **Create New Assistant** is disabled | Add an APS app, or another app if every app has 10 SSAs. |
| "SSA {id} isn't linked to an APS app. Add the app that owns it in Settings > Assistants." | The assistant was created before per-project apps. Add the app that owns its SSA; if it still isn't linked, delete the assistant. Assistants that aren't linked can't chat. |
| A tool result ends with a setup hint after a 403 | Grant what the hint names: Edit or Manage permission on the folder, project administrator access (Project Administration product), a Forma Build subscription, or the custom integration for hub user search. |
| A publish task stays Running | `Webhooks:CallbackUrl` is empty, not public, or not `https`. See [Publish tracking](#publish-tracking). |
| A delegated task failed with "Interrupted by an app restart." | The app restarted while it ran. Ask the assistant to delegate it again. |

const API_BASE_URL =
  (typeof window !== "undefined" && window.APP_CONFIG && window.APP_CONFIG.apiBaseUrl) ||
  "http://localhost:5253";

const MAX_TITLE_LEN = 500;
const MAX_NOTE_LEN = 4000;

const PRIORITY_LEVEL = { Low: 1, Medium: 2, High: 3, Urgent: 4 };
const PRIORITY_OPTIONS = ["Low", "Medium", "High", "Urgent"];
const STATUS_OPTIONS = ["Todo", "InProgress", "Done"];

const PRIORITY_TO_API = { Low: "low", Medium: "medium", High: "high", Urgent: "urgent" };
const STATUS_TO_API = { Todo: "todo", InProgress: "in-progress", Done: "done" };
const CREATE_GROUP_OPTION_VALUE = "__create_group__";

let allTasks = [];
let allProjects = [];
let focusModeEnabled = false;
let apiOnline = false;
let activeEditTaskId = null;
let modalEditTaskId = null;
let listLoading = false;
const pendingActions = new Set();
let aiSuggestionMode = "llm";
let aiState = { loading: false, error: "", focusSuggestion: null, newTaskSuggestions: [], emptyFocusRationale: null };

function renderAppShell() {
  const app = document.getElementById("app");
  if (!app) throw new Error("Missing #app element.");

  app.innerHTML = `
    <main class="app-layout">
      <aside class="project-sidebar" aria-label="Primary navigation">
        <div class="sidebar-brand">Projects</div>
        <nav class="sidebar-nav">
          <a href="#" class="sidebar-link is-active">Tasks</a>
        </nav>
      </aside>

      <section class="workspace-shell">
        <header class="workspace-topbar">
          <div class="workspace-topbar-title">
            <h1 class="dashboard-title mb-0">Task Workspace</h1>
            <p class="dashboard-subtitle mb-0">Manage planning and execution in one view.</p>
          </div>
          <div class="workspace-tabs" aria-label="Workspace tabs">
            <span class="workspace-tab is-active">Tasks</span>
          </div>
        </header>

        <div class="workspace-content">
          <section id="loadFallback" class="alert alert-danger mb-3" hidden>
            <p id="fallbackText" class="mb-2">Could not load tasks.</p>
            <button id="fallbackRetryBtn" type="button" class="btn btn-danger btn-sm">Retry</button>
          </section>

          <form id="quickAddForm" class="card quick-capture-card mb-3" autocomplete="off" novalidate>
            <div class="card-body">
              <div class="row g-2 align-items-center">
                <div class="col-md-6">
                  <input id="quickTitle" class="form-control" name="title" type="text" maxlength="${MAX_TITLE_LEN}" placeholder="Add task" aria-label="Task title" />
                </div>
                <div class="col-md-4">
                  <select id="quickProject" class="form-select" name="projectId" aria-label="Project"></select>
                </div>
                <div class="col-md-2 d-grid">
                  <button id="addBtn" type="submit" class="btn btn-primary">Add Task</button>
                </div>
              </div>
            </div>
            <div class="card-footer bg-body-tertiary">
              <details class="small">
                <summary class="text-secondary">Details</summary>
                <div class="row g-2 mt-1">
                  <div class="col-md-3">
                    <label class="form-label small text-secondary mb-1" for="quickPriority">Priority</label>
                    <select id="quickPriority" class="form-select form-select-sm" name="priority">
                      <option value="Low">Low</option>
                      <option value="Medium" selected>Medium</option>
                      <option value="High">High</option>
                      <option value="Urgent">Urgent</option>
                    </select>
                  </div>
                  <div class="col-md-3">
                    <label class="form-label small text-secondary mb-1" for="quickStatus">Status</label>
                    <select id="quickStatus" class="form-select form-select-sm" name="status">
                      <option value="Todo" selected>Todo</option>
                      <option value="InProgress">In progress</option>
                      <option value="Done">Done</option>
                    </select>
                  </div>
                  <div class="col-md-3">
                    <label class="form-label small text-secondary mb-1" for="quickDue">Due date</label>
                    <input id="quickDue" class="form-control form-control-sm" name="dueDate" type="date" />
                  </div>
                  <div class="col-12">
                    <label class="form-label small text-secondary mb-1" for="quickNote">Description / notes</label>
                    <textarea id="quickNote" class="form-control form-control-sm" name="description" maxlength="${MAX_NOTE_LEN}" rows="2" placeholder="Optional notes"></textarea>
                  </div>
                </div>
              </details>
            </div>
          </form>

          <div class="dashboard-content row g-3 align-items-start">
            <section id="mainPanel" class="col-lg-8 dashboard-main" aria-labelledby="tasks-label">
              <div class="card shadow-sm main-panel-card">
                <div class="card-body">
                  <h2 id="tasks-label" class="visually-hidden">Tasks</h2>

                  <div class="d-flex flex-wrap align-items-center gap-2 mb-3">
                    <button id="focusModeBtn" type="button" class="btn btn-outline-primary btn-sm focus-mode-toggle" aria-pressed="false">Focus mode</button>
                    <div class="ms-auto d-flex flex-wrap gap-2">
                      <select id="projectFilter" class="form-select form-select-sm" aria-label="Filter by project"></select>
                      <select id="priorityFilter" class="form-select form-select-sm" aria-label="Filter by priority"></select>
                      <select id="statusFilter" class="form-select form-select-sm" aria-label="Filter by status"></select>
                    </div>
                  </div>

                  <div id="message" class="small mb-2 text-secondary" role="status" aria-live="polite" aria-atomic="true"></div>

                  <section class="task-group active-group mb-3">
                    <h3 class="h6 text-uppercase text-secondary mb-2">Active</h3>
                    <div id="activeTaskList" class="vstack gap-2"></div>
                  </section>

                  <section class="task-group completed-group mt-3">
                    <h3 class="h6 text-uppercase text-secondary mb-2">Completed</h3>
                    <div id="completedTaskList" class="vstack gap-2"></div>
                  </section>

                  <p id="taskCount" class="small text-secondary text-end mb-0 mt-3"></p>
                </div>
              </div>
            </section>

            <aside class="col-lg-4 dashboard-sidebar" aria-labelledby="ai-label">
              <div class="card shadow-sm">
                <div class="card-body">
                  <div class="d-flex align-items-center gap-2 mb-2">
                    <div>
                      <h2 id="ai-label" class="h5 ai-panel-title mb-0">Suggested next actions</h2>
                      <p class="small text-secondary mb-0">Helps you decide what to do next - you stay in control.</p>
                    </div>
                    <button id="aiRefreshBtn" type="button" class="btn btn-outline-secondary btn-sm ms-auto js-icon-btn" data-tooltip="Refresh suggestions" aria-label="Refresh suggestions">
                      &#x21bb;
                    </button>
                  </div>
                  <div class="mb-2">
                    <label for="aiSuggestionMode" class="form-label small mb-1">Suggestion style</label>
                    <select id="aiSuggestionMode" class="form-select form-select-sm" aria-label="Suggestion tool selector">
                      <option value="llm" selected>Balanced recommendation</option>
                      <option value="heuristic">Priority and due-date first</option>
                    </select>
                  </div>
                  <div id="aiFocus" class="card border-0 bg-light mb-2"></div>
                  <div id="aiNewTasks" class="card border-0 bg-light mb-2"></div>
                  <div id="aiOverdue" class="card border-0 bg-light mb-2"></div>
                  <div id="aiNeglected" class="card border-0 bg-light"></div>
                </div>
              </div>
            </aside>
          </div>
        </div>
      </section>
    </main>

    <div class="modal fade" id="taskModal" tabindex="-1" aria-labelledby="taskModalTitle" aria-hidden="true">
      <div class="modal-dialog">
        <div class="modal-content">
          <form id="taskModalForm" novalidate>
            <div class="modal-header">
              <h2 class="modal-title fs-5" id="taskModalTitle">Add Task</h2>
              <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
            </div>
            <div class="modal-body vstack gap-2">
              <div>
                <label class="form-label mb-1" for="modalTitle">Title</label>
                <input id="modalTitle" class="form-control" name="title" type="text" maxlength="${MAX_TITLE_LEN}" required />
              </div>
              <div class="row g-2">
                <div class="col-md-6">
                  <label class="form-label mb-1" for="modalProject">Project</label>
                  <select id="modalProject" class="form-select" name="projectId" required></select>
                </div>
                <div class="col-md-6">
                  <label class="form-label mb-1" for="modalPriority">Priority</label>
                  <select id="modalPriority" class="form-select" name="priority">
                    <option value="Low">Low</option>
                    <option value="Medium" selected>Medium</option>
                    <option value="High">High</option>
                    <option value="Urgent">Urgent</option>
                  </select>
                </div>
              </div>
              <div class="row g-2">
                <div class="col-md-6">
                  <label class="form-label mb-1" for="modalStatus">Status</label>
                  <select id="modalStatus" class="form-select" name="status">
                    <option value="Todo" selected>Todo</option>
                    <option value="InProgress">In progress</option>
                    <option value="Done">Done</option>
                  </select>
                </div>
                <div class="col-md-6">
                  <label class="form-label mb-1" for="modalDue">Due date</label>
                  <input id="modalDue" class="form-control" name="dueDate" type="date" />
                </div>
              </div>
              <div>
                <label class="form-label mb-1" for="modalDescription">Description / notes</label>
                <textarea id="modalDescription" class="form-control" name="description" maxlength="${MAX_NOTE_LEN}" rows="3" placeholder="Optional notes"></textarea>
              </div>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary" data-bs-dismiss="modal">Cancel</button>
              <button id="taskModalSubmitBtn" type="submit" class="btn btn-primary">Save task</button>
            </div>
          </form>
        </div>
      </div>
    </div>

    <div class="modal fade" id="groupModal" tabindex="-1" aria-labelledby="groupModalTitle" aria-hidden="true">
      <div class="modal-dialog modal-sm">
        <div class="modal-content">
          <form id="groupModalForm" novalidate>
            <div class="modal-header">
              <h2 class="modal-title fs-6" id="groupModalTitle">Create group</h2>
              <button type="button" class="btn-close" data-bs-dismiss="modal" aria-label="Close"></button>
            </div>
            <div class="modal-body">
              <label class="form-label mb-1" for="groupNameInput">Group name</label>
              <input id="groupNameInput" class="form-control" type="text" maxlength="120" placeholder="e.g., Marketing Q3" required />
              <label class="form-label mb-1 mt-2" for="groupGoalInput">Project goal / purpose</label>
              <textarea id="groupGoalInput" class="form-control" rows="3" maxlength="2000" placeholder="What is this project trying to achieve?" required></textarea>
            </div>
            <div class="modal-footer">
              <button type="button" class="btn btn-outline-secondary btn-sm" data-bs-dismiss="modal">Cancel</button>
              <button id="groupModalSubmitBtn" type="submit" class="btn btn-primary btn-sm">Create</button>
            </div>
          </form>
        </div>
      </div>
    </div>
  `;
}

renderAppShell();

const projectFilter = document.getElementById("projectFilter");
const priorityFilter = document.getElementById("priorityFilter");
const statusFilter = document.getElementById("statusFilter");
const activeTaskList = document.getElementById("activeTaskList");
const completedTaskList = document.getElementById("completedTaskList");
const taskCount = document.getElementById("taskCount");
const message = document.getElementById("message");
const mainPanel = document.getElementById("mainPanel");
const apiDot = document.getElementById("apiDot");
const apiText = document.getElementById("apiText");
const apiRetryBtn = document.getElementById("apiRetryBtn");
const loadFallback = document.getElementById("loadFallback");
const fallbackText = document.getElementById("fallbackText");
const fallbackRetryBtn = document.getElementById("fallbackRetryBtn");
const aiFocus = document.getElementById("aiFocus");
const aiNewTasks = document.getElementById("aiNewTasks");
const aiOverdue = document.getElementById("aiOverdue");
const aiNeglected = document.getElementById("aiNeglected");
const aiRefreshBtn = document.getElementById("aiRefreshBtn");
const aiSuggestionModeSelect = document.getElementById("aiSuggestionMode");

const quickAddForm = document.getElementById("quickAddForm");
const quickTitle = document.getElementById("quickTitle");
const quickProject = document.getElementById("quickProject");
const quickPriority = document.getElementById("quickPriority");
const quickStatus = document.getElementById("quickStatus");
const quickDue = document.getElementById("quickDue");
const quickNote = document.getElementById("quickNote");
const addBtn = document.getElementById("addBtn");
const focusModeBtn = document.getElementById("focusModeBtn");
const taskModalEl = document.getElementById("taskModal");
const taskModalForm = document.getElementById("taskModalForm");
const taskModalTitle = document.getElementById("taskModalTitle");
const taskModalSubmitBtn = document.getElementById("taskModalSubmitBtn");
const modalTitle = document.getElementById("modalTitle");
const modalProject = document.getElementById("modalProject");
const modalPriority = document.getElementById("modalPriority");
const modalStatus = document.getElementById("modalStatus");
const modalDue = document.getElementById("modalDue");
const modalDescription = document.getElementById("modalDescription");
const taskModal = taskModalEl ? new bootstrap.Modal(taskModalEl) : null;
const groupModalEl = document.getElementById("groupModal");
const groupModalForm = document.getElementById("groupModalForm");
const groupNameInput = document.getElementById("groupNameInput");
const groupGoalInput = document.getElementById("groupGoalInput");
const groupModalSubmitBtn = document.getElementById("groupModalSubmitBtn");
const groupModal = groupModalEl ? new bootstrap.Modal(groupModalEl) : null;
let groupCreateTargetSelect = "quick";
let previousQuickProjectValue = "";
let previousModalProjectValue = "";

function escapeHtml(str) {
  if (str == null || str === "") return "";
  const div = document.createElement("div");
  div.textContent = String(str);
  return div.innerHTML;
}

function toUiStatus(raw) {
  const v = String(raw || "").toLowerCase();
  if (v === "inprogress" || v === "in-progress") return "InProgress";
  if (v === "done") return "Done";
  return "Todo";
}

function toUiPriority(raw) {
  const v = String(raw || "").toLowerCase();
  if (v === "urgent") return "Urgent";
  if (v === "high") return "High";
  if (v === "low") return "Low";
  return "Medium";
}

function statusLabel(status) {
  return status === "InProgress" ? "In progress" : status;
}

function priorityClass(priority) {
  if (priority === "Urgent") return "priority-badge priority-urgent";
  if (priority === "High") return "priority-badge priority-high";
  if (priority === "Medium") return "priority-badge priority-medium";
  return "priority-badge priority-low";
}

function statusClass(status) {
  if (status === "Done") return "status-badge status-done";
  if (status === "InProgress") return "status-badge status-in-progress";
  return "status-badge status-todo";
}

function setStatus(text, kind = "neutral") {
  message.textContent = text || "";
  message.classList.remove("text-danger", "text-success", "text-secondary");
  message.setAttribute("role", kind === "error" ? "alert" : "status");
  if (kind === "loading") message.classList.add("text-secondary");
  if (kind === "error") message.classList.add("text-danger");
  if (kind === "success") message.classList.add("text-success");
  if (kind === "neutral") message.classList.add("text-secondary");
}

function setApiStatus(online) {
  apiOnline = online;
  if (!apiDot || !apiText) return;
  apiDot.classList.add("text-white");
  apiDot.classList.toggle("api-badge-online", online);
  apiDot.classList.toggle("api-badge-offline", !online);
  apiDot.textContent = online ? "Online" : "Offline";
  apiText.textContent = online ? "API connected" : "API disconnected";
}

function setListLoading(loading) {
  listLoading = loading;
  mainPanel.classList.toggle("opacity-50", loading);
  mainPanel.classList.toggle("pe-none", loading);
  focusModeBtn.disabled = loading;
}

function setQuickAddBusy(busy) {
  quickAddForm.classList.toggle("opacity-50", busy);
  quickAddForm.classList.toggle("pe-none", busy);
  const disabled = busy || allProjects.length === 0;
  addBtn.disabled = disabled;
  quickTitle.disabled = busy;
  quickProject.disabled = disabled;
  quickPriority.disabled = busy;
  quickStatus.disabled = busy;
  quickDue.disabled = busy;
  quickNote.disabled = busy;
}

function setFallback(visible, text = "") {
  loadFallback.hidden = !visible;
  if (text) fallbackText.textContent = text;
}

async function readApiError(response) {
  try {
    const text = await response.text();
    if (!text) return `Request failed (${response.status}).`;
    try {
      const parsed = JSON.parse(text);
      if (parsed && typeof parsed.error === "string") return parsed.error;
    } catch {
      // no-op
    }
    return `Request failed (${response.status}).`;
  } catch {
    return "Could not read error response.";
  }
}

async function fetchJson(url, options) {
  let response;
  try {
    response = await fetch(url, options);
  } catch (e) {
    if (e instanceof TypeError) {
      setApiStatus(false);
      throw new Error("Network error. API is unreachable.");
    }
    throw e;
  }
  setApiStatus(true);
  if (!response.ok) {
    throw new Error(await readApiError(response));
  }
  return response.json();
}

function ensureArray(value, label) {
  if (!Array.isArray(value)) throw new Error(`Invalid response: expected ${label} array.`);
  return value;
}

function normalizeTask(task) {
  return {
    ...task,
    id: Number(task.id ?? task.Id),
    projectId: Number(task.projectId ?? task.ProjectId),
    title: task.title ?? task.Title ?? "",
    description: task.description ?? task.Description ?? "",
    projectName: task.projectName ?? task.ProjectName ?? "Project",
    priority: toUiPriority(task.priority ?? task.Priority),
    status: toUiStatus(task.status ?? task.Status),
    dueDate: task.dueDate ?? task.DueDate ?? null,
    createdAt: task.createdAt ?? task.CreatedAt ?? null,
    updatedAt: task.updatedAt ?? task.UpdatedAt ?? null
  };
}

function formatDueDate(value) {
  if (!value) return "No due date";
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return "No due date";
  return d.toLocaleDateString(undefined, { month: "short", day: "numeric", year: "numeric" });
}

function toDateInputValue(value) {
  if (!value) return "";
  const d = new Date(value);
  if (Number.isNaN(d.getTime())) return "";
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

function getProjectName(projectId) {
  const project = allProjects.find((p) => Number(p.id ?? p.Id) === Number(projectId));
  return project ? (project.name ?? project.Name) : "Project";
}

function toProjectContext(project) {
  return {
    name: (project.name ?? project.Name ?? "").trim(),
    description: (project.description ?? project.Description ?? "").trim() || null,
    category: (project.category ?? project.Category ?? "").trim() || null,
    goalPurpose: (project.goalPurpose ?? project.GoalPurpose ?? "").trim() || null
  };
}

function buildAiProjectContext() {
  return allProjects
    .map(toProjectContext)
    .filter((p) => p.name);
}

function inferProjectGoal(projectContexts) {
  const explicitGoal = projectContexts.find((p) => p.goalPurpose);
  if (explicitGoal) return explicitGoal.goalPurpose;
  const described = projectContexts.find((p) => p.description);
  if (described) return described.description;
  const categories = projectContexts.map((p) => p.category).filter(Boolean);
  if (categories.length === 0) return null;
  const unique = [...new Set(categories)];
  return `Main project categories: ${unique.join(", ")}.`;
}

const MAX_AI_TASK_DESC_CHARS = 800;

/** Project rows relevant to the current dashboard filter (or all). */
function buildAiProjectContextForNext() {
  const contexts = buildAiProjectContext();
  const filterId = projectFilter.value;
  if (!filterId) return contexts;
  const p = allProjects.find((x) => String(x.id ?? x.Id) === filterId);
  if (!p) return contexts;
  const one = toProjectContext(p);
  return one.name ? [one] : contexts;
}

/** Prefer the filtered project's goal when a single project is selected. */
function inferProjectGoalForNext(projectContexts) {
  const filterId = projectFilter.value;
  if (filterId) {
    const p = allProjects.find((x) => String(x.id ?? x.Id) === filterId);
    if (p) {
      const ctx = toProjectContext(p);
      return ctx.goalPurpose || ctx.description || inferProjectGoal(projectContexts);
    }
  }
  return inferProjectGoal(projectContexts);
}

function hasMeaningfulProjectContextForAi(projectContexts) {
  const g = inferProjectGoalForNext(projectContexts);
  if (g && String(g).trim()) return true;
  return projectContexts.some((p) => p.goalPurpose || p.description || p.category);
}

/** Default project when adding from AI suggestions (filter wins, else single scoped project). */
function defaultProjectIdForAiAdds() {
  const fid = projectFilter.value;
  if (fid) return Number(fid);
  const ctx = buildAiProjectContextForNext();
  if (ctx.length === 1) {
    const name = ctx[0].name;
    const p = allProjects.find((x) => String(x.name ?? x.Name).trim() === name);
    if (p) return Number(p.id ?? p.Id);
  }
  const q = Number(quickProject.value);
  if (!Number.isNaN(q) && q > 0) return q;
  const first = allProjects[0];
  return first ? Number(first.id ?? first.Id) : 0;
}

function findProjectByName(projectName) {
  const key = String(projectName || "").trim().toLowerCase();
  if (!key) return null;
  return allProjects.find((p) => String(p.name ?? p.Name).trim().toLowerCase() === key) || null;
}

function parseSuggestedNewTasksFromResponse(aiResponse, existingTitleKeys) {
  const raw = Array.isArray(aiResponse.suggestedNewTasks) ? aiResponse.suggestedNewTasks : [];
  const out = [];
  for (const item of raw) {
    const title = typeof item === "string" ? item.trim() : String(item?.title ?? "").trim();
    const why = item && typeof item === "object" ? String(item.why ?? "").trim() : "";
    const projectName = item && typeof item === "object" ? String(item.projectName ?? "").trim() : "";
    if (!title) continue;
    if (existingTitleKeys.has(titleKey(title))) continue;
    if (out.some((x) => titleKey(x.title) === titleKey(title))) continue;
    out.push({ title, why, projectName });
    if (out.length >= 5) break;
  }
  return out;
}

function fillProjectDropdowns(projects) {
  const projectOpts = projects
    .map((p) => {
      const id = p.id ?? p.Id;
      const name = p.name ?? p.Name;
      return `<option value="${id}">${escapeHtml(name)}</option>`;
    })
    .join("");
  const createOpt = `<option value="${CREATE_GROUP_OPTION_VALUE}">+ Create new group...</option>`;
  const opts = projectOpts + createOpt;
  quickProject.innerHTML = opts;
  modalProject.innerHTML = opts;
  if (projects.length > 0 && !quickProject.value) {
    quickProject.value = String(projects[0].id ?? projects[0].Id);
  }
  if (projects.length > 0 && !modalProject.value) {
    modalProject.value = String(projects[0].id ?? projects[0].Id);
  }

  projectFilter.innerHTML = '<option value="">All projects</option>' + projectOpts;
}

function openCreateGroupModal(target) {
  if (!groupModal) return;
  groupCreateTargetSelect = target;
  groupNameInput.value = "";
  groupGoalInput.value = "";
  groupModal.show();
  groupNameInput.focus();
}

async function createGroupFromModal() {
  const name = groupNameInput.value.trim();
  const goalPurpose = groupGoalInput.value.trim();
  if (!name) return setStatus("Group name is required.", "error");
  if (!goalPurpose) return setStatus("Project goal/purpose is required.", "error");
  if (allProjects.some((p) => String(p.name ?? p.Name).trim().toLowerCase() === name.toLowerCase())) {
    return setStatus("That group already exists.", "error");
  }

  groupModalSubmitBtn.disabled = true;
  setStatus("Adding group...", "loading");
  try {
    const created = await fetchJson(`${API_BASE_URL}/api/projects`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        name,
        description: null,
        category: "Custom Group",
        goalPurpose
      })
    });

    allProjects.push(created);
    allProjects.sort((a, b) => String(a.name ?? a.Name).localeCompare(String(b.name ?? b.Name)));
    fillProjectDropdowns(allProjects);
    const createdId = String(created.id ?? created.Id);
    if (groupCreateTargetSelect === "modal") {
      modalProject.value = createdId;
      quickProject.value = previousQuickProjectValue || quickProject.value;
    } else {
      quickProject.value = createdId;
      modalProject.value = previousModalProjectValue || modalProject.value;
    }
    setStatus("Group added.", "success");
    groupModal.hide();
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Could not add group.";
    setStatus(msg, "error");
  } finally {
    groupModalSubmitBtn.disabled = false;
  }
}

function titleKey(value) {
  return String(value || "").trim().toLowerCase();
}

function findTaskByTitle(title) {
  const key = titleKey(title);
  if (!key) return null;
  return allTasks.find((task) => titleKey(task.title) === key) || null;
}

function openTaskModal(mode, taskData) {
  if (!taskModal) return;
  const isEdit = mode === "edit";
  modalEditTaskId = isEdit ? Number(taskData.id) : null;
  taskModalForm.setAttribute("data-mode", isEdit ? "edit" : "add");
  taskModalTitle.textContent = isEdit ? "Edit Task" : "Add Task";
  taskModalSubmitBtn.textContent = isEdit ? "Save changes" : "Add task";

  modalTitle.value = taskData.title || "";
  modalProject.value = String(taskData.projectId || allProjects[0]?.id || allProjects[0]?.Id || "");
  modalPriority.value = taskData.priority || "Medium";
  modalStatus.value = taskData.status || "Todo";
  modalDue.value = toDateInputValue(taskData.dueDate);
  modalDescription.value = taskData.description || "";
  taskModal.show();
  modalTitle.focus();
}

function fillFilterDropdowns() {
  priorityFilter.innerHTML = `
    <option value="">All priorities</option>
    <option value="Low">Low</option>
    <option value="Medium">Medium</option>
    <option value="High">High</option>
    <option value="Urgent">Urgent</option>
  `;
  statusFilter.innerHTML = `
    <option value="">All statuses</option>
    <option value="Todo">Todo</option>
    <option value="InProgress">In progress</option>
    <option value="Done">Done</option>
  `;
}

function isOverdue(task) {
  if (!task.dueDate || task.status === "Done") return false;
  const due = new Date(task.dueDate);
  if (Number.isNaN(due.getTime())) return false;
  return due.getTime() < Date.now();
}

function isDueToday(task) {
  if (!task.dueDate || task.status === "Done") return false;
  const due = new Date(task.dueDate);
  if (Number.isNaN(due.getTime())) return false;
  const now = new Date();
  return (
    due.getFullYear() === now.getFullYear() &&
    due.getMonth() === now.getMonth() &&
    due.getDate() === now.getDate()
  );
}

function isNeglected(task) {
  if (task.status === "Done") return false;
  const touch = new Date(task.updatedAt || task.createdAt || 0);
  if (Number.isNaN(touch.getTime())) return false;
  return Date.now() - touch.getTime() >= 3 * 24 * 60 * 60 * 1000;
}

function compareFocus(a, b) {
  const pDiff = PRIORITY_LEVEL[b.priority] - PRIORITY_LEVEL[a.priority];
  if (pDiff !== 0) return pDiff;

  const aDue = a.dueDate ? new Date(a.dueDate).getTime() : Number.POSITIVE_INFINITY;
  const bDue = b.dueDate ? new Date(b.dueDate).getTime() : Number.POSITIVE_INFINITY;
  if (aDue !== bDue) return aDue - bDue;

  return new Date(a.createdAt || 0).getTime() - new Date(b.createdAt || 0).getTime();
}

function computeSuggestions(tasks) {
  const active = tasks.filter((t) => t.status !== "Done");
  const overdue = active.filter(isOverdue).sort(compareFocus);
  const neglected = active.filter(isNeglected).sort(compareFocus);
  const focusNext = [...active].sort(compareFocus)[0] || null;
  return { focusNext, overdue, neglected };
}

function buildFallbackRationale(task) {
  if (!task) return "No active tasks available to prioritize.";
  const urgency = isOverdue(task)
    ? "It is overdue."
    : isDueToday(task)
      ? "It is due today."
      : task.dueDate
        ? "It has a nearby due date."
        : "It has no due date, so priority drives ordering.";
  const projectContexts = buildAiProjectContextForNext();
  const hasContext = projectContexts.some((p) => p.goalPurpose || p.description || p.category);
  const contextHint = hasContext
    ? ""
    : " Add project goal/purpose context when creating projects to get more tailored suggestions.";
  return `${urgency} Priority is ${task.priority}, so this is the highest-impact next task.${contextHint}`;
}

async function refreshAiSuggestions() {
  const activeTasks = getVisibleTasks().filter((t) => t.status !== "Done");
  aiState = { ...aiState, loading: true, error: "" };
  renderAiPanel();

  if (aiSuggestionMode !== "llm") {
    const fallback = [...activeTasks].sort(compareFocus)[0] || null;
    aiState = {
      loading: false,
      error: "",
      focusSuggestion: fallback
        ? { ...fallback, rationale: buildFallbackRationale(fallback) }
        : null,
      newTaskSuggestions: [],
      emptyFocusRationale: null
    };
    renderAiPanel();
    return;
  }

  const projectContexts = buildAiProjectContextForNext();
  const projectGoal = inferProjectGoalForNext(projectContexts);
  const hasCtx = hasMeaningfulProjectContextForAi(projectContexts);

  if (activeTasks.length === 0 && !hasCtx) {
    aiState = {
      loading: false,
      error: "",
      focusSuggestion: null,
      newTaskSuggestions: [],
      emptyFocusRationale: null
    };
    renderAiPanel();
    return;
  }

  const existingTitleKeys = new Set(allTasks.map((t) => titleKey(t.title)));

  try {
    const taskPayload =
      activeTasks.length > 0
        ? activeTasks.map((t) => {
            const desc = String(t.description || "").trim();
            return {
              id: t.id,
              title: t.title,
              priority: t.priority,
              status: t.status,
              dueDate: t.dueDate,
              projectId: t.projectId,
              projectName: t.projectName || getProjectName(t.projectId),
              description: desc ? desc.slice(0, MAX_AI_TASK_DESC_CHARS) : null
            };
          })
        : [];

    const aiResponse = await fetchJson(`${API_BASE_URL}/api/ai/next`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        tasks: taskPayload,
        projectGoal,
        projects: projectContexts
      })
    });
    const recommendedTitle = String(aiResponse.recommendedTitle || "").trim();
    const rationale = String(aiResponse.rationale || aiResponse.reason || "").trim();
    const recommendedTaskId = Number(aiResponse.recommendedTaskId);
    let selected = allTasks.find((t) => t.id === recommendedTaskId) || null;
    if (!selected && recommendedTitle) selected = findTaskByTitle(recommendedTitle);
    const newTaskSuggestions = parseSuggestedNewTasksFromResponse(aiResponse, existingTitleKeys);
    aiState = {
      loading: false,
      error: "",
      focusSuggestion: selected ? { ...selected, rationale: rationale || buildFallbackRationale(selected) } : null,
      newTaskSuggestions,
      emptyFocusRationale: !selected && rationale ? rationale : null
    };
  } catch (e) {
    const fallback = [...activeTasks].sort(compareFocus)[0] || null;
    aiState = {
      loading: false,
      error: "",
      focusSuggestion: fallback
        ? { ...fallback, rationale: buildFallbackRationale(fallback) }
        : null,
      newTaskSuggestions: [],
      emptyFocusRationale: null
    };
  }

  renderAiPanel();
}

function renderAiPanel() {
  const visibleTasks = getVisibleTasks();
  const visibleTaskIds = new Set(visibleTasks.map((t) => t.id));
  const { focusNext, overdue, neglected } = computeSuggestions(visibleTasks);
  const aiFocusSuggestion =
    aiState.focusSuggestion && visibleTaskIds.has(aiState.focusSuggestion.id)
      ? aiState.focusSuggestion
      : focusNext;
  const rationale = aiFocusSuggestion?.rationale || buildFallbackRationale(aiFocusSuggestion);
  const loadingHint = aiState.loading ? "<p class='small text-secondary mb-2'>Refreshing suggestions...</p>" : "";
  const errorHint = aiState.error ? `<p class="small text-danger mb-2">${escapeHtml(aiState.error)}</p>` : "";

  const emptyNote = aiState.emptyFocusRationale ? escapeHtml(aiState.emptyFocusRationale) : "";
  if (aiFocusSuggestion) {
    aiFocus.innerHTML = `<div class="card-body p-3"><h3 class="h6">Start here</h3>${loadingHint}${errorHint}<button type="button" class="ai-suggestion-item w-100 text-start border-0 bg-transparent p-2 mt-2 js-ai-suggestion" data-title="${escapeHtml(
      aiFocusSuggestion.title
    )}" data-project-id="${aiFocusSuggestion.projectId}" data-priority="${escapeHtml(aiFocusSuggestion.priority)}" data-due-date="${escapeHtml(
      toDateInputValue(aiFocusSuggestion.dueDate)
    )}"><p class="mb-1 task-title">${escapeHtml(aiFocusSuggestion.title)}</p><p class="small task-meta mb-1">${escapeHtml(
      aiFocusSuggestion.projectName
    )} - ${escapeHtml(aiFocusSuggestion.priority)} - ${escapeHtml(formatDueDate(aiFocusSuggestion.dueDate))}</p><p class="small mb-0"><strong>Why this is suggested:</strong> ${escapeHtml(
      rationale
    )}</p></button></div>`;
  } else if (aiState.loading || emptyNote) {
    aiFocus.innerHTML = `<div class="card-body p-3"><h3 class="h6">Start here</h3>${loadingHint}${errorHint}${
      emptyNote ? `<p class="small mb-0">${emptyNote}</p>` : "<p class='small text-secondary mb-0'>Refreshing…</p>"
    }</div>`;
  } else {
    aiFocus.innerHTML =
      "<div class='card-body p-3'><h3 class='h6'>Start here</h3><p class='small text-secondary mb-0'>No active tasks right now.</p></div>";
  }

  const addPid = defaultProjectIdForAiAdds();
  const newList = Array.isArray(aiState.newTaskSuggestions) ? aiState.newTaskSuggestions : [];
  if (aiNewTasks) {
    aiNewTasks.innerHTML =
      newList.length > 0
        ? `<div class="card-body p-3"><h3 class="h6">Ideas to add</h3><p class="small text-secondary mb-2">Not on your list yet — click to open the add form.</p><ul class="mb-0 list-unstyled">${newList
            .map((s) => {
              const mappedProject = findProjectByName(s.projectName);
              const suggestionProjectId = mappedProject ? Number(mappedProject.id ?? mappedProject.Id) : addPid;
              const suggestionProjectName = mappedProject
                ? String(mappedProject.name ?? mappedProject.Name)
                : (s.projectName || "");
              const descParam = encodeURIComponent(s.why || "");
              return `<li><button type="button" class="ai-suggestion-item w-100 text-start border-0 bg-transparent p-2 mt-1 js-ai-suggestion" data-title="${escapeHtml(
                s.title
              )}" data-project-id="${suggestionProjectId}" data-priority="Medium" data-due-date="" data-desc="${descParam}"><p class="mb-0 task-title">${escapeHtml(
                s.title
              )}</p>${
                suggestionProjectName
                  ? `<p class="small text-secondary mb-0 mt-1"><strong>Group:</strong> ${escapeHtml(suggestionProjectName)}</p>`
                  : ""
              }${
                s.why
                  ? `<p class="small text-secondary mb-0 mt-1">${escapeHtml(s.why)}</p>`
                  : ""
              }</button></li>`;
            })
            .join("")}</ul></div>`
        : `<div class="card-body p-3"><h3 class="h6">Ideas to add</h3><p class="small text-secondary mb-0">${
            aiSuggestionMode === "llm" ? "No extra ideas this refresh." : "Switch to balanced recommendation for AI ideas."
          }</p></div>`;
  }

  aiOverdue.innerHTML = overdue.length
    ? `<div class="card-body p-3"><h3 class="h6">Due now</h3><ul class="mb-0 list-unstyled">${overdue
        .slice(0, 4)
        .map(
          (t) =>
            `<li><button type="button" class="ai-suggestion-item w-100 text-start border-0 bg-transparent p-2 mt-1 js-ai-suggestion" data-title="${escapeHtml(
              t.title
            )}" data-project-id="${t.projectId}" data-priority="${escapeHtml(t.priority)}" data-due-date="${escapeHtml(
              toDateInputValue(t.dueDate)
            )}">${escapeHtml(t.title)} (${escapeHtml(formatDueDate(t.dueDate))})</button></li>`
        )
        .join("")}</ul></div>`
    : "<div class='card-body p-3'><h3 class='h6'>Due now</h3><p class='small text-secondary mb-0'>Nothing overdue.</p></div>";

  aiNeglected.innerHTML = neglected.length
    ? `<div class="card-body p-3"><h3 class="h6">Needs a check-in</h3><ul class="mb-0 list-unstyled">${neglected
        .slice(0, 4)
        .map(
          (t) =>
            `<li><button type="button" class="ai-suggestion-item w-100 text-start border-0 bg-transparent p-2 mt-1 js-ai-suggestion" data-title="${escapeHtml(
              t.title
            )}" data-project-id="${t.projectId}" data-priority="${escapeHtml(t.priority)}" data-due-date="${escapeHtml(
              toDateInputValue(t.dueDate)
            )}">${escapeHtml(t.title)} (inactive 3+ days)</button></li>`
        )
        .join("")}</ul></div>`
    : "<div class='card-body p-3'><h3 class='h6'>Needs a check-in</h3><p class='small text-secondary mb-0'>Everything has been touched recently.</p></div>";
}

function getVisibleTasks() {
  const projectId = projectFilter.value;
  const priority = priorityFilter.value;
  const status = statusFilter.value;

  return allTasks.filter((task) => {
    const okProject = !projectId || String(task.projectId) === projectId;
    const okPriority = !priority || task.priority === priority;
    const okStatus = !status || task.status === status;
    const okFocus =
      !focusModeEnabled ||
      ((task.priority === "High" || task.priority === "Urgent") && (isOverdue(task) || isDueToday(task)));

    return okProject && okPriority && okStatus && okFocus;
  });
}

function renderTaskCard(task) {
  if (activeEditTaskId === task.id) {
    return `
      <article class="card task-card ${task.status !== "Done" ? "active-task-card" : ""} border-primary-subtle">
        <div class="card-body">
        <form class="edit-form vstack gap-2" data-task-id="${task.id}">
          <input class="form-control" type="text" name="title" maxlength="${MAX_TITLE_LEN}" value="${escapeHtml(task.title)}" required />
          <div class="row g-2">
            <div class="col-md-3"><select class="form-select form-select-sm" name="projectId">
              ${allProjects
                .map((p) => {
                  const id = Number(p.id ?? p.Id);
                  const name = p.name ?? p.Name;
                  return `<option value="${id}" ${id === task.projectId ? "selected" : ""}>${escapeHtml(name)}</option>`;
                })
                .join("")}
            </select></div>
            <div class="col-md-3"><select class="form-select form-select-sm" name="priority">
              ${PRIORITY_OPTIONS.map((p) => `<option value="${p}" ${p === task.priority ? "selected" : ""}>${p}</option>`).join("")}
            </select></div>
            <div class="col-md-3"><select class="form-select form-select-sm" name="status">
              ${STATUS_OPTIONS.map((s) => `<option value="${s}" ${s === task.status ? "selected" : ""}>${escapeHtml(statusLabel(s))}</option>`).join("")}
            </select></div>
            <div class="col-md-3"><input class="form-control form-control-sm" type="date" name="dueDate" value="${toDateInputValue(task.dueDate)}" /></div>
          </div>
          <textarea class="form-control form-control-sm" name="description" maxlength="${MAX_NOTE_LEN}" rows="2" placeholder="Optional notes">${escapeHtml(
            task.description || ""
          )}</textarea>
          <div class="d-flex gap-2 align-items-center">
            <button type="submit" class="btn btn-primary btn-sm">Save</button>
            <button type="button" class="btn btn-outline-secondary btn-sm js-cancel-edit" data-task-id="${task.id}">Cancel</button>
          </div>
        </form>
        </div>
      </article>
    `;
  }

  const busy = pendingActions.has(task.id);
  const done = task.status === "Done";
  return `
    <article class="card task-card ${!done ? "active-task-card" : ""} ${busy ? "opacity-50 pe-none" : ""}">
      <div class="card-body">
      <div class="d-flex justify-content-between gap-2">
        <p class="task-title mb-0">${escapeHtml(task.title)}</p>
        <div class="d-flex gap-2">
          <button type="button" class="btn btn-outline-secondary btn-sm js-edit js-icon-btn" data-task-id="${task.id}" data-tooltip="Edit task" aria-label="Edit task" ${busy ? "disabled" : ""}>&#9998;</button>
          <button type="button" class="btn btn-outline-danger btn-sm js-delete js-icon-btn" data-task-id="${task.id}" data-tooltip="Delete task" aria-label="Delete task" ${busy ? "disabled" : ""}>&#128465;</button>
        </div>
      </div>
      <div class="d-flex flex-wrap gap-2 mt-2">
        <span class="badge project-badge task-meta">${escapeHtml(task.projectName || "Project")}</span>
        <span class="badge ${priorityClass(task.priority)}">${escapeHtml(task.priority)}</span>
        <span class="badge ${statusClass(task.status)}">${escapeHtml(statusLabel(task.status))}</span>
        <span class="badge due-badge task-meta">${escapeHtml(formatDueDate(task.dueDate))}</span>
      </div>
      ${task.description ? `<p class="small task-meta mb-0 mt-2">${escapeHtml(task.description)}</p>` : ""}
      <div class="d-flex gap-2 align-items-center mt-2">
        ${
          done
            ? `<span class="small completed-label">Completed</span>`
            : `<button type="button" class="btn btn-primary btn-sm js-complete js-icon-btn" data-task-id="${task.id}" data-tooltip="Mark complete" aria-label="Mark complete" ${busy ? "disabled" : ""}>&#10003;</button>`
        }
      </div>
      </div>
    </article>
  `;
}

function renderTasks() {
  const visible = getVisibleTasks();
  const active = visible.filter((t) => t.status !== "Done");
  const completed = visible.filter((t) => t.status === "Done");

  activeTaskList.innerHTML = active.length
    ? active.map(renderTaskCard).join("")
    : '<p class="text-secondary small mb-0 py-2">No active tasks match your filters.</p>';

  completedTaskList.innerHTML = completed.length
    ? completed.map(renderTaskCard).join("")
    : '<p class="text-secondary small mb-0 py-2">No completed tasks yet.</p>';

  taskCount.textContent = `${visible.length} visible - ${active.length} active - ${completed.length} completed`;
  renderAiPanel();
}

async function loadDashboard(showLoading = true) {
  if (showLoading) {
    setListLoading(true);
    setStatus("Loading tasks...", "loading");
  }

  try {
    const [projectsRes, tasksRes] = await Promise.all([
      fetchJson(`${API_BASE_URL}/api/projects`),
      fetchJson(`${API_BASE_URL}/api/tasks/all`)
    ]);

    allProjects = ensureArray(projectsRes, "projects");
    allTasks = ensureArray(tasksRes, "tasks").map(normalizeTask);
    allTasks.forEach((task) => {
      if (!task.projectName) task.projectName = getProjectName(task.projectId);
    });

    fillProjectDropdowns(allProjects);
    fillFilterDropdowns();
    setFallback(false);
    setStatus("", "neutral");
    renderTasks();
    await refreshAiSuggestions();
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Failed to load dashboard.";
    setStatus(msg, "error");
    setFallback(true, `${msg} Use retry once API is available.`);
    allProjects = [];
    allTasks = [];
    fillProjectDropdowns([]);
    renderTasks();
    aiState = { loading: false, error: "", focusSuggestion: null, newTaskSuggestions: [], emptyFocusRationale: null };
  } finally {
    setListLoading(false);
    setQuickAddBusy(false);
  }
}

async function mutateTask(taskId, body, successMessage) {
  pendingActions.add(taskId);
  renderTasks();
  setStatus("Saving...", "loading");
  try {
    const updated = await fetchJson(`${API_BASE_URL}/api/tasks/${taskId}`, {
      method: "PUT",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body)
    });
    const task = normalizeTask(updated);
    task.projectName = task.projectName || getProjectName(task.projectId);

    const index = allTasks.findIndex((t) => t.id === taskId);
    if (index >= 0) allTasks[index] = task;
    else allTasks.push(task);

    setStatus(successMessage, "success");
    renderTasks();
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Update failed.";
    setStatus(msg, "error");
  } finally {
    pendingActions.delete(taskId);
    window.setTimeout(() => {
      if (message.textContent === successMessage) setStatus("", "neutral");
    }, 1800);
  }
}

quickAddForm.addEventListener("submit", async (event) => {
  event.preventDefault();

  const title = quickTitle.value.trim();
  const description = quickNote.value.trim();
  const projectId = Number(quickProject.value);

  if (!title) return setStatus("Title is required.", "error");
  if (title.length > MAX_TITLE_LEN) return setStatus(`Title max is ${MAX_TITLE_LEN}.`, "error");
  if (description.length > MAX_NOTE_LEN) return setStatus(`Description max is ${MAX_NOTE_LEN}.`, "error");
  if (!projectId || Number.isNaN(projectId)) return setStatus("Choose a valid project.", "error");

  const payload = {
    title,
    description: description || null,
    dueDate: quickDue.value ? new Date(`${quickDue.value}T00:00:00`).toISOString() : null,
    priority: PRIORITY_TO_API[quickPriority.value] || "medium",
    status: STATUS_TO_API[quickStatus.value] || "todo",
    projectId
  };

  setQuickAddBusy(true);
  setStatus("Adding task...", "loading");

  try {
    const created = await fetchJson(`${API_BASE_URL}/api/tasks`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(payload)
    });
    const task = normalizeTask(created);
    task.projectName = task.projectName || getProjectName(task.projectId);
    allTasks.unshift(task);
    quickTitle.value = "";
    quickNote.value = "";
    quickDue.value = "";
    quickPriority.value = "Medium";
    quickStatus.value = "Todo";
    setStatus("Task added.", "success");
    renderTasks();
    quickTitle.focus();
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Could not create task.";
    setStatus(msg, "error");
  } finally {
    setQuickAddBusy(false);
  }
});

function buildTaskPayloadFromValues(values) {
  return {
    title: values.title,
    description: values.description || null,
    dueDate: values.dueDate ? new Date(`${values.dueDate}T00:00:00`).toISOString() : null,
    priority: PRIORITY_TO_API[values.priority] || "medium",
    status: STATUS_TO_API[values.status] || "todo",
    projectId: values.projectId
  };
}

async function createTaskFromModal(values) {
  setStatus("Adding task...", "loading");
  const created = await fetchJson(`${API_BASE_URL}/api/tasks`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(buildTaskPayloadFromValues(values))
  });
  const task = normalizeTask(created);
  task.projectName = task.projectName || getProjectName(task.projectId);
  allTasks.unshift(task);
  renderTasks();
  setStatus("Task added.", "success");
}

async function updateTaskFromModal(taskId, values) {
  await mutateTask(taskId, buildTaskPayloadFromValues(values), "Task updated.");
}

taskModalForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  const mode = taskModalForm.getAttribute("data-mode") || "add";
  const values = {
    title: modalTitle.value.trim(),
    description: modalDescription.value.trim(),
    projectId: Number(modalProject.value),
    priority: modalPriority.value,
    status: modalStatus.value,
    dueDate: modalDue.value
  };

  if (!values.title) return setStatus("Title is required.", "error");
  if (!values.projectId || Number.isNaN(values.projectId)) return setStatus("Project is required.", "error");

  taskModalSubmitBtn.disabled = true;
  try {
    if (mode === "edit" && modalEditTaskId) {
      await updateTaskFromModal(modalEditTaskId, values);
    } else {
      await createTaskFromModal(values);
    }
    taskModal.hide();
  } catch (e) {
    const msg = e instanceof Error ? e.message : "Could not save task.";
    setStatus(msg, "error");
  } finally {
    taskModalSubmitBtn.disabled = false;
  }
});

taskModalEl.addEventListener("hidden.bs.modal", () => {
  modalEditTaskId = null;
  taskModalForm.reset();
  if (allProjects.length > 0) {
    modalProject.value = String(allProjects[0].id ?? allProjects[0].Id);
  }
  modalPriority.value = "Medium";
  modalStatus.value = "Todo";
});

function handleAiSuggestionClick(button) {
  const title = button.getAttribute("data-title") || "";
  const suggestedProjectId = Number(button.getAttribute("data-project-id"));
  const suggestedPriority = button.getAttribute("data-priority") || "Medium";
  const suggestedDueDate = button.getAttribute("data-due-date") || "";
  const descEnc = button.getAttribute("data-desc") || "";
  let description = "";
  if (descEnc) {
    try {
      description = decodeURIComponent(descEnc);
    } catch {
      description = "";
    }
  }

  const existing = findTaskByTitle(title);
  if (existing) {
    openTaskModal("edit", existing);
    return;
  }

  const pid = Number.isNaN(suggestedProjectId) || suggestedProjectId <= 0 ? defaultProjectIdForAiAdds() : suggestedProjectId;

  openTaskModal("add", {
    title,
    projectId: pid,
    priority: PRIORITY_OPTIONS.includes(suggestedPriority) ? suggestedPriority : "Medium",
    status: "Todo",
    dueDate: suggestedDueDate ? new Date(`${suggestedDueDate}T00:00:00`).toISOString() : null,
    description: description || ""
  });
}

document.addEventListener("click", (event) => {
  const aiSuggestionBtn = event.target.closest(".js-ai-suggestion");
  if (!aiSuggestionBtn) return;
  handleAiSuggestionClick(aiSuggestionBtn);
});

mainPanel.addEventListener("click", async (event) => {
  const editBtn = event.target.closest(".js-edit");
  if (editBtn) {
    activeEditTaskId = Number(editBtn.getAttribute("data-task-id"));
    renderTasks();
    return;
  }

  const cancelBtn = event.target.closest(".js-cancel-edit");
  if (cancelBtn) {
    activeEditTaskId = null;
    renderTasks();
    return;
  }

  const completeBtn = event.target.closest(".js-complete");
  if (completeBtn) {
    const id = Number(completeBtn.getAttribute("data-task-id"));
    const task = allTasks.find((t) => t.id === id);
    if (!task) return;
    await mutateTask(
      id,
      {
        title: task.title,
        description: task.description || null,
        dueDate: task.dueDate,
        priority: PRIORITY_TO_API[task.priority],
        status: "done",
        projectId: task.projectId
      },
      "Task completed."
    );
    return;
  }

  const deleteBtn = event.target.closest(".js-delete");
  if (deleteBtn) {
    const id = Number(deleteBtn.getAttribute("data-task-id"));
    if (!id) return;
    pendingActions.add(id);
    renderTasks();
    setStatus("Deleting...", "loading");
    try {
      await fetchJson(`${API_BASE_URL}/api/tasks/${id}`, { method: "DELETE" });
      allTasks = allTasks.filter((t) => t.id !== id);
      if (activeEditTaskId === id) activeEditTaskId = null;
      setStatus("Task deleted.", "success");
      renderTasks();
    } catch (e) {
      const msg = e instanceof Error ? e.message : "Delete failed.";
      setStatus(msg, "error");
    } finally {
      pendingActions.delete(id);
    }
  }
});

mainPanel.addEventListener("submit", async (event) => {
  const form = event.target.closest(".edit-form");
  if (!form) return;
  event.preventDefault();

  const id = Number(form.getAttribute("data-task-id"));
  const formData = new FormData(form);
  const title = String(formData.get("title") || "").trim();
  const description = String(formData.get("description") || "").trim();
  const projectId = Number(formData.get("projectId"));
  const priority = String(formData.get("priority") || "Medium");
  const status = String(formData.get("status") || "Todo");
  const dueValue = String(formData.get("dueDate") || "");

  if (!title) return setStatus("Title is required.", "error");
  if (!projectId || Number.isNaN(projectId)) return setStatus("Project is required.", "error");

  await mutateTask(
    id,
    {
      title,
      description: description || null,
      dueDate: dueValue ? new Date(`${dueValue}T00:00:00`).toISOString() : null,
      priority: PRIORITY_TO_API[priority] || "medium",
      status: STATUS_TO_API[status] || "todo",
      projectId
    },
    "Task updated."
  );

  activeEditTaskId = null;
  renderTasks();
});

focusModeBtn.addEventListener("click", () => {
  focusModeEnabled = !focusModeEnabled;
  focusModeBtn.setAttribute("aria-pressed", String(focusModeEnabled));
  focusModeBtn.textContent = focusModeEnabled ? "Focus mode: on" : "Focus mode";
  renderTasks();
});

projectFilter.addEventListener("change", () => {
  renderTasks();
  void refreshAiSuggestions();
});
priorityFilter.addEventListener("change", renderTasks);
statusFilter.addEventListener("change", renderTasks);

if (apiRetryBtn) apiRetryBtn.addEventListener("click", () => loadDashboard());
fallbackRetryBtn.addEventListener("click", () => loadDashboard());
aiRefreshBtn.addEventListener("click", () => refreshAiSuggestions());
aiSuggestionModeSelect.addEventListener("change", () => {
  aiSuggestionMode = aiSuggestionModeSelect.value || "llm";
  refreshAiSuggestions();
});
quickProject.addEventListener("focus", () => {
  previousQuickProjectValue = quickProject.value;
});
modalProject.addEventListener("focus", () => {
  previousModalProjectValue = modalProject.value;
});
quickProject.addEventListener("change", () => {
  if (quickProject.value !== CREATE_GROUP_OPTION_VALUE) return;
  quickProject.value = previousQuickProjectValue || "";
  openCreateGroupModal("quick");
});
modalProject.addEventListener("change", () => {
  if (modalProject.value !== CREATE_GROUP_OPTION_VALUE) return;
  modalProject.value = previousModalProjectValue || "";
  openCreateGroupModal("modal");
});
groupModalForm.addEventListener("submit", async (event) => {
  event.preventDefault();
  await createGroupFromModal();
});

fillFilterDropdowns();
loadDashboard().then(() => {
  quickTitle.focus();
});

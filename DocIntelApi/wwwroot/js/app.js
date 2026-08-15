const API = "/api/v1";
const TOKEN_KEY = "docintel.token";
const NAME_KEY = "docintel.name";
const ADMIN_KEY = "docintel.isAdmin";

const state = {
  token: localStorage.getItem(TOKEN_KEY) || "",
  name: localStorage.getItem(NAME_KEY) || "",
  isAdmin: localStorage.getItem(ADMIN_KEY) === "1",
  docs: [],
  activeId: null,
  pollTimer: null,
};

const $ = (sel) => document.querySelector(sel);

const ui = {
  authView: $("#auth-view"),
  appView: $("#app-view"),
  loginForm: $("#login-form"),
  registerForm: $("#register-form"),
  authError: $("#auth-error"),
  userName: $("#user-name"),
  logoutBtn: $("#logout-btn"),
  refreshBtn: $("#refresh-btn"),
  uploadForm: $("#upload-form"),
  fileInput: $("#file-input"),
  pickFileBtn: $("#pick-file-btn"),
  uploadStatus: $("#upload-status"),
  docList: $("#doc-list"),
  emptyStage: $("#empty-stage"),
  askStage: $("#ask-stage"),
  activeName: $("#active-name"),
  activeStatus: $("#active-status"),
  askForm: $("#ask-form"),
  question: $("#question"),
  askBtn: $("#ask-btn"),
  askHint: $("#ask-hint"),
  askError: $("#ask-error"),
  answerPanel: $("#answer-panel"),
  answerText: $("#answer-text"),
  tokenMeter: $("#token-meter"),
  sourcesList: $("#sources-list"),
  adminNav: $("#admin-nav"),
  workspaceView: $("#workspace-view"),
  adminView: $("#admin-view"),
  adminRefreshBtn: $("#admin-refresh-btn"),
  adminError: $("#admin-error"),
  statUsed: $("#stat-used"),
  statRemaining: $("#stat-remaining"),
  statBudget: $("#stat-budget"),
  statAsks: $("#stat-asks"),
  statPercent: $("#stat-percent"),
  statPercentFill: $("#stat-percent-fill"),
  adminUserRows: $("#admin-user-rows"),
};

function friendlyError(message) {
  if (!message) return "";
  const text = String(message);
  if (/high demand|UNAVAILABLE|503|RESOURCE_EXHAUSTED|Too Many Requests|429/i.test(text)
      || /currently experiencing/i.test(text)) {
    return "The AI service is busy right now. Please wait a moment and try again.";
  }
  if (/generateContent failed|Gemini embed failed|Gemini .* failed/i.test(text)) {
    return "The AI service had a temporary problem. Please try again in a moment.";
  }
  return text;
}

function showError(el, message) {
  if (!message) {
    el.hidden = true;
    el.textContent = "";
    return;
  }
  el.hidden = false;
  el.textContent = friendlyError(message);
}

async function api(path, options = {}) {
  const headers = new Headers(options.headers || {});
  if (state.token) headers.set("Authorization", `Bearer ${state.token}`);
  if (options.json) {
    headers.set("Content-Type", "application/json");
  }

  const res = await fetch(`${API}${path}`, {
    ...options,
    headers,
    body: options.json ? JSON.stringify(options.json) : options.body,
  });

  const text = await res.text();
  let data = null;
  if (text) {
    try { data = JSON.parse(text); } catch { data = text; }
  }

  if (!res.ok) {
    const detail =
      (data && (data.detail || data.title || data.message)) ||
      `Request failed (${res.status})`;
    const err = new Error(detail);
    err.status = res.status;
    err.data = data;
    throw err;
  }

  return data;
}

function setSession({ accessToken, fullName, isAdmin }) {
  state.token = accessToken;
  state.name = fullName || "";
  // Accept boolean true or string "true" from API/local quirks
  state.isAdmin = isAdmin === true || isAdmin === "true" || isAdmin === 1;
  localStorage.setItem(TOKEN_KEY, state.token);
  localStorage.setItem(NAME_KEY, state.name);
  localStorage.setItem(ADMIN_KEY, state.isAdmin ? "1" : "0");
}

function clearSession() {
  state.token = "";
  state.name = "";
  state.isAdmin = false;
  state.docs = [];
  state.activeId = null;
  localStorage.removeItem(TOKEN_KEY);
  localStorage.removeItem(NAME_KEY);
  localStorage.removeItem(ADMIN_KEY);
  stopPolling();
}

function showApp() {
  ui.authView.classList.add("hidden");
  ui.appView.classList.remove("hidden");
  const role = state.isAdmin ? " · [Admin]" : "";
  ui.userName.textContent = state.name
    ? `Signed in as ${state.name}${role}`
    : "";
  ui.adminNav.classList.toggle("hidden", !state.isAdmin);
  showView("workspace");
  loadDocuments();
  startPolling();
}

function showAuth() {
  ui.appView.classList.add("hidden");
  ui.authView.classList.remove("hidden");
}

function showView(view) {
  const isAdminView = view === "admin";
  ui.workspaceView.classList.toggle("hidden", isAdminView);
  ui.adminView.classList.toggle("hidden", !isAdminView);
  document.querySelectorAll(".nav-btn").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.view === view);
  });
  if (isAdminView) loadAdminDashboard();
  refreshIcons();
}

function formatTokens(n) {
  return Number(n || 0).toLocaleString();
}

async function loadAdminDashboard() {
  showError(ui.adminError, "");
  try {
    const data = await api("/admin/usage");
    ui.statUsed.textContent = formatTokens(data.totalTokensUsed);
    ui.statRemaining.textContent = formatTokens(data.tokensRemaining);
    ui.statBudget.textContent = formatTokens(data.tokenBudget);
    ui.statAsks.textContent = formatTokens(data.totalAsks);
    ui.statPercent.textContent = `${data.percentUsed}%`;

    const pct = Math.min(100, Number(data.percentUsed) || 0);
    ui.statPercentFill.style.width = `${pct}%`;
    ui.statPercentFill.classList.remove("level-ok", "level-warn", "level-high", "level-crit");
    if (pct >= 90) ui.statPercentFill.classList.add("level-crit");
    else if (pct >= 75) ui.statPercentFill.classList.add("level-high");
    else if (pct >= 50) ui.statPercentFill.classList.add("level-warn");
    else ui.statPercentFill.classList.add("level-ok");

    const rows = data.byUser || [];
    const maxTokens = Math.max(...rows.map((u) => Number(u.tokensUsed) || 0), 1);

    ui.adminUserRows.innerHTML = rows.length
      ? rows
          .map((u) => {
            const tokens = Number(u.tokensUsed) || 0;
            const share = tokens / maxTokens;
            let tone = "idle";
            if (tokens <= 0) tone = "idle";
            else if (share >= 0.75) tone = "hot";
            else if (share >= 0.4) tone = "warm";
            else tone = "cool";

            return `
        <tr>
          <td>${escapeHtml(u.fullName)}</td>
          <td>${escapeHtml(u.email)}</td>
          <td>${formatTokens(u.askCount)}</td>
          <td class="tokens-cell ${tone}">${formatTokens(tokens)}</td>
        </tr>`;
          })
          .join("")
      : `<tr><td colspan="4">No Ask usage recorded yet.</td></tr>`;
  } catch (err) {
    showError(ui.adminError, err.message);
  }
}

function setAuthTab(tab) {
  document.querySelectorAll("[data-auth-tab]").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.authTab === tab);
  });
  ui.loginForm.classList.toggle("hidden", tab !== "login");
  ui.registerForm.classList.toggle("hidden", tab !== "register");
  showError(ui.authError, "");
}

async function loadDocuments() {
  try {
    const data = await api("/documents");
    state.docs = data.items || [];
    renderDocs();
    renderStage();
  } catch (err) {
    if (err.status === 401) {
      clearSession();
      showAuth();
      showError(ui.authError, "Session expired. Please sign in again.");
      return;
    }
    throw err;
  }
}

function fileExtension(name) {
  const i = name.lastIndexOf(".");
  return i >= 0 ? name.slice(i + 1).toUpperCase() : "FILE";
}

function groundedHint(fileName) {
  const ext = fileExtension(fileName);
  return `Answers are grounded in retrieved passages from this ${ext}.`;
}

function indexingFailedHint(fileName) {
  const ext = fileExtension(fileName);
  return `Indexing failed. Re-upload the ${ext} file.`;
}

function shortName(name, max = 42) {
  if (!name) return "";
  if (name.length <= max) return name;
  const ext = name.includes(".") ? name.slice(name.lastIndexOf(".")) : "";
  const base = name.slice(0, name.length - ext.length);
  const keep = Math.max(8, max - ext.length - 1);
  return `${base.slice(0, keep)}…${ext}`;
}

function refreshIcons() {
  if (window.lucide?.createIcons) window.lucide.createIcons();
}

function renderDocs() {
  if (!state.docs.length) {
    ui.docList.innerHTML = `<li class="hint">No documents yet. Upload one to begin.</li>`;
    refreshIcons();
    return;
  }

  ui.docList.innerHTML = state.docs
    .map((d) => `
      <li>
        <button type="button" class="doc-item ${d.id === state.activeId ? "active" : ""}" data-id="${d.id}" title="${escapeHtml(d.fileName)}">
          <div class="doc-item-top">
            <span class="name">${escapeHtml(shortName(d.fileName))}</span>
            <span class="doc-ext">${escapeHtml(fileExtension(d.fileName))}</span>
          </div>
          <span class="status-pill ${escapeHtml(d.status)}">${escapeHtml(d.status)}</span>
        </button>
      </li>
    `)
    .join("");
  refreshIcons();
}

function renderStage() {
  const doc = state.docs.find((d) => d.id === state.activeId);
  if (!doc) {
    ui.emptyStage.classList.remove("hidden");
    ui.askStage.classList.add("hidden");
    return;
  }

  ui.emptyStage.classList.add("hidden");
  ui.askStage.classList.remove("hidden");
  ui.activeName.textContent = shortName(doc.fileName, 64);
  ui.activeName.title = doc.fileName;
  ui.activeStatus.textContent = doc.status;
  ui.activeStatus.className = `status-pill ${doc.status}`;

  const ready = doc.status === "Ready";
  ui.askBtn.disabled = !ready;
  ui.question.disabled = !ready;
  ui.askHint.textContent = ready
    ? groundedHint(doc.fileName)
    : doc.status === "Failed"
      ? indexingFailedHint(doc.fileName)
      : "Indexing in progress… Ask unlocks when status is Ready.";
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;");
}

function startPolling() {
  stopPolling();
  state.pollTimer = setInterval(async () => {
    const busy = state.docs.some((d) => d.status === "Pending" || d.status === "Processing");
    if (!busy || !state.token) return;
    try { await loadDocuments(); } catch { /* ignore transient poll errors */ }
  }, 2500);
}

function stopPolling() {
  if (state.pollTimer) {
    clearInterval(state.pollTimer);
    state.pollTimer = null;
  }
}

// ── Events ───────────────────────────────────────────────────────────

document.querySelectorAll("[data-auth-tab]").forEach((btn) => {
  btn.addEventListener("click", () => setAuthTab(btn.dataset.authTab));
});

ui.loginForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  showError(ui.authError, "");
  const fd = new FormData(ui.loginForm);
  try {
    const data = await api("/auth/login", {
      method: "POST",
      json: {
        email: fd.get("email"),
        password: fd.get("password"),
      },
    });
    setSession(data);
    showApp();
  } catch (err) {
    showError(ui.authError, err.message);
  }
});

ui.registerForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  showError(ui.authError, "");
  const fd = new FormData(ui.registerForm);
  try {
    const data = await api("/auth/register", {
      method: "POST",
      json: {
        email: fd.get("email"),
        password: fd.get("password"),
        fullName: fd.get("fullName"),
      },
    });
    setSession(data);
    showApp();
  } catch (err) {
    showError(ui.authError, err.message);
  }
});

ui.logoutBtn.addEventListener("click", () => {
  clearSession();
  showAuth();
});

ui.adminNav?.addEventListener("click", (e) => {
  const btn = e.target.closest(".nav-btn");
  if (!btn) return;
  showView(btn.dataset.view);
});

ui.adminRefreshBtn?.addEventListener("click", () => {
  loadAdminDashboard();
});

ui.refreshBtn.addEventListener("click", () => {
  loadDocuments().catch((err) => alert(err.message));
});

ui.pickFileBtn.addEventListener("click", () => ui.fileInput.click());

["dragenter", "dragover"].forEach((evt) => {
  ui.uploadForm.addEventListener(evt, (e) => {
    e.preventDefault();
    ui.uploadForm.classList.add("dragover");
  });
});

["dragleave", "drop"].forEach((evt) => {
  ui.uploadForm.addEventListener(evt, (e) => {
    e.preventDefault();
    ui.uploadForm.classList.remove("dragover");
  });
});

ui.uploadForm.addEventListener("drop", (e) => {
  const file = e.dataTransfer?.files?.[0];
  if (file) uploadFile(file);
});

ui.fileInput.addEventListener("change", () => {
  const file = ui.fileInput.files?.[0];
  if (file) uploadFile(file);
  ui.fileInput.value = "";
});

async function uploadFile(file) {
  ui.uploadStatus.hidden = false;
  ui.uploadStatus.textContent = `Uploading ${shortName(file.name, 36)}…`;

  const body = new FormData();
  body.append("File", file);
  body.append("Description", "Uploaded from DocIntel UI");

  try {
    const doc = await api("/documents", { method: "POST", body });
    ui.uploadStatus.textContent =
      `Uploaded ${shortName(doc.fileName, 32)} · ${doc.status}. Indexing in background.`;
    state.activeId = doc.id;
    ui.answerPanel.classList.add("hidden");
    showError(ui.askError, "");
    await loadDocuments();
  } catch (err) {
    ui.uploadStatus.textContent = friendlyError(err.message);
  }
}

ui.docList.addEventListener("click", (e) => {
  const btn = e.target.closest(".doc-item");
  if (!btn) return;
  state.activeId = btn.dataset.id;
  ui.answerPanel.classList.add("hidden");
  showError(ui.askError, "");
  renderDocs();
  renderStage();
});

ui.askForm.addEventListener("submit", async (e) => {
  e.preventDefault();
  showError(ui.askError, "");
  ui.answerPanel.classList.add("hidden");

  if (!state.activeId) return;

  ui.askBtn.disabled = true;
  ui.askHint.textContent = "Searching passages and generating answer…";

  try {
    const data = await api(`/documents/${state.activeId}/ask`, {
      method: "POST",
      json: {
        question: ui.question.value.trim(),
        topK: 5,
      },
    });

    ui.answerText.textContent = data.answer || "(empty answer)";

    const u = data.usage || data.Usage;
    if (u) {
      const total = u.totalTokens ?? u.TotalTokens ?? 0;
      const prompt = u.promptTokens ?? u.PromptTokens ?? 0;
      const output = u.outputTokens ?? u.OutputTokens ?? 0;
      ui.tokenMeter.textContent =
        `Token usage (admin): ${total} total  ·  ${prompt} input  ·  ${output} output`;
      ui.tokenMeter.classList.remove("hidden");
    } else {
      ui.tokenMeter.classList.add("hidden");
      ui.tokenMeter.textContent = state.isAdmin
        ? "Token usage (admin): not returned by API — sign out/in and ask again."
        : "";
      if (state.isAdmin) ui.tokenMeter.classList.remove("hidden");
    }

    ui.sourcesList.innerHTML = (data.sources || [])
      .map((s) => {
        const full = String(s.text ?? "");
        const preview = full.length > 280 ? `${full.slice(0, 280)}…` : full;
        return `
        <li>
          <span class="meta">Passage ${s.chunkIndex} · similarity ${Number(s.score).toFixed(3)}</span>
          <span class="source-preview">${escapeHtml(preview)}</span>
        </li>`;
      })
      .join("") || "<li>No sources returned.</li>";

    ui.answerPanel.classList.remove("hidden");
    const active = state.docs.find((d) => d.id === state.activeId);
    ui.askHint.textContent = active
      ? groundedHint(active.fileName)
      : "Answers are grounded in retrieved passages from this document.";
  } catch (err) {
    showError(ui.askError, err.message);
    ui.askHint.textContent = "";
  } finally {
    renderStage();
  }
});

// Boot
if (state.token) {
  showApp();
} else {
  showAuth();
}
refreshIcons();


const state = {
  auth: { authenticated: false, mustChangePassword: true },
  config: { keepAliveSeconds: 60, accounts: [] },
  status: null,
  toastTimer: null,
  pollingTimer: null,
};

const $ = (selector, root = document) => root.querySelector(selector);
const $$ = (selector, root = document) => Array.from(root.querySelectorAll(selector));

const views = $$(".view");
const navItems = $$(".nav-item");
const accountTemplate = $("#account-template");
const accountsEditor = $("#accounts-editor");

navItems.forEach((item) => {
  item.addEventListener("click", () => {
    navItems.forEach((nav) => nav.classList.toggle("active", nav === item));
    views.forEach((view) => view.classList.toggle("active", view.id === item.dataset.view));
  });
});

$("#login-form").addEventListener("submit", login);
$("#change-password-form").addEventListener("submit", changePassword);
$("#logout-btn").addEventListener("click", logout);
$("#refresh-btn").addEventListener("click", () => loadAll());
$("#restart-btn").addEventListener("click", () => postAction("/api/service/restart", "保活服务已重启。"));
$("#stop-btn").addEventListener("click", () => postAction("/api/service/stop", "保活服务已停止。"));
$("#add-account-btn").addEventListener("click", () => {
  state.config.accounts.push({ name: "", user: "", password: "", deviceCode: "" });
  renderConfig();
});

$("#config-form").addEventListener("submit", async (event) => {
  event.preventDefault();
  const payload = readConfigForm();
  const saved = await request("/api/config", {
    method: "PUT",
    body: JSON.stringify(payload),
  });
  state.config = saved;
  renderConfig();
  await loadStatus();
  showToast("配置已保存，保活服务正在使用最新配置。");
});

async function init() {
  await loadAuthStatus();
  renderAuthGate();

  if (canUseConsole()) {
    await loadAll();
    startPolling();
  }
}

async function loadAuthStatus() {
  state.auth = await request("/api/auth/status", {}, { skipAuthRedirect: true, silent: true });
}

async function login(event) {
  event.preventDefault();
  const password = $("#admin-password").value;

  try {
    state.auth = await request("/api/auth/login", {
      method: "POST",
      body: JSON.stringify({ password }),
    }, { skipAuthRedirect: true });
    $("#admin-password").value = "";
    renderAuthGate();

    if (state.auth.mustChangePassword) {
      $("#current-admin-password").value = password;
      $("#new-admin-password").focus();
      showToast("首次登录需要先修改管理员密码。");
      return;
    }

    await loadAll();
    startPolling();
  } catch {
    $("#admin-password").focus();
  }
}

async function changePassword(event) {
  event.preventDefault();
  const currentPassword = $("#current-admin-password").value;
  const newPassword = $("#new-admin-password").value;
  const confirmPassword = $("#confirm-admin-password").value;

  if (newPassword !== confirmPassword) {
    showToast("两次输入的新密码不一致。");
    $("#confirm-admin-password").focus();
    return;
  }

  const result = await request("/api/auth/change-password", {
    method: "POST",
    body: JSON.stringify({ currentPassword, newPassword }),
  }, { skipAuthRedirect: true });

  $("#current-admin-password").value = "";
  $("#new-admin-password").value = "";
  $("#confirm-admin-password").value = "";
  await loadAuthStatus();
  renderAuthGate();
  await loadAll();
  startPolling();
  showToast(result.message || "管理员密码已更新。");
}

async function logout() {
  await request("/api/auth/logout", { method: "POST" }, { skipAuthRedirect: true, silent: true });
  stopPolling();
  state.auth = { authenticated: false, mustChangePassword: false };
  state.status = null;
  renderAuthGate();
  showToast("已退出登录。");
}

async function loadAll() {
  await Promise.all([loadConfig(), loadStatus()]);
}

async function loadConfig() {
  state.config = await request("/api/config");
  renderConfig();
}

async function loadStatus() {
  state.status = await request("/api/status");
  renderStatus();
}

async function postAction(url, message) {
  await request(url, { method: "POST" });
  await loadStatus();
  showToast(message);
}

function renderAuthGate() {
  const authScreen = $("#auth-screen");
  const shell = $("#app-shell");
  const loginPanel = $("#login-panel");
  const changePanel = $("#change-password-panel");
  const needsPasswordChange = state.auth.authenticated && state.auth.mustChangePassword;

  authScreen.hidden = canUseConsole();
  shell.hidden = !canUseConsole();
  loginPanel.hidden = state.auth.authenticated;
  changePanel.hidden = !needsPasswordChange;

  if (!state.auth.authenticated) {
    $("#admin-password").focus();
  }
}

function canUseConsole() {
  return state.auth.authenticated && !state.auth.mustChangePassword;
}

function startPolling() {
  stopPolling();
  state.pollingTimer = setInterval(() => {
    if (canUseConsole()) {
      loadStatus().catch(() => {});
    }
  }, 5000);
}

function stopPolling() {
  if (state.pollingTimer) {
    clearInterval(state.pollingTimer);
    state.pollingTimer = null;
  }
}

function renderConfig() {
  $("#keep-alive-seconds").value = state.config.keepAliveSeconds || 60;
  accountsEditor.innerHTML = "";

  if (!state.config.accounts.length) {
    state.config.accounts.push({ name: "", user: "", password: "", deviceCode: "" });
  }

  state.config.accounts.forEach((account, index) => {
    const node = accountTemplate.content.firstElementChild.cloneNode(true);
    $(".account-title", node).textContent = account.name || account.user || `账号 ${index + 1}`;
    setInput(node, "name", account.name);
    setInput(node, "user", account.user);
    setInput(node, "password", account.password);
    setInput(node, "deviceCode", account.deviceCode);

    $(".remove-account", node).addEventListener("click", () => {
      state.config.accounts.splice(index, 1);
      renderConfig();
    });
    $(".test-login", node).addEventListener("click", () => accountAction(node, "/api/accounts/test-login", accountPayload(node)));
    $(".send-sms", node).addEventListener("click", () => accountAction(node, "/api/accounts/send-sms", accountPayload(node)));
    $(".bind-device", node).addEventListener("click", () => bindDevice(node));

    accountsEditor.appendChild(node);
  });
}

function renderStatus() {
  const status = state.status || {};
  const accounts = status.accounts || [];
  const desktopCount = accounts.reduce((sum, account) => sum + (account.desktopCount || 0), 0);

  $("#metric-accounts").textContent = accounts.length || (state.config.accounts || []).filter((account) => account.user).length;
  $("#metric-desktops").textContent = desktopCount;
  $("#metric-interval").textContent = `${status.keepAliveSeconds || state.config.keepAliveSeconds || 60}s`;
  $("#config-path").textContent = status.configPath || "";

  const dot = $("#service-dot");
  dot.className = "dot";
  if (status.running) {
    dot.classList.add("running");
    $("#service-state").textContent = "运行中";
    $("#service-detail").textContent = status.configured ? "保活服务已启动" : "等待账号配置";
  } else if (status.configured) {
    dot.classList.add("error");
    $("#service-state").textContent = "未运行";
    $("#service-detail").textContent = "可手动重启服务";
  } else {
    $("#service-state").textContent = "待配置";
    $("#service-detail").textContent = "打开账号配置开始使用";
  }

  renderAccountStates(accounts);
  renderEvents(status.events || []);
}

function renderAccountStates(accounts) {
  const list = $("#account-state-list");
  list.innerHTML = "";

  if (!accounts.length) {
    list.appendChild(emptyState("还没有运行状态。保存账号配置后，保活服务会自动启动。"));
    return;
  }

  accounts.forEach((account) => {
    const item = document.createElement("article");
    item.className = "state-item";
    const desktops = (account.desktops || []).map((desktop) => `
      <div class="desktop">
        <strong>${escapeHtml(desktop.desktopName || desktop.desktopCode || desktop.desktopId || "云电脑")}</strong>
        <small>${escapeHtml(desktop.message || desktop.status || "")}</small>
      </div>
    `).join("");

    item.innerHTML = `
      <div class="state-head">
        <span class="state-name">${escapeHtml(account.name || account.user || "账号")}</span>
        <span class="badge ${escapeHtml(account.status || "")}">${escapeHtml(labelForStatus(account.status))}</span>
      </div>
      <p class="state-message">${escapeHtml(account.message || "")}</p>
      ${desktops ? `<div class="desktops">${desktops}</div>` : ""}
    `;
    list.appendChild(item);
  });
}

function renderEvents(events) {
  const list = $("#event-list");
  list.innerHTML = "";

  if (!events.length) {
    list.appendChild(emptyState("暂无日志。"));
    return;
  }

  events.forEach((event) => {
    const item = document.createElement("article");
    item.className = "event-item";
    item.innerHTML = `
      <div class="event-head">
        <span class="event-scope">${escapeHtml(event.scope || "service")}</span>
        <span class="event-time">${formatTime(event.time)}</span>
      </div>
      <p class="event-message">${escapeHtml(event.message || "")}</p>
    `;
    list.appendChild(item);
  });
}

async function accountAction(node, url, account) {
  const message = $(".account-message", node);
  message.textContent = "处理中...";
  const result = await request(url, {
    method: "POST",
    body: JSON.stringify(account),
  });
  message.textContent = result.message || "操作完成。";
  showToast(result.message || "操作完成。");
  await loadStatus();
}

async function bindDevice(node) {
  const code = getInput(node, "smsCode").trim();
  const message = $(".account-message", node);
  if (!code) {
    message.textContent = "请输入短信验证码。";
    return;
  }

  message.textContent = "正在绑定设备...";
  const result = await request("/api/accounts/bind-device", {
    method: "POST",
    body: JSON.stringify({ account: accountPayload(node), code }),
  });
  message.textContent = result.message || "操作完成。";
  showToast(result.message || "操作完成。");
  await loadStatus();
}

function readConfigForm() {
  return {
    keepAliveSeconds: Number($("#keep-alive-seconds").value) || 60,
    accounts: $$(".account-card", accountsEditor)
      .map(accountPayload)
      .filter((account) => account.user),
  };
}

function accountPayload(node) {
  return {
    name: getInput(node, "name"),
    user: getInput(node, "user"),
    password: getInput(node, "password"),
    deviceCode: getInput(node, "deviceCode"),
  };
}

function setInput(node, field, value) {
  const input = $(`[data-field="${field}"]`, node);
  if (input) input.value = value || "";
}

function getInput(node, field) {
  const input = $(`[data-field="${field}"]`, node);
  return input ? input.value.trim() : "";
}

async function request(url, options = {}, behavior = {}) {
  const response = await fetch(url, {
    credentials: "same-origin",
    headers: { "Content-Type": "application/json", "X-CtYun-CSRF": "1", ...(options.headers || {}) },
    ...options,
  });

  let payload = null;
  const text = await response.text();
  if (text) {
    payload = JSON.parse(text);
  }

  if (!response.ok) {
    const message = payload?.message || messageForStatus(response.status);
    if (!behavior.skipAuthRedirect && (response.status === 401 || response.status === 403)) {
      stopPolling();
      state.auth = {
        authenticated: response.status === 403,
        mustChangePassword: response.status === 403,
      };
      renderAuthGate();
    }

    if (!behavior.silent) {
      showToast(message);
    }
    throw new Error(message);
  }

  return payload;
}

function messageForStatus(status) {
  if (status === 401) return "管理员密码错误或登录已过期。";
  if (status === 403) return "首次登录必须先修改管理员密码。";
  return `请求失败：${status}`;
}

function emptyState(message) {
  const item = document.createElement("div");
  item.className = "state-item";
  item.innerHTML = `<p class="state-message">${escapeHtml(message)}</p>`;
  return item;
}

function showToast(message) {
  const toast = $("#toast");
  toast.textContent = message;
  toast.classList.add("show");
  clearTimeout(state.toastTimer);
  state.toastTimer = setTimeout(() => toast.classList.remove("show"), 3000);
}

function labelForStatus(status) {
  const map = {
    "logging-in": "登录中",
    running: "运行中",
    warning: "注意",
    error: "异常",
    connected: "已连接",
    connecting: "连接中",
    reconnecting: "重连中",
  };
  return map[status] || status || "未知";
}

function formatTime(value) {
  if (!value) return "";
  return new Date(value).toLocaleString("zh-CN", { hour12: false });
}

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&", "&amp;")
    .replaceAll("<", "&lt;")
    .replaceAll(">", "&gt;")
    .replaceAll('"', "&quot;")
    .replaceAll("'", "&#039;");
}

init().catch((error) => showToast(error.message));

(() => {
  document.addEventListener("contextmenu", (event) => event.preventDefault());
  document.addEventListener("dragstart", (event) => event.preventDefault());

  const viewAuth = document.querySelector("[data-view=\"auth\"]");
  const viewChat = document.querySelector("[data-view=\"chat\"]");
  const modeButtons = document.querySelectorAll("[data-mode]");
  const authSections = document.querySelectorAll("[data-auth]");
  const modeTitle = document.querySelector("[data-mode-title]");
  const modeSubtitle = document.querySelector("[data-mode-subtitle]");
  const statusEl = document.querySelector("[data-status]");
  const chatStatusEl = document.querySelector("[data-chat-status-line]");
  const errorEls = document.querySelectorAll("[data-error]");
  const userNameEls = document.querySelectorAll("[data-user-name]");
  const networkCountEl = document.querySelector("[data-network-count]");
  const searchButton = document.querySelector("[data-search-btn]");
  const accountNameEls = document.querySelectorAll("[data-account-name]");
  const renameInput = document.querySelector("[data-rename-input]");
  const displayNameInput = document.querySelector("[data-displayname-input]");
  const renameButton = document.querySelector("[data-rename-btn]");
  const displayNameButton = document.querySelector("[data-displayname-btn]");
  const logoutButton = document.querySelector("[data-logout-btn]");
  const accountStatusEl = document.querySelector("[data-account-status]");
  const settingsSaveButton = document.querySelector("[data-settings-save]");
  const settingsStatusEl = document.querySelector("[data-settings-status]");
  const settingsOpenButton = document.querySelector("[data-settings-open]");
  const settingsPanel = document.querySelector("[data-settings-panel]");
  const settingsBackdrop = document.querySelector("[data-settings-backdrop]");
  const settingsCloseButton = document.querySelector("[data-settings-close]");
  const settingsFields = Array.from(document.querySelectorAll("[data-setting]"));
  const twoFactorToggle = document.querySelector("[data-setting=\"twoFactor\"]");
  const twoFactorMethod = document.querySelector("[data-setting=\"twoFactorMethod\"]");
  const typingIndicator = document.querySelector("[data-typing-indicator]");
  const networkKeyInput = document.querySelector("[data-network-key]");
  const networkKeyButton = document.querySelector("[data-network-key-btn]");
  const manualPeerInput = document.querySelector("[data-manual-peer]");
  const manualPeerButton = document.querySelector("[data-manual-peer-btn]");
  const relayHostInput = document.querySelector("[data-relay-host]");
  const relayPortInput = document.querySelector("[data-relay-port]");
  const relayModeSelect = document.querySelector("[data-relay-mode]");
  const relayEnabledToggle = document.querySelector("[data-relay-enabled]");
  const relayServerToggle = document.querySelector("[data-relay-server-enabled]");
  const relayServerPortInput = document.querySelector("[data-relay-server-port]");
  const relaySaveButton = document.querySelector("[data-relay-save]");
  const relayStatusEl = document.querySelector("[data-relay-status]");
  const diagRefreshButton = document.querySelector("[data-diag-refresh]");
  const diagStatusEl = document.querySelector("[data-diag-status]");
  const diagFirewallEl = document.querySelector("[data-diag-firewall]");
  const diagPortsEl = document.querySelector("[data-diag-ports]");
  const diagCryptoEl = document.querySelector("[data-diag-crypto]");
  const diagScanEl = document.querySelector("[data-diag-scan]");
  const diagRelayEl = document.querySelector("[data-diag-relay]");
  const logListEl = document.querySelector("[data-log-list]");
  const logLimitSelect = document.querySelector("[data-log-limit]");
  const logRefreshButton = document.querySelector("[data-log-refresh]");
  const logDownloadButton = document.querySelector("[data-log-download]");
  const logStatusEl = document.querySelector("[data-log-status]");
  const archiveRangeSelect = document.querySelector("[data-archive-range]");
  const archiveExportJsonButton = document.querySelector("[data-archive-export-json]");
  const archiveExportCsvButton = document.querySelector("[data-archive-export-csv]");
  const archiveClearButton = document.querySelector("[data-archive-clear]");
  const archiveStatusEl = document.querySelector("[data-archive-status]");

  const inputs = {
    username: document.querySelector("[data-field=\"username\"]"),
    displayName: document.querySelector("[data-field=\"displayName\"]"),
    password: document.querySelector("[data-field=\"password\"]"),
    registerPassword: document.querySelector("[data-field=\"registerPassword\"]"),
    confirmPassword: document.querySelector("[data-field=\"confirmPassword\"]"),
  };

  const loginButton = document.querySelector("[data-action=\"login\"]");
  const registerButton = document.querySelector("[data-action=\"register\"]");

  const contactsEl = document.querySelector("[data-contacts]");
  const searchInput = document.querySelector("[data-search]");
  const chatTitleEl = document.querySelector("[data-chat-title]");
  const chatStatusText = document.querySelector("[data-chat-status]");
  const chatAvatarEl = document.querySelector("[data-chat-avatar]");
  const connectionQualityEl = document.querySelector("[data-connection-quality]");
  const messagesEl = document.querySelector("[data-messages]");
  const composeInput = document.querySelector("[data-compose]");
  const sendButton = document.querySelector("[data-send]");
  const messageSearchInput = document.querySelector("[data-message-search]");
  const messageFilterSelect = document.querySelector("[data-message-filter]");
  const messageClearButton = document.querySelector("[data-message-clear]");
  const fileInput = document.querySelector("[data-file-input]");
  const filePick = document.querySelector("[data-file-pick]");
  const fileChip = document.querySelector("[data-file-chip]");
  const fileNameEl = document.querySelector("[data-file-name]");
  const fileSizeEl = document.querySelector("[data-file-size]");
  const fileClear = document.querySelector("[data-file-clear]");
  const dropZone = document.querySelector("[data-dropzone]");
  const dropOverlay = document.querySelector("[data-drop-overlay]");

  const state = {
    mode: "login",
    view: "auth",
    user: null,
    contacts: [],
    threads: {},
    activeContactId: null,
    messageQuery: "",
    messageFilter: "all",
    forceScroll: false,
  };
  let dragCounter = 0;
  let uiStatusTimer = null;
  let typingTimer = null;
  let typingActive = false;

  const copy = {
    login: {
      title: "Tekrar hos geldin",
      subtitle: "Yerel ag icindeki kisilerle baglanti kur.",
    },
    register: {
      title: "Yeni hesap ac",
      subtitle: "Hesabini olustur, sohbet etmeye basla.",
    },
  };

  const pending = new Map();
  const hasHost = () => Boolean(window.chrome && window.chrome.webview);
  const postMessageRaw = (type, payload) => {
    if (!hasHost()) {
      return;
    }
    const id = (window.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`).toString();
    window.chrome.webview.postMessage({ id, type, payload });
  };

  const escapeHtml = (value) =>
    String(value ?? "")
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/\"/g, "&quot;")
      .replace(/'/g, "&#39;");

  const postMessage = (type, payload) => {
    if (!hasHost()) {
      return Promise.resolve({
        id: "local",
        type,
        ok: false,
        payload: { message: "WebView2 baglantisi hazir degil." },
        errors: [{ field: "general", message: "WebView2 baglantisi hazir degil." }],
      });
    }

    const id = (window.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random()}`).toString();
    window.chrome.webview.postMessage({ id, type, payload });

    return new Promise((resolve) => {
      pending.set(id, resolve);
      setTimeout(() => {
        if (pending.has(id)) {
          pending.delete(id);
          resolve({
            id,
            type,
            ok: false,
            payload: { message: "Istek zaman asimina ugradi." },
            errors: [{ field: "general", message: "Istek zaman asimina ugradi." }],
          });
        }
      }, 8000);
    });
  };

  const setView = (view) => {
    state.view = view;
    viewAuth?.classList.toggle("hidden", view !== "auth");
    viewChat?.classList.toggle("hidden", view !== "chat");
    if (view !== "chat") {
      closeSettings();
      hideDropOverlay();
    }
  };

  const setStatus = (message, tone = "info") => {
    if (!statusEl) {
      return;
    }

    statusEl.textContent = message;
    statusEl.dataset.tone = tone;
    statusEl.classList.toggle("hidden", message.length === 0);
  };

  const setChatStatus = (message, tone = "info") => {
    if (!chatStatusEl) {
      return;
    }

    chatStatusEl.textContent = message;
    chatStatusEl.dataset.tone = tone;
    chatStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const showUiStatus = (message, tone = "info", autoClearMs = 0) => {
    setStatus(message, tone);
    setChatStatus(message, tone);
    if (uiStatusTimer) {
      clearTimeout(uiStatusTimer);
      uiStatusTimer = null;
    }
    if (autoClearMs > 0) {
      uiStatusTimer = setTimeout(() => {
        setStatus("", "info");
        setChatStatus("", "info");
      }, autoClearMs);
    }
  };

  const setAccountStatus = (message, tone = "info") => {
    if (!accountStatusEl) {
      return;
    }

    accountStatusEl.textContent = message;
    accountStatusEl.dataset.tone = tone;
    accountStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const setSettingsStatus = (message, tone = "info") => {
    if (!settingsStatusEl) {
      return;
    }

    settingsStatusEl.textContent = message;
    settingsStatusEl.dataset.tone = tone;
    settingsStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const setRelayStatus = (message, tone = "info") => {
    if (!relayStatusEl) {
      return;
    }

    relayStatusEl.textContent = message;
    relayStatusEl.dataset.tone = tone;
    relayStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const setDiagStatus = (message, tone = "info") => {
    if (!diagStatusEl) {
      return;
    }

    diagStatusEl.textContent = message;
    diagStatusEl.dataset.tone = tone;
    diagStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const setLogStatus = (message, tone = "info") => {
    if (!logStatusEl) {
      return;
    }

    logStatusEl.textContent = message;
    logStatusEl.dataset.tone = tone;
    logStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const setArchiveStatus = (message, tone = "info") => {
    if (!archiveStatusEl) {
      return;
    }

    archiveStatusEl.textContent = message;
    archiveStatusEl.dataset.tone = tone;
    archiveStatusEl.classList.toggle("hidden", message.length === 0);
  };

  const loadSettings = () => {
    try {
      const raw = localStorage.getItem("lnm.settings");
      if (!raw) {
        return {};
      }
      return JSON.parse(raw);
    } catch (error) {
      return {};
    }
  };

  const applySettings = (settings) => {
    settingsFields.forEach((field) => {
      const key = field.dataset.setting;
      if (!key || !(key in settings)) {
        return;
      }

      if (field.type === "checkbox") {
        field.checked = Boolean(settings[key]);
      } else {
        field.value = settings[key];
      }
    });

    syncTwoFactorMethod();
    applyUiPreferences(settings);
  };

  const collectSettings = () => {
    const settings = {};
    settingsFields.forEach((field) => {
      const key = field.dataset.setting;
      if (!key) {
        return;
      }
      if (field.type === "checkbox") {
        settings[key] = field.checked;
      } else {
        settings[key] = field.value;
      }
    });
    return settings;
  };

  const syncTwoFactorMethod = () => {
    if (!twoFactorToggle || !twoFactorMethod) {
      return;
    }

    const enabled = twoFactorToggle.checked;
    twoFactorMethod.disabled = !enabled;
  };

  const persistSettings = () => {
    const settings = collectSettings();
    localStorage.setItem("lnm.settings", JSON.stringify(settings));
    applyUiPreferences(settings);
  };

  const applyUiPreferences = (settings) => {
    const themeSetting = settings?.theme ?? "Koyu";
    const densitySetting = settings?.density ?? "Rahat";
    const fontSetting = settings?.fontSize ?? "Orta";

    const root = document.documentElement;
    const prefersLight = window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches;
    if (themeSetting === "Acik") {
      root.dataset.theme = "light";
    } else if (themeSetting === "Koyu") {
      root.dataset.theme = "dark";
    } else {
      root.dataset.theme = prefersLight ? "light" : "dark";
    }

    if (densitySetting === "Sik") {
      root.dataset.density = "compact";
    } else if (densitySetting === "Rahat") {
      root.dataset.density = "cozy";
    } else {
      root.dataset.density = "normal";
    }

    let fontSize = "16px";
    if (fontSetting === "Kucuk") {
      fontSize = "14px";
    } else if (fontSetting === "Buyuk") {
      fontSize = "18px";
    }
    root.style.setProperty("--base-font-size", fontSize);
  };

  const applyDiagnostics = (snapshot) => {
    if (!snapshot) {
      return;
    }

    const firewallLabel = snapshot.firewall?.ok
      ? "Acik"
      : (snapshot.firewall?.message || "Kapali");
    if (diagFirewallEl) {
      diagFirewallEl.textContent = `${firewallLabel}`;
    }
    if (diagPortsEl) {
      const discovery = snapshot.ports?.discovery ?? "-";
      const tcp = snapshot.ports?.tcp ?? "-";
      diagPortsEl.textContent = `UDP ${discovery} / TCP ${tcp}`;
    }
    if (diagCryptoEl) {
      const mode = snapshot.crypto?.mode ?? "unknown";
      diagCryptoEl.textContent = mode === "dll" ? "DLL aktif" : mode === "process" ? "Proses" : "Pasif";
    }
    if (diagScanEl) {
      const mode = snapshot.scan?.mode ?? "basic";
      diagScanEl.textContent = mode === "pythonnet" ? "Python.NET" : mode === "process" ? "Proses" : "Basit";
    }
    if (diagRelayEl) {
      const relay = snapshot.relay ?? {};
      const state = relay.connected ? "Bagli" : "Kapali";
      const mode = relay.mode || "local";
      const host = relay.host ? ` (${relay.host})` : "";
      diagRelayEl.textContent = `${mode} / ${state}${host}`;
    }

    if (relayHostInput) {
      relayHostInput.value = snapshot.relay?.host ?? "";
    }
    if (relayPortInput) {
      relayPortInput.value = snapshot.relay?.port ? String(snapshot.relay.port) : "";
    }
    if (relayServerPortInput) {
      relayServerPortInput.value = snapshot.relay?.serverPort ? String(snapshot.relay.serverPort) : "";
    }
    if (relayEnabledToggle) {
      relayEnabledToggle.checked = Boolean(snapshot.relay?.enabled);
    }
    if (relayServerToggle) {
      relayServerToggle.checked = Boolean(snapshot.relay?.serverEnabled);
    }
    if (relayModeSelect) {
      relayModeSelect.value = snapshot.relay?.mode || "local";
    }
  };

  const fetchDiagnostics = async () => {
    if (!hasHost()) {
      return;
    }

    setDiagStatus("");
    const response = await postMessage("diag.snapshot", {});
    if (!response.ok) {
      setDiagStatus(response.payload?.message || "Durum bilgisi alinmadi.", "error");
      return;
    }

    applyDiagnostics(response.payload);
  };

  const formatLogTime = (value) => {
    if (!value) {
      return "";
    }
    const date = new Date(value);
    if (Number.isNaN(date.getTime())) {
      return "";
    }
    return date.toLocaleString("tr-TR", { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
  };

  const renderLogs = (entries) => {
    if (!logListEl) {
      return;
    }

    if (!entries || entries.length === 0) {
      logListEl.innerHTML = "<div class=\"empty-state\">Log kaydi bulunamadi.</div>";
      return;
    }

    const items = [...entries]
      .reverse()
      .map((entry) => {
        const title = escapeHtml(entry.type || "olay");
        const user = entry.user ? `@${escapeHtml(entry.user)}` : "Sistem";
        const message = escapeHtml(entry.message || "");
        const details = entry.details ? ` · ${escapeHtml(entry.details)}` : "";
        const time = formatLogTime(entry.at);
        return `
          <div class="log-item">
            <div>
              <div class="log-title">${title} <span class="log-meta">${user}</span></div>
              <div class="log-message">${message}${details}</div>
            </div>
            <div class="log-meta">${escapeHtml(time)}</div>
          </div>`;
      })
      .join("");

    logListEl.innerHTML = items;
  };

  const fetchLogs = async () => {
    if (!hasHost()) {
      return;
    }

    setLogStatus("");
    const limitValue = logLimitSelect?.value ?? "50";
    const limit = Number.parseInt(limitValue, 10) || 50;
    const response = await postMessage("logs.security", { limit });
    if (!response.ok) {
      setLogStatus(response.payload?.message || "Loglar yuklenemedi.", "error");
      return;
    }

    renderLogs(response.payload?.entries || []);
  };

  const handleLogDownload = async () => {
    if (!hasHost()) {
      return;
    }

    setLogStatus("");
    const response = await postMessage("logs.download", {});
    if (!response.ok) {
      if (response.payload?.cancelled) {
        setLogStatus(response.payload?.message || "Islem iptal edildi.", "info");
      } else {
        setLogStatus(response.payload?.message || "Log indirilemedi.", "error");
      }
      return;
    }

    setLogStatus(response.payload?.message || "Log kaydedildi.");
  };

  const getArchiveRangeDays = () => {
    if (!archiveRangeSelect) {
      return null;
    }
    const value = archiveRangeSelect.value;
    if (value === "all") {
      return null;
    }
    const parsed = Number.parseInt(value, 10);
    return Number.isNaN(parsed) ? null : parsed;
  };

  const handleArchiveExport = async (format) => {
    if (!hasHost()) {
      return;
    }

    setArchiveStatus("");
    const response = await postMessage("archive.export", {
      format,
      rangeDays: getArchiveRangeDays(),
    });
    if (!response.ok) {
      if (response.payload?.cancelled) {
        setArchiveStatus(response.payload?.message || "Islem iptal edildi.", "info");
      } else {
        setArchiveStatus(response.payload?.message || "Arsiv kaydedilemedi.", "error");
      }
      return;
    }

    setArchiveStatus(response.payload?.message || "Arsiv kaydedildi.");
  };

  const handleArchiveClear = async () => {
    if (!hasHost()) {
      return;
    }

    setArchiveStatus("Arsiv temizleniyor...");
    const response = await postMessage("archive.clear", {
      rangeDays: getArchiveRangeDays(),
    });
    if (!response.ok) {
      setArchiveStatus(response.payload?.message || "Arsiv temizlenemedi.", "error");
      return;
    }

    setArchiveStatus(response.payload?.message || "Arsiv temizlendi.");
    await loadSnapshot();
  };

  function openSettings() {
    if (!settingsPanel) {
      return;
    }

    settingsPanel.classList.remove("hidden");
    setAccountStatus("");
    setSettingsStatus("");
    setRelayStatus("");
    setDiagStatus("");
    applySettings(loadSettings());
    fetchDiagnostics();
    fetchLogs();
    renameInput?.focus();
  }

  function closeSettings() {
    if (!settingsPanel) {
      return;
    }

    settingsPanel.classList.add("hidden");
    setAccountStatus("");
    setSettingsStatus("");
    setRelayStatus("");
    setDiagStatus("");
    setLogStatus("");
    setArchiveStatus("");
  }

  const clearErrors = () => {
    errorEls.forEach((el) => {
      el.textContent = "";
      el.classList.add("hidden");
    });
  };

  const setErrors = (errors) => {
    if (!errors || errors.length === 0) {
      return;
    }

    let generalMessage = "";
    errors.forEach((error) => {
      const targets = document.querySelectorAll(`[data-error=\"${error.field}\"]`);
      if (targets.length > 0) {
        targets.forEach((target) => {
          target.textContent = error.message;
          target.classList.remove("hidden");
        });
      } else if (!generalMessage) {
        generalMessage = error.message;
      }
    });

    if (generalMessage) {
      setStatus(generalMessage, "error");
    }
  };

  const setMode = (mode) => {
    state.mode = mode;
    const next = copy[mode];
    if (next && modeTitle && modeSubtitle) {
      modeTitle.textContent = next.title;
      modeSubtitle.textContent = next.subtitle;
    }

    modeButtons.forEach((button) => {
      const isActive = button.dataset.mode === mode;
      button.classList.toggle("is-active", isActive);
      button.setAttribute("aria-pressed", isActive ? "true" : "false");
    });

    authSections.forEach((section) => {
      const scope = section.dataset.auth;
      const shouldShow = scope === mode || scope === "both";
      section.classList.toggle("hidden", !shouldShow);
    });

    clearErrors();
    setStatus("");
  };

  const formatTime = (value) => {
    const date = value ? new Date(value) : new Date();
    if (Number.isNaN(date.getTime())) {
      return "";
    }

    return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
  };

  const formatSize = (bytes) => {
    if (!bytes && bytes !== 0) {
      return "";
    }

    if (bytes < 1024) {
      return `${bytes} B`;
    }

    const kb = bytes / 1024;
    if (kb < 1024) {
      return `${kb.toFixed(1)} KB`;
    }

    const mb = kb / 1024;
    return `${mb.toFixed(1)} MB`;
  };

  const isNearBottom = (el) => {
    if (!el) {
      return true;
    }
    const threshold = 80;
    return el.scrollTop + el.clientHeight >= el.scrollHeight - threshold;
  };

  const formatQuality = (contact) => {
    if (!contact || contact.id === "all") {
      return "";
    }

    const ping = Number.isFinite(contact.pingMs) ? Math.round(contact.pingMs) : null;
    const loss = Number.isFinite(contact.lossPercent) ? Math.round(contact.lossPercent) : null;
    const parts = [];
    if (ping !== null) {
      parts.push(`${ping} ms`);
    }
    if (loss !== null) {
      parts.push(`%${loss} kayip`);
    }
    return parts.join(" · ");
  };

  const mapDeliveryState = (state) => {
    switch (state) {
      case "read":
        return { label: "Okundu", className: "read" };
      case "delivered":
        return { label: "Ulasti", className: "delivered" };
      case "sent":
        return { label: "Gonderildi", className: "sent" };
      case "failed":
        return { label: "Gonderilemedi", className: "failed" };
      default:
        return { label: "", className: "" };
    }
  };

  const mapTransferState = (state) => {
    switch (state) {
      case "sending":
        return "Gonderiliyor";
      case "receiving":
        return "Aliniyor";
      case "completed":
        return "Tamamlandi";
      case "failed":
        return "Basarisiz";
      default:
        return "";
    }
  };

  const MAX_FILE_BYTES = 200 * 1024 * 1024;
  const MAX_MESSAGE_CHARS = 1200;

  const readFileAsBase64 = (file) =>
    new Promise((resolve, reject) => {
      const reader = new FileReader();
      reader.onerror = () => reject(new Error("Dosya okunamadi."));
      reader.onload = () => {
        const buffer = reader.result;
        if (!(buffer instanceof ArrayBuffer)) {
          reject(new Error("Dosya okunamadi."));
          return;
        }
        const bytes = new Uint8Array(buffer);
        let binary = "";
        for (let i = 0; i < bytes.length; i += 1) {
          binary += String.fromCharCode(bytes[i]);
        }
        resolve(btoa(binary));
      };
      reader.readAsArrayBuffer(file);
    });

  const splitMessage = (text) => {
    if (text.length <= MAX_MESSAGE_CHARS) {
      return [text];
    }
    const parts = [];
    let start = 0;
    while (start < text.length) {
      parts.push(text.slice(start, start + MAX_MESSAGE_CHARS));
      start += MAX_MESSAGE_CHARS;
    }
    return parts;
  };

  const setComposerEnabled = (enabled) => {
    if (composeInput) {
      composeInput.disabled = !enabled;
    }
    if (sendButton) {
      sendButton.disabled = !enabled;
    }
    if (fileInput) {
      fileInput.disabled = !enabled;
    }
    if (filePick) {
      filePick.classList.toggle("is-disabled", !enabled);
    }
  };

  const clearFileChip = () => {
    fileChip?.classList.add("hidden");
    if (fileNameEl) {
      fileNameEl.textContent = "";
    }
    if (fileSizeEl) {
      fileSizeEl.textContent = "";
    }
  };

  const showDropOverlay = () => {
    if (!dropOverlay) {
      return;
    }

    dropOverlay.classList.remove("hidden");
  };

  const hideDropOverlay = () => {
    if (!dropOverlay) {
      return;
    }

    dragCounter = 0;
    dropOverlay.classList.add("hidden");
  };

  const initialsFromName = (name) => {
    return name
      .split(" ")
      .map((part) => part.charAt(0))
      .slice(0, 2)
      .join("")
      .toUpperCase();
  };

  const canSendTyping = () =>
    Boolean(state.activeContactId) && state.activeContactId !== "all";

  const indicateTyping = (value) => {
    if (!canSendTyping()) {
      typingActive = false;
      return;
    }

    if (typingActive === value) {
      return;
    }

    typingActive = value;
    postMessageRaw("chat.typing", {
      threadId: state.activeContactId,
      isTyping: value,
    });
  };

  const scheduleTypingStop = () => {
    if (typingTimer) {
      clearTimeout(typingTimer);
    }
    typingTimer = setTimeout(() => {
      indicateTyping(false);
    }, 1600);
  };

  const renderContacts = () => {
    if (!contactsEl) {
      return;
    }

    const query = (searchInput?.value ?? "").trim().toLowerCase();
    const filtered = state.contacts.filter((contact) =>
      contact.displayName.toLowerCase().includes(query)
    );

    const sorted = [...filtered].sort((a, b) => {
      if (a.id === "all") {
        return -1;
      }
      if (b.id === "all") {
        return 1;
      }
      return a.displayName.localeCompare(b.displayName, "tr");
    });

    if (sorted.length === 0) {
      contactsEl.innerHTML = "<div class=\"empty-state\">Agda aktif kisi yok.</div>";
      return;
    }

    const items = sorted
      .map((contact) => {
        const name = escapeHtml(contact.displayName);
        const preview = escapeHtml(contact.preview || "");
        const time = escapeHtml(contact.status || "");
        const typing = Boolean(contact.isTyping);
        const initials = initialsFromName(name) || "LN";
        const active = contact.id === state.activeContactId;
        const badge = contact.unreadCount > 0
          ? `<span class=\"contact-badge\">${contact.unreadCount}</span>`
          : "";
        const previewMarkup = typing
          ? `<div class=\"contact-preview typing\">Yaziyor...</div>`
          : (preview
            ? `<div class=\"contact-preview\">${preview}</div>`
            : `<div class=\"contact-preview muted\">${contact.id === "all" ? "Genel sohbet" : "Henuz mesaj yok."}</div>`);
        const statusLabel = contact.id === "all"
          ? "Genel sohbet"
          : (time ? `Son gorulme: ${time}` : "");
        const statusMarkup = statusLabel
          ? `<div class=\"contact-status\">${escapeHtml(statusLabel)}</div>`
          : "";
        const qualityLabel = formatQuality(contact);
        const qualityMarkup = qualityLabel
          ? `<div class=\"contact-quality\">${escapeHtml(qualityLabel)}</div>`
          : "";
        const dot = contact.id !== "all"
          ? `<span class=\"contact-dot ${contact.isOnline ? "is-online" : ""}\"></span>`
          : "";

        return `
          <div class="contact-card" data-contact-id="${contact.id}" data-active="${active}">
            <div class="contact-avatar">${initials}${dot}</div>
            <div class="contact-main">
              <div class="contact-name">${name}</div>
              ${previewMarkup}
              ${statusMarkup}
              ${qualityMarkup}
            </div>
            ${badge}
          </div>`;
      })
      .join("");

    contactsEl.innerHTML = items;
  };

  const renderMessages = () => {
    if (!messagesEl) {
      return;
    }

    const messages = state.threads[state.activeContactId] || [];
    const query = state.messageQuery.trim().toLowerCase();
    const filter = state.messageFilter;
    const filtered = messages.filter((message) => {
      if (filter === "files" && !message.attachment) {
        return false;
      }
      if (filter === "text" && message.attachment) {
        return false;
      }
      if (!query) {
        return true;
      }
      const textMatch = (message.text || "").toLowerCase().includes(query);
      const fileMatch = message.attachment
        ? (message.attachment.fileName || "").toLowerCase().includes(query)
        : false;
      return textMatch || fileMatch;
    });
    if (filtered.length === 0) {
      const emptyText = query || filter !== "all"
        ? "Sonuc bulunamadi."
        : "Henuz mesaj yok.";
      messagesEl.innerHTML = `<div class=\"empty-state\">${emptyText}</div>`;
      if (state.forceScroll) {
        messagesEl.scrollTop = messagesEl.scrollHeight;
        state.forceScroll = false;
      }
      return;
    }

    const rows = filtered
      .map((message) => {
        const mineClass = message.isMine ? "message-row mine" : "message-row";
        const text = escapeHtml(message.text);
        const sender = escapeHtml(message.sender);
        const time = formatTime(message.sentAt);
        const deliveryInfo = message.isMine ? mapDeliveryState(message.deliveryState) : { label: "", className: "" };
        const deliveryMarkup = deliveryInfo.label
          ? `<span class=\"message-status ${deliveryInfo.className}\">${deliveryInfo.label}</span>`
          : "";
        let attachment = "";
        if (message.attachment) {
          const fileName = escapeHtml(message.attachment.fileName);
          const size = formatSize(message.attachment.sizeBytes);
          const scanStatus = message.attachment.status ? escapeHtml(message.attachment.status) : "";
          const transferLabel = mapTransferState(message.attachment.transferState);
          const progressValue = typeof message.attachment.progress === "number"
            ? Math.max(0, Math.min(100, message.attachment.progress))
            : null;
          const progressMarkup = progressValue !== null
            ? `<div class=\"attachment-progress\"><span style=\"width:${progressValue}%\"></span></div>`
            : "";
          const preview = message.attachment.previewDataUrl;
          const contentType = message.attachment.contentType || "";
          const localPath = message.attachment.localPath || "";
          let previewMarkup = "";
          if (preview) {
            if (contentType.startsWith("video/")) {
              previewMarkup = `<video class=\"attachment-preview\" src=\"${preview}\" controls preload=\"metadata\" playsinline muted></video>`;
            } else if (contentType.startsWith("audio/")) {
              previewMarkup = `<audio class=\"attachment-audio\" src=\"${preview}\" controls></audio>`;
            } else {
              previewMarkup = `<img class=\"attachment-preview\" src=\"${preview}\" alt=\"${fileName}\" loading=\"lazy\" />`;
            }
          } else if (contentType.startsWith("video/")) {
            previewMarkup = `<div class=\"attachment-placeholder\">Video dosyasi</div>`;
          } else if (contentType.startsWith("audio/")) {
            previewMarkup = `<div class=\"attachment-placeholder\">Ses dosyasi</div>`;
          } else if (contentType.startsWith("image/")) {
            previewMarkup = `<div class=\"attachment-placeholder\">Resim dosyasi</div>`;
          }
          const actionsMarkup = localPath
            ? `
              <div class=\"attachment-actions\">
                <button class=\"attachment-btn\" data-file-action=\"open\" data-file-path=\"${escapeHtml(localPath)}\">Dosyayi ac</button>
                <button class=\"attachment-btn\" data-file-action=\"reveal\" data-file-path=\"${escapeHtml(localPath)}\">Klasorde goster</button>
              </div>`
            : "";
          const metaParts = [size, scanStatus, transferLabel].filter(Boolean).join(" • ");
          const metaMarkup = metaParts ? `<div class=\"attachment-sub\">${metaParts}</div>` : "";
          attachment = `
            <div class=\"attachment\">
              ${previewMarkup}
              <div class=\"attachment-meta\">
                <div class=\"attachment-name\">${fileName}</div>
                ${metaMarkup}
                ${progressMarkup}
                ${actionsMarkup}
              </div>
            </div>`;
        }

        return `
          <div class="${mineClass}">
            <div class="message-bubble">
              <div class="message-sender">${sender}</div>
              <div class="message-text">${text}</div>
              ${attachment}
              <div class="message-foot">
                <span>${time}</span>
                ${deliveryMarkup}
              </div>
            </div>
          </div>`;
      })
      .join("");

    const shouldStick = state.forceScroll || isNearBottom(messagesEl);
    const previousScrollTop = messagesEl.scrollTop;
    messagesEl.innerHTML = `<div class="message-stack">${rows}</div>`;
    if (shouldStick) {
      messagesEl.scrollTop = messagesEl.scrollHeight;
    } else {
      messagesEl.scrollTop = previousScrollTop;
    }
    state.forceScroll = false;
  };

  const setActiveContact = (contactId, notify = true) => {
    const previous = state.activeContactId;
    state.activeContactId = contactId;
    const changed = previous !== contactId;
    const active = state.contacts.find((contact) => contact.id === contactId);
    if (chatTitleEl) {
      chatTitleEl.textContent = active ? active.displayName : "Secili kisi yok";
    }
    if (chatStatusText) {
      if (!active) {
        chatStatusText.textContent = "Bir sohbet sec.";
      } else if (active.id === "all") {
        chatStatusText.textContent = "Yerel agdaki herkese acik sohbet";
      } else if (active.isTyping) {
        chatStatusText.textContent = "Yaziyor...";
      } else if (active.status) {
        chatStatusText.textContent = `Son gorulme: ${active.status}`;
      } else {
        chatStatusText.textContent = "Durum bilgisi yok.";
      }
    }
    if (connectionQualityEl) {
      if (!active || active.id === "all") {
        connectionQualityEl.textContent = "";
        connectionQualityEl.classList.add("hidden");
      } else {
        const quality = formatQuality(active);
        connectionQualityEl.textContent = quality || "Baglanti kalitesi bilinmiyor";
        connectionQualityEl.classList.remove("hidden");
        connectionQualityEl.classList.toggle("muted", !quality);
      }
    }
    if (typingIndicator) {
      typingIndicator.classList.toggle("hidden", !active || !active.isTyping);
    }
    if (chatAvatarEl) {
      const initials = active ? initialsFromName(active.displayName) : "LN";
      chatAvatarEl.textContent = initials || "LN";
    }

    if (messageSearchInput && state.messageQuery) {
      messageSearchInput.value = "";
      state.messageQuery = "";
    }
    if (messageFilterSelect && state.messageFilter !== "all") {
      messageFilterSelect.value = "all";
      state.messageFilter = "all";
    }

    if (notify) {
      indicateTyping(false);
    }
    setComposerEnabled(Boolean(active));
    if (changed) {
      state.forceScroll = true;
    }
    renderContacts();
    renderMessages();
    if (notify) {
      postMessageRaw("chat.active", { threadId: contactId });
    }
  };

  const applyTheme = (theme) => {
    if (!theme) {
      return;
    }

    const root = document.documentElement;
    if (theme.accent) {
      root.style.setProperty("--accent", theme.accent);
    }
    if (theme.accentStrong) {
      root.style.setProperty("--accent-strong", theme.accentStrong);
    }
    if (theme.accentSoft) {
      root.style.setProperty("--accent-soft", theme.accentSoft);
    }
    if (theme.bubbleMine) {
      root.style.setProperty("--chat-bubble-mine", theme.bubbleMine);
    }
  };

  const applySnapshot = (snapshot) => {
    if (!snapshot) {
      return;
    }

    state.user = snapshot.currentUser ?? state.user;
    state.contacts = snapshot.contacts ?? state.contacts;
    state.threads = snapshot.threads ?? state.threads;
    const nextActive = snapshot.activeContactId || state.activeContactId;
    state.activeContactId = nextActive;

    userNameEls.forEach((el) => {
      el.textContent = state.user
        ? `Merhaba, ${state.user.displayName}`
        : "Baglanti bekleniyor...";
    });

    if (accountNameEls.length > 0) {
      const text = state.user
        ? `${state.user.displayName} (@${state.user.username})`
        : "Baglanti bekleniyor...";
      accountNameEls.forEach((el) => {
        el.textContent = text;
      });
    }

    if (renameInput && state.user) {
      renameInput.placeholder = state.user.username;
    }

    if (displayNameInput && state.user) {
      displayNameInput.placeholder = state.user.displayName;
    }

    if (networkCountEl) {
      const total = state.contacts.filter((contact) => contact.id !== "all").length;
      const online = state.contacts.filter((contact) => contact.id !== "all" && contact.isOnline).length;
      networkCountEl.textContent = `${online} aktif`;
      networkCountEl.title = `${total} toplam`;
    }

    setActiveContact(state.activeContactId, !typingActive);
  };

  const loadSnapshot = async () => {
    const response = await postMessage("chat.snapshot", {});
    if (!response.ok) {
      setChatStatus(response.payload?.message || "Sohbet yuklenemedi.", "error");
      return;
    }

    applySnapshot(response.payload);
  };

  const handleAuth = async (type, payload) => {
    clearErrors();
    setStatus("");
    setAccountStatus("");
    const response = await postMessage(type, payload);

    if (!response.ok) {
      setErrors(response.errors || []);
      setStatus(response.payload?.message || "Islem tamamlanamadi.", "error");
      return;
    }

    setStatus(response.payload?.message || "Islem basarili.");
    setView("chat");
    setChatStatus("");
    await loadSnapshot();
  };

  const restoreSession = async () => {
    const response = await postMessage("auth.restore", {});
    if (!response.ok || !response.payload?.user) {
      return;
    }

    setAccountStatus("");
    setView("chat");
    setChatStatus("");
    await loadSnapshot();
  };

  const handleRename = async () => {
    const newUsername = renameInput?.value.trim() ?? "";
    if (!newUsername) {
      setAccountStatus("Yeni kullanici adi gir.", "error");
      return;
    }

    const response = await postMessage("auth.rename", { newUsername });
    if (!response.ok) {
      setAccountStatus(response.payload?.message || "Guncelleme basarisiz.", "error");
      return;
    }

    if (renameInput) {
      renameInput.value = "";
    }

    setAccountStatus(response.payload?.message || "Kullanici adi guncellendi.");
    await loadSnapshot();
  };

  const handleDisplayNameUpdate = async () => {
    const displayName = displayNameInput?.value.trim() ?? "";
    if (!displayName) {
      setAccountStatus("Gorunen adini gir.", "error");
      return;
    }

    const response = await postMessage("auth.displayName", { displayName });
    if (!response.ok) {
      setAccountStatus(response.payload?.message || "Guncelleme basarisiz.", "error");
      return;
    }

    if (displayNameInput) {
      displayNameInput.value = "";
    }

    setAccountStatus(response.payload?.message || "Gorunen ad guncellendi.");
    await loadSnapshot();
  };

  const handleLogout = async () => {
    const response = await postMessage("auth.logout", {});
    if (!response.ok) {
      setAccountStatus(response.payload?.message || "Cikis yapilamadi.", "error");
      return;
    }

    state.user = null;
    state.contacts = [];
    state.threads = {};
    state.activeContactId = null;
    setAccountStatus("");
    setChatStatus("");
    setComposerEnabled(false);
    if (renameInput) {
      renameInput.value = "";
    }
    if (displayNameInput) {
      displayNameInput.value = "";
    }
    closeSettings();
    setView("auth");
    setMode("login");
  };

  const handleSettingsSave = () => {
    persistSettings();
    setSettingsStatus("Ayarlar kaydedildi.");
  };

  const handleNetworkKeySave = async () => {
    if (!networkKeyInput) {
      return;
    }

    const value = networkKeyInput.value.trim();
    const response = await postMessage("settings.networkKey", { networkKey: value });
    if (!response.ok) {
      setSettingsStatus(response.payload?.message || "Ag anahtari kaydedilemedi.", "error");
      return;
    }

    setSettingsStatus(response.payload?.message || "Ag anahtari guncellendi.");
  };

  const handleManualPeer = async () => {
    if (!manualPeerInput) {
      return;
    }

    const endpoint = manualPeerInput.value.trim();
    if (!endpoint) {
      setRelayStatus("Manuel IP bos olamaz.", "error");
      return;
    }

    const response = await postMessage("net.manualPeer", { endpoint });
    if (!response.ok) {
      setRelayStatus(response.payload?.message || "Manuel baglanti kurulamadi.", "error");
      return;
    }

    setRelayStatus(response.payload?.message || "Manuel baglanti eklendi.");
    manualPeerInput.value = "";
    await loadSnapshot();
  };

  const parsePort = (value) => {
    if (!value) {
      return null;
    }
    const parsed = Number.parseInt(value, 10);
    return Number.isNaN(parsed) ? null : parsed;
  };

  const handleRelaySave = async () => {
    const host = relayHostInput?.value.trim() ?? "";
    const mode = relayModeSelect?.value ?? "local";
    const relayEnabled = Boolean(relayEnabledToggle?.checked);
    const relayServerEnabled = Boolean(relayServerToggle?.checked);
    const relayPort = parsePort(relayPortInput?.value);
    const relayServerPort = parsePort(relayServerPortInput?.value);

    const response = await postMessage("relay.config", {
      host,
      mode,
      relayEnabled,
      relayServerEnabled,
      relayPort,
      relayServerPort,
    });

    if (!response.ok) {
      setRelayStatus(response.payload?.message || "Relay ayarlari kaydedilemedi.", "error");
      return;
    }

    setRelayStatus(response.payload?.message || "Relay ayarlari guncellendi.");
    fetchDiagnostics();
  };

  const sendSingleMessage = async (text) => {
    if (!state.activeContactId) {
      setChatStatus("Sohbet secmeden mesaj gonderemezsin.", "error");
      return false;
    }

    if (!text) {
      setChatStatus("Mesaj bos olamaz.", "error");
      return false;
    }

    const response = await postMessage("chat.send", {
      threadId: state.activeContactId,
      text,
    });

    if (!response.ok) {
      setChatStatus(response.payload?.message || "Mesaj gonderilemedi.", "error");
      return false;
    }

    const message = response.payload?.message;
    if (message) {
      if (!state.threads[message.threadId]) {
        state.threads[message.threadId] = [];
      }
      state.threads[message.threadId].push(message);
      const contact = state.contacts.find((item) => item.id === message.threadId);
      if (contact) {
        contact.preview = message.text;
        contact.status = "Az once";
      }
    }

    indicateTyping(false);
    state.forceScroll = true;
    renderMessages();
    renderContacts();
    setChatStatus("");
    return true;
  };

  const handleSend = async () => {
    if (!state.activeContactId) {
      setChatStatus("Sohbet secmeden mesaj gonderemezsin.", "error");
      return;
    }

    const text = composeInput?.value.trim() ?? "";
    if (!text) {
      setChatStatus("Mesaj bos olamaz.", "error");
      return;
    }

    const parts = splitMessage(text);
    if (parts.length > 1) {
      setChatStatus(`Mesaj ${parts.length} parcaya bolundu.`, "info");
    }

    for (const part of parts) {
      const ok = await sendSingleMessage(part);
      if (!ok) {
        return;
      }
    }

    if (composeInput) {
      composeInput.value = "";
    }
  };

  const applyFileResponse = (payload) => {
    const message = payload?.message;
    if (message) {
      if (!state.threads[message.threadId]) {
        state.threads[message.threadId] = [];
      }
      state.threads[message.threadId].push(message);
      const contact = state.contacts.find((item) => item.id === message.threadId);
      if (contact) {
        contact.preview = message.text;
      }
    }

    const scan = payload?.scan;
    if (scan) {
      const details = scan.details ? ` (${scan.details})` : "";
      setChatStatus(`${scan.message || "Dosya taramaya alindi."}${details}`);
    } else {
      setChatStatus("Dosya taramaya alindi.");
    }

    state.forceScroll = true;
    renderMessages();
    renderContacts();
  };

  const handleFileSelection = async (file) => {
    if (!file) {
      return;
    }

    if (!state.activeContactId) {
      setChatStatus("Sohbet secmeden dosya gonderemezsin.", "error");
      return;
    }

    if (file.size > MAX_FILE_BYTES) {
      setChatStatus(`Dosya boyutu ${formatSize(MAX_FILE_BYTES)} uzerinde. Daha kucuk bir dosya sec.`, "error");
      return;
    }

    if (fileChip && fileNameEl && fileSizeEl) {
      fileNameEl.textContent = file.name;
      fileSizeEl.textContent = formatSize(file.size);
      fileChip.classList.remove("hidden");
    }

    setChatStatus("Dosya hazirlaniyor...");
    let dataBase64 = "";
    try {
      dataBase64 = await readFileAsBase64(file);
    } catch (error) {
      setChatStatus(error?.message || "Dosya okunamadi.", "error");
      clearFileChip();
      return;
    }

    const response = await postMessage("chat.attach", {
      threadId: state.activeContactId,
      fileName: file.name,
      sizeBytes: file.size,
      contentType: file.type || "application/octet-stream",
      dataBase64,
    });

    if (!response.ok) {
      setChatStatus(response.payload?.message || "Dosya gonderilemedi.", "error");
      clearFileChip();
      return;
    }

    applyFileResponse(response.payload);
    clearFileChip();
  };

  const handlePickFile = async () => {
    if (!state.activeContactId) {
      setChatStatus("Sohbet secmeden dosya gonderemezsin.", "error");
      return;
    }

    setChatStatus("Dosya seciliyor...");
    const response = await postMessage("chat.pickFile", {
      threadId: state.activeContactId,
    });

    if (!response.ok) {
      if (response.payload?.cancelled) {
        setChatStatus(response.payload?.message || "Dosya secilmedi.", "info");
      } else {
        setChatStatus(response.payload?.message || "Dosya secilemedi.", "error");
      }
      return;
    }

    applyFileResponse(response.payload);
  };

  loginButton?.addEventListener("click", () => {
    handleAuth("auth.login", {
      username: inputs.username?.value || "",
      password: inputs.password?.value || "",
    });
  });

  registerButton?.addEventListener("click", () => {
    handleAuth("auth.register", {
      username: inputs.username?.value || "",
      displayName: inputs.displayName?.value || "",
      password: inputs.registerPassword?.value || "",
      confirmPassword: inputs.confirmPassword?.value || "",
    });
  });

  modeButtons.forEach((button) => {
    button.addEventListener("click", () => {
      const mode = button.dataset.mode;
      if (mode) {
        setMode(mode);
      }
    });
  });

  messagesEl?.addEventListener("click", (event) => {
    const actionEl = event.target.closest("[data-file-action]");
    if (!actionEl) {
      return;
    }

    const action = actionEl.dataset.fileAction || "open";
    const path = actionEl.dataset.filePath || "";
    if (path) {
      postMessage("file.open", { path, action });
    }
  });

  contactsEl?.addEventListener("click", (event) => {
    const target = event.target.closest("[data-contact-id]");
    if (!target) {
      return;
    }

    const contactId = target.dataset.contactId;
    if (contactId) {
      setActiveContact(contactId);
    }
  });

  searchInput?.addEventListener("input", () => {
    renderContacts();
  });

  searchButton?.addEventListener("click", () => {
    searchInput?.focus();
  });

  messageSearchInput?.addEventListener("input", () => {
    state.messageQuery = messageSearchInput.value || "";
    renderMessages();
  });

  messageFilterSelect?.addEventListener("change", () => {
    state.messageFilter = messageFilterSelect.value || "all";
    renderMessages();
  });

  messageClearButton?.addEventListener("click", () => {
    if (messageSearchInput) {
      messageSearchInput.value = "";
    }
    if (messageFilterSelect) {
      messageFilterSelect.value = "all";
    }
    state.messageQuery = "";
    state.messageFilter = "all";
    renderMessages();
  });

  settingsOpenButton?.addEventListener("click", () => {
    openSettings();
  });

  settingsBackdrop?.addEventListener("click", () => {
    closeSettings();
  });

  settingsCloseButton?.addEventListener("click", () => {
    closeSettings();
  });

  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape" && settingsPanel && !settingsPanel.classList.contains("hidden")) {
      closeSettings();
    }
  });

  renameButton?.addEventListener("click", handleRename);
  renameInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      handleRename();
    }
  });
  displayNameButton?.addEventListener("click", handleDisplayNameUpdate);
  displayNameInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      handleDisplayNameUpdate();
    }
  });
  logoutButton?.addEventListener("click", handleLogout);

  settingsSaveButton?.addEventListener("click", handleSettingsSave);
  twoFactorToggle?.addEventListener("change", () => {
    syncTwoFactorMethod();
  });
  networkKeyButton?.addEventListener("click", handleNetworkKeySave);
  networkKeyInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      handleNetworkKeySave();
    }
  });
  manualPeerButton?.addEventListener("click", handleManualPeer);
  manualPeerInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter") {
      event.preventDefault();
      handleManualPeer();
    }
  });
  relaySaveButton?.addEventListener("click", handleRelaySave);
  diagRefreshButton?.addEventListener("click", () => {
    fetchDiagnostics();
  });
  logRefreshButton?.addEventListener("click", () => {
    fetchLogs();
  });
  logLimitSelect?.addEventListener("change", () => {
    fetchLogs();
  });
  logDownloadButton?.addEventListener("click", () => {
    handleLogDownload();
  });
  archiveExportJsonButton?.addEventListener("click", () => {
    handleArchiveExport("json");
  });
  archiveExportCsvButton?.addEventListener("click", () => {
    handleArchiveExport("csv");
  });
  archiveClearButton?.addEventListener("click", () => {
    handleArchiveClear();
  });

  sendButton?.addEventListener("click", handleSend);
  composeInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      handleSend();
    }
  });
  composeInput?.addEventListener("input", () => {
    const value = composeInput?.value.trim() ?? "";
    if (!value) {
      indicateTyping(false);
      return;
    }

    indicateTyping(true);
    scheduleTypingStop();
  });
  composeInput?.addEventListener("blur", () => {
    indicateTyping(false);
  });

  if (hasHost() && fileInput) {
    fileInput.disabled = true;
  }

  filePick?.addEventListener("click", (event) => {
    if (!hasHost()) {
      return;
    }

    event.preventDefault();
    handlePickFile();
  });

  fileInput?.addEventListener("change", (event) => {
    const target = event.target;
    const file = target?.files?.[0];
    if (file) {
      handleFileSelection(file);
    }
    if (fileInput) {
      fileInput.value = "";
    }
  });

  fileClear?.addEventListener("click", () => {
    clearFileChip();
    setChatStatus("");
  });

  dropZone?.addEventListener("dragenter", (event) => {
    if (state.view !== "chat" || !state.activeContactId) {
      return;
    }

    event.preventDefault();
    dragCounter += 1;
    showDropOverlay();
  });

  dropZone?.addEventListener("dragover", (event) => {
    if (state.view !== "chat" || !state.activeContactId) {
      return;
    }

    event.preventDefault();
  });

  dropZone?.addEventListener("dragleave", (event) => {
    if (state.view !== "chat" || !state.activeContactId) {
      return;
    }

    dragCounter -= 1;
    if (dragCounter <= 0) {
      hideDropOverlay();
    }
  });

  dropZone?.addEventListener("drop", (event) => {
    if (state.view !== "chat" || !state.activeContactId) {
      return;
    }

    event.preventDefault();
    const file = event.dataTransfer?.files?.[0];
    hideDropOverlay();
    if (hasHost()) {
      return;
    }
    if (file) {
      handleFileSelection(file);
    }
  });

  if (hasHost()) {
    window.chrome.webview.addEventListener("message", (event) => {
      const data = event.data;
      if (!data) {
        return;
      }

      if (data.type === "chat.push" && data.payload) {
        applySnapshot(data.payload);
        return;
      }

      if (data.type === "chat.status" && data.payload) {
        const message = data.payload.message || "";
        const tone = data.payload.tone || "info";
        setChatStatus(message, tone);
        return;
      }

      if (data.type === "ui.theme" && data.payload) {
        applyTheme(data.payload);
        return;
      }

      if (data.type === "ui.drop") {
        hideDropOverlay();
        return;
      }

      if (data.type === "ui.status" && data.payload) {
        const message = data.payload.message || "";
        const tone = data.payload.tone || "info";
        const autoClearMs = Number(data.payload.autoClearMs || 0);
        showUiStatus(message, tone, autoClearMs);
        return;
      }

      if (!data.id) {
        return;
      }

      const resolver = pending.get(data.id);
      if (resolver) {
        pending.delete(data.id);
        resolver(data);
      }
    });
  }

  setMode("login");
  setView("auth");
  setComposerEnabled(false);
  syncTwoFactorMethod();
  applyUiPreferences(loadSettings());
  if (window.matchMedia) {
    const media = window.matchMedia("(prefers-color-scheme: light)");
    media.addEventListener("change", () => {
      const settings = loadSettings();
      if (settings.theme === "Otomatik") {
        applyUiPreferences(settings);
      }
    });
  }
  if (hasHost()) {
    restoreSession();
  }
})();

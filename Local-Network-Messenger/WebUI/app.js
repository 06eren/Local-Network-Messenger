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
  const messagesEl = document.querySelector("[data-messages]");
  const composeInput = document.querySelector("[data-compose]");
  const sendButton = document.querySelector("[data-send]");
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
  };
  let dragCounter = 0;

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
  };

  function openSettings() {
    if (!settingsPanel) {
      return;
    }

    settingsPanel.classList.remove("hidden");
    setAccountStatus("");
    setSettingsStatus("");
    applySettings(loadSettings());
    renameInput?.focus();
  }

  function closeSettings() {
    if (!settingsPanel) {
      return;
    }

    settingsPanel.classList.add("hidden");
    setAccountStatus("");
    setSettingsStatus("");
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

  const MAX_FILE_BYTES = 10 * 1024 * 1024;

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
        const initials = initialsFromName(name) || "LN";
        const active = contact.id === state.activeContactId;
        const badge = contact.unreadCount > 0
          ? `<span class=\"contact-badge\">${contact.unreadCount}</span>`
          : "";
        const previewMarkup = preview
          ? `<div class=\"contact-preview\">${preview}</div>`
          : `<div class=\"contact-preview muted\">${contact.id === "all" ? "Genel sohbet" : "Henuz mesaj yok."}</div>`;
        const statusLabel = contact.id === "all"
          ? "Genel sohbet"
          : (time ? `Son gorulme: ${time}` : "");
        const statusMarkup = statusLabel
          ? `<div class=\"contact-status\">${escapeHtml(statusLabel)}</div>`
          : "";

        return `
          <div class="contact-card" data-contact-id="${contact.id}" data-active="${active}">
            <div class="contact-avatar">${initials}</div>
            <div class="contact-main">
              <div class="contact-name">${name}</div>
              ${previewMarkup}
              ${statusMarkup}
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
    if (messages.length === 0) {
      messagesEl.innerHTML = "<div class=\"empty-state\">Henuz mesaj yok.</div>";
      return;
    }

    const rows = messages
      .map((message) => {
        const mineClass = message.isMine ? "message-row mine" : "message-row";
        const text = escapeHtml(message.text);
        const sender = escapeHtml(message.sender);
        const time = formatTime(message.sentAt);
        const attachment = message.attachment
          ? `<div class=\"attachment\">
              <span>${escapeHtml(message.attachment.fileName)}</span>
              <span class=\"text-[10px]\">${formatSize(message.attachment.sizeBytes)}</span>
              <span class=\"text-[10px]\">${escapeHtml(message.attachment.status)}</span>
            </div>`
          : "";

        return `
          <div class="${mineClass}">
            <div class="message-bubble">
              <div class="message-sender">${sender}</div>
              <div class="message-text">${text}</div>
              ${attachment}
              <div class="message-foot">
                <span>${time}</span>
              </div>
            </div>
          </div>`;
      })
      .join("");

    messagesEl.innerHTML = `<div class="message-stack">${rows}</div>`;
    messagesEl.scrollTop = messagesEl.scrollHeight;
  };

  const setActiveContact = (contactId) => {
    state.activeContactId = contactId;
    const active = state.contacts.find((contact) => contact.id === contactId);
    if (chatTitleEl) {
      chatTitleEl.textContent = active ? active.displayName : "Secili kisi yok";
    }
    if (chatStatusText) {
      if (!active) {
        chatStatusText.textContent = "Bir sohbet sec.";
      } else if (active.id === "all") {
        chatStatusText.textContent = "Yerel agdaki herkese acik sohbet";
      } else if (active.status) {
        chatStatusText.textContent = `Son gorulme: ${active.status}`;
      } else {
        chatStatusText.textContent = "Durum bilgisi yok.";
      }
    }
    if (chatAvatarEl) {
      const initials = active ? initialsFromName(active.displayName) : "LN";
      chatAvatarEl.textContent = initials || "LN";
    }

    setComposerEnabled(Boolean(active));
    renderContacts();
    renderMessages();
    postMessageRaw("chat.active", { threadId: contactId });
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
      const count = state.contacts.filter((contact) => contact.id !== "all").length;
      networkCountEl.textContent = `${count} kisi`;
    }

    setActiveContact(state.activeContactId);
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

    const response = await postMessage("chat.send", {
      threadId: state.activeContactId,
      text,
    });

    if (!response.ok) {
      setChatStatus(response.payload?.message || "Mesaj gonderilemedi.", "error");
      return;
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

    if (composeInput) {
      composeInput.value = "";
    }

    renderMessages();
    renderContacts();
    setChatStatus("");
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
      setChatStatus("Dosya boyutu 10 MB uzerinde. Daha kucuk bir dosya sec.", "error");
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

  sendButton?.addEventListener("click", handleSend);
  composeInput?.addEventListener("keydown", (event) => {
    if (event.key === "Enter" && !event.shiftKey) {
      event.preventDefault();
      handleSend();
    }
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
  if (hasHost()) {
    restoreSession();
  }
})();

(() => {
  "use strict";

  const api = window.exchangeInstaller;
  const PROFILE_DEFAULTS = Object.freeze({
    mediaPath: "",
    organizationName: "Exchange Organization",
    role: "Mailbox",
    installPath: "C:\\Program Files\\Microsoft\\Exchange Server\\V15",
    databaseName: "Mailbox Database 01",
    databasePath: "C:\\ExchangeDatabases\\Mailbox Database 01\\Mailbox Database 01.edb",
    logPath: "C:\\ExchangeDatabases\\Mailbox Database 01\\Logs",
    installPrerequisites: true,
    prepareSchema: true,
    prepareActiveDirectory: true,
    prepareDomains: true,
    disableTelemetry: true,
    resumeAfterRestart: true,
  });

  const CHECK_DEFINITIONS = Object.freeze([
    { key: "elevation", aliases: ["administrator", "admin", "isElevated"] },
    { key: "media", aliases: ["setupMedia", "exchangeMedia", "setup"] },
    { key: "digest", aliases: ["mediaDigest", "hash", "signature"] },
    { key: "prerequisites", aliases: ["prerequisite", "windowsFeatures", "features"] },
    { key: "activeDirectory", aliases: ["ad", "directory", "directoryState"] },
    { key: "restart", aliases: ["pendingRestart", "reboot"] },
  ]);

  const FAILURE_STATUSES = new Set(["failed", "error", "blocked", "interrupted", "uncertain"]);
  const ACTIVE_STATUSES = new Set(["running", "installing", "preparing", "cancelling", "cancel-pending"]);
  const COMPLETE_STATUSES = new Set(["complete", "completed", "passed", "success", "succeeded", "skipped"]);

  const elements = {
    appStatus: document.querySelector("#app-status"),
    appStatusLabel: document.querySelector("#app-status-label"),
    heroStateLabel: document.querySelector("#hero-state-label"),
    heroProgressLabel: document.querySelector("#hero-progress-label"),
    overallProgress: document.querySelector("#overall-progress"),
    overallProgressBar: document.querySelector("#overall-progress-bar"),
    noticeStack: document.querySelector("#notice-stack"),
    actionRecovery: document.querySelector("#action-recovery"),
    actionRecoveryMessage: document.querySelector("#action-recovery-message"),
    retryLastAction: document.querySelector("#retry-last-action"),
    profileForm: document.querySelector("#profile-form"),
    profileSaveState: document.querySelector("#profile-save-state"),
    mediaPath: document.querySelector("#media-path"),
    mediaValidation: document.querySelector("#media-validation"),
    chooseMedia: document.querySelector("#choose-media"),
    inspectMedia: document.querySelector("#inspect-media"),
    cheapLfsVerify: document.querySelector("#cheap-lfs-verify"),
    cheapLfsDownload: document.querySelector("#cheap-lfs-download"),
    cheapLfsStatus: document.querySelector("#cheap-lfs-status"),
    mailboxSettings: document.querySelector("#mailbox-settings"),
    runPreflight: document.querySelector("#run-preflight"),
    preflightChecks: document.querySelector("#preflight-checks"),
    planStatus: document.querySelector("#plan-status"),
    planList: document.querySelector("#plan-list"),
    acceptLicense: document.querySelector("#accept-license"),
    installationSummary: document.querySelector("#installation-summary"),
    stageList: document.querySelector("#stage-list"),
    startInstall: document.querySelector("#start-install"),
    resumeInstall: document.querySelector("#resume-install"),
    requestCancel: document.querySelector("#request-cancel"),
    safeCancelNote: document.querySelector("#safe-cancel-note"),
    stageRecovery: document.querySelector("#stage-recovery"),
    stageRecoveryMessage: document.querySelector("#stage-recovery-message"),
    retryStage: document.querySelector("#retry-stage"),
    resumeFromRecovery: document.querySelector("#resume-from-recovery"),
    uncertainReconciliation: document.querySelector("#uncertain-reconciliation"),
    uncertainEvidence: document.querySelector("#uncertain-evidence"),
    recordReconciliation: document.querySelector("#record-reconciliation"),
    logView: document.querySelector("#log-view"),
    revealLogs: document.querySelector("#reveal-logs"),
    exportLogs: document.querySelector("#export-logs"),
    openCodeRefresh: document.querySelector("#opencode-refresh"),
    openCodeInstall: document.querySelector("#opencode-install"),
    openCodeStatus: document.querySelector("#opencode-status"),
    openCodeProgress: document.querySelector("#opencode-progress"),
    yoloMode: document.querySelector("#yolo-mode"),
    yoloConfirmationField: document.querySelector("#yolo-confirmation-field"),
    yoloConfirmation: document.querySelector("#yolo-confirmation"),
    repairRun: document.querySelector("#repair-run"),
    repairApply: document.querySelector("#repair-apply"),
    repairStop: document.querySelector("#repair-stop"),
    repairOutput: document.querySelector("#repair-output"),
  };

  const model = {
    state: null,
    busy: new Set(),
    lastRetryableAction: null,
    failedStageId: null,
    saveTimer: null,
    unsubscribe: null,
    profileWasHydrated: false,
    lastAnnouncedInstallStatus: null,
    repairPlan: null,
    uncertainStage: null,
    renderQueued: false,
  };

  function asObject(value) {
    return value && typeof value === "object" && !Array.isArray(value) ? value : {};
  }

  function firstDefined(...values) {
    return values.find((value) => value !== undefined && value !== null);
  }

  function displayText(value, fallback = "") {
    if (typeof value === "string" && value.trim()) return value.trim();
    if (typeof value === "number" || typeof value === "boolean") return String(value);
    return fallback;
  }

  function errorMessage(error) {
    if (error instanceof Error && error.message) return error.message;
    if (typeof error === "string" && error.trim()) return error.trim();
    return "The installer service did not provide more detail.";
  }

  function normalizeStatus(value) {
    if (value === true) return "passed";
    if (value === false) return "failed";
    const normalized = displayText(value, "pending").toLowerCase().replace(/[\s_]+/g, "-");
    if (["ok", "ready", "valid", "verified"].includes(normalized)) return "passed";
    if (["done", "finished"].includes(normalized)) return "complete";
    if (["warn", "needs-attention"].includes(normalized)) return "warning";
    if (["not-started", "unknown", "idle"].includes(normalized)) return "pending";
    return normalized;
  }

  function statusLabel(status) {
    const labels = {
      pending: "Pending",
      passed: "Passed",
      complete: "Complete",
      completed: "Complete",
      running: "Running",
      installing: "Installing",
      preparing: "Preparing",
      warning: "Needs attention",
      failed: "Failed",
      error: "Error",
      blocked: "Blocked",
      interrupted: "Interrupted",
      cancelled: "Cancelled",
      "cancel-pending": "Stopping at a safe boundary",
      cancelling: "Stopping at a safe boundary",
      skipped: "Skipped",
      success: "Complete",
      succeeded: "Complete",
    };
    return labels[status] || status.replace(/-/g, " ").replace(/^./, (letter) => letter.toUpperCase());
  }

  function redactSensitiveText(value) {
    let text = displayText(value);
    const replacements = [
      [/(\b(?:password|passwd|secret|token|authorization|credential|client_secret)\b\s*[:=]\s*)([^\s,;]+)/gi, "$1[REDACTED]"],
      [/\bBearer\s+[^\s,;]+/gi, "Bearer [REDACTED]"],
      [/(?:https?|ftp):\/\/([^\s/@:]+):([^\s/@]+)@/gi, (match) => match.replace(/\/\/.*@/, "//[REDACTED]@")],
      [/(\b(?:-Password|-Credential|-Token)\s+)(?:"[^"]*"|'[^']*'|\S+)/gi, "$1[REDACTED]"],
    ];
    for (const [pattern, replacement] of replacements) text = text.replace(pattern, replacement);
    return text;
  }

  function setAppStatus(kind, label) {
    elements.appStatus.classList.toggle("ready", kind === "ready");
    elements.appStatus.classList.toggle("error", kind === "error");
    elements.appStatusLabel.textContent = label;
  }

  function showNotice(message, kind = "info", persistent = false) {
    const initiator = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    const notice = document.createElement("div");
    notice.className = `notice ${kind}`;
    notice.setAttribute("role", kind === "error" ? "alert" : "status");

    const copy = document.createElement("span");
    copy.textContent = redactSensitiveText(message);
    notice.append(copy);

    const dismiss = document.createElement("button");
    dismiss.type = "button";
    dismiss.setAttribute("aria-label", "Dismiss notification");
    dismiss.textContent = "×";
    let timeoutId = null;
    let remaining = 5500;
    let startedAt = 0;
    const remove = () => {
      window.clearTimeout(timeoutId);
      notice.remove();
      if (initiator?.isConnected) initiator.focus();
    };
    const resume = () => {
      if (persistent || kind === "error" || timeoutId || !notice.isConnected) return;
      startedAt = Date.now();
      timeoutId = window.setTimeout(remove, remaining);
    };
    const pause = () => {
      if (!timeoutId) return;
      remaining = Math.max(250, remaining - (Date.now() - startedAt));
      window.clearTimeout(timeoutId);
      timeoutId = null;
    };
    dismiss.addEventListener("click", remove);
    notice.addEventListener("mouseenter", pause);
    notice.addEventListener("mouseleave", resume);
    notice.addEventListener("focusin", pause);
    notice.addEventListener("focusout", resume);
    notice.append(dismiss);
    elements.noticeStack.prepend(notice);

    if (!persistent && kind !== "error") {
      resume();
    }
  }

  function setControlAvailability(control, available, reason = "") {
    if (!control) return;
    control.disabled = false;
    control.setAttribute("aria-disabled", String(!available));
    if (!available && reason) control.setAttribute("data-disabled-reason", reason);
    else control.removeAttribute("data-disabled-reason");
  }

  function showActionRecovery(label, error, retry) {
    const safeMessage = redactSensitiveText(errorMessage(error));
    model.lastRetryableAction = { label, retry };
    elements.actionRecoveryMessage.textContent = `${label} stopped: ${safeMessage}`;
    elements.retryLastAction.textContent = `Retry: ${label}`;
    elements.actionRecovery.classList.remove("hidden");
  }

  function clearActionRecovery() {
    model.lastRetryableAction = null;
    elements.actionRecovery.classList.add("hidden");
  }

  function setBusy(key, isBusy) {
    if (isBusy) model.busy.add(key);
    else model.busy.delete(key);
    renderControls();
  }

  async function refreshState() {
    const nextState = await api.getState();
    model.state = asObject(firstDefined(nextState?.state, nextState));
    scheduleRender();
    return model.state;
  }

  async function runAction(key, label, operation, successMessage = "") {
    if (model.busy.has(key)) return undefined;
    setBusy(key, true);
    const retry = () => runAction(key, label, operation, successMessage);
    try {
      const result = await operation();
      await refreshState();
      refreshOpenCodeStatus();
      clearActionRecovery();
      if (successMessage) showNotice(successMessage, "success");
      return result;
    } catch (error) {
      showActionRecovery(label, error, retry);
      showNotice(`${label} stopped. ${errorMessage(error)}`, "error", true);
      return undefined;
    } finally {
      setBusy(key, false);
    }
  }

  function collectProfile() {
    const profile = {};
    for (const control of elements.profileForm.querySelectorAll("[data-profile]")) {
      profile[control.dataset.profile] = control.type === "checkbox" ? control.checked : control.value.trim();
    }
    return profile;
  }

  function updateRoleVisibility() {
    const roleControl = elements.profileForm.querySelector('[data-profile="role"]');
    const showsMailboxSettings = roleControl?.value === "Mailbox";
    elements.mailboxSettings.classList.toggle("hidden", !showsMailboxSettings);
    for (const input of elements.mailboxSettings.querySelectorAll("input")) input.disabled = !showsMailboxSettings;
  }

  function hydrateProfile(profileValue) {
    const profile = { ...PROFILE_DEFAULTS, ...asObject(profileValue) };
    for (const control of elements.profileForm.querySelectorAll("[data-profile]")) {
      if (document.activeElement === control && model.profileWasHydrated) continue;
      const value = profile[control.dataset.profile];
      if (control.type === "checkbox") control.checked = Boolean(value);
      else if (value !== undefined && value !== null) control.value = String(value);
    }
    model.profileWasHydrated = true;
    updateRoleVisibility();
  }

  async function persistProfile(showFailure = true) {
    if (!api || model.busy.has("profile")) return false;
    setBusy("profile", true);
    elements.profileSaveState.textContent = "Saving…";
    try {
      await api.updateProfile(collectProfile());
      elements.profileSaveState.textContent = "Saved locally";
      clearActionRecovery();
      return true;
    } catch (error) {
      elements.profileSaveState.textContent = "Not saved — retry available";
      if (showFailure) {
        showActionRecovery("Save profile", error, () => persistProfile(true));
        showNotice(`Profile changes were not saved. ${errorMessage(error)}`, "error", true);
      }
      return false;
    } finally {
      setBusy("profile", false);
    }
  }

  function scheduleProfileSave() {
    window.clearTimeout(model.saveTimer);
    elements.profileSaveState.textContent = "Unsaved changes";
    model.saveTimer = window.setTimeout(() => persistProfile(true), 450);
  }

  async function refreshOpenCodeStatus() {
    if (typeof api.getOpenCodeStatus !== "function") {
      elements.openCodeStatus.textContent = "OpenCode integration is unavailable in this build.";
      elements.openCodeStatus.className = "status-chip failed";
      return;
    }
    await runAction("opencode-status", "Check OpenCode", async () => {
      const status = asObject(await api.getOpenCodeStatus());
      elements.openCodeStatus.textContent = redactSensitiveText(firstDefined(status.message, status.status, "OpenCode status returned."));
      elements.openCodeStatus.className = `status-chip ${status.ok || status.compatible ? "passed" : "warning"}`;
      if (typeof status.yoloMode === "boolean") elements.yoloMode.checked = status.yoloMode;
      return status;
    });
  }

  async function setYoloMode(enabled) {
    const acknowledgement = enabled ? elements.yoloConfirmation.value.trim() : "";
    await runAction("yolo-mode", enabled ? "Enable bounded YOLO mode" : "Disable YOLO mode", async () => {
      const result = await api.setYoloMode({ enabled, acknowledgement });
      elements.yoloConfirmation.value = "";
      elements.yoloConfirmationField.classList.add("hidden");
      elements.yoloMode.checked = Boolean(result?.enabled ?? enabled);
      elements.yoloMode.focus();
      showNotice(`YOLO mode is ${elements.yoloMode.checked ? "enabled for the bounded Exchange repair catalog" : "off"}.`, elements.yoloMode.checked ? "warning" : "success", elements.yoloMode.checked);
      return result;
    });
  }

  function renderRepairOutput(result) {
    elements.repairOutput.replaceChildren();
    const pre = document.createElement("pre");
    pre.textContent = redactSensitiveText(typeof result === "string" ? result : JSON.stringify(result, null, 2));
    elements.repairOutput.append(pre);
    model.repairPlan = result && result.planId && Array.isArray(result.actionIds) ? { planId: result.planId, actionIds: result.actionIds } : null;
    elements.repairApply.disabled = !model.repairPlan || Boolean(result?.autoApprove);
  }

  function validateMediaPath() {
    const path = elements.mediaPath.value.trim();
    elements.mediaValidation.textContent = path ? "" : "Choose the Exchange media folder or Setup.exe first.";
    return path;
  }

  function preflightCollection() {
    const preflight = asObject(model.state?.preflight);
    return firstDefined(preflight.checks, model.state?.checks, preflight.results, {});
  }

  function lookupCheck(definition) {
    const collection = preflightCollection();
    const names = [definition.key, ...definition.aliases].map((name) => name.toLowerCase());
    if (Array.isArray(collection)) {
      return collection.find((item) => {
        const itemObject = asObject(item);
        const key = displayText(firstDefined(itemObject.key, itemObject.id, itemObject.name)).toLowerCase();
        return names.includes(key);
      });
    }
    const object = asObject(collection);
    const matchingKey = Object.keys(object).find((key) => names.includes(key.toLowerCase()));
    return matchingKey ? object[matchingKey] : undefined;
  }

  function unpackCheck(rawCheck) {
    if (typeof rawCheck === "boolean" || typeof rawCheck === "string") {
      return { status: normalizeStatus(rawCheck), message: "" };
    }
    const check = asObject(rawCheck);
    return {
      status: normalizeStatus(firstDefined(check.status, check.state, check.result, check.passed)),
      message: redactSensitiveText(firstDefined(check.message, check.detail, check.reason, check.summary, "")),
    };
  }

  function canInstallFromPreflight() {
    const preflight = asObject(model.state?.preflight);
    const authoritative = firstDefined(preflight.canInstall, preflight.ready, model.state?.canInstall);
    if (typeof authoritative === "boolean") return authoritative;
    const checks = CHECK_DEFINITIONS.map((definition) => unpackCheck(lookupCheck(definition)));
    return checks.length > 0 && checks.every((check) => check.status === "passed" || check.status === "complete");
  }

  function renderPreflight() {
    for (const definition of CHECK_DEFINITIONS) {
      const card = elements.preflightChecks.querySelector(`[data-check="${definition.key}"]`);
      if (!card) continue;
      const check = unpackCheck(lookupCheck(definition));
      card.className = `check-card ${check.status}`;
      const icon = card.querySelector(".check-icon");
      const detail = card.querySelector("p");
      icon.textContent = check.status === "passed" || check.status === "complete" ? "✓" : FAILURE_STATUSES.has(check.status) ? "!" : check.status === "warning" ? "!" : "—";
      detail.textContent = check.message || statusLabel(check.status);
      card.setAttribute("aria-label", `${card.querySelector("h3").textContent}: ${detail.textContent}`);
    }
  }

  function planItems() {
    const preflight = asObject(model.state?.preflight);
    const rawPlan = firstDefined(model.state?.plan, preflight.plan, model.state?.installPlan, []);
    if (Array.isArray(rawPlan)) return rawPlan;
    const plan = asObject(rawPlan);
    return Array.isArray(plan.stages) ? plan.stages : Array.isArray(plan.steps) ? plan.steps : Object.keys(plan).length ? Object.entries(plan).map(([title, detail]) => ({ title, detail })) : [];
  }

  function renderPlan() {
    const items = planItems();
    elements.planList.replaceChildren();
    if (!items.length) {
      const empty = document.createElement("li");
      empty.className = "empty-row";
      empty.textContent = "Run preflight to generate the exact staged plan.";
      elements.planList.append(empty);
      elements.planStatus.className = "status-chip pending";
      elements.planStatus.textContent = "Waiting for preflight";
      return;
    }

    for (const itemValue of items) {
      const item = typeof itemValue === "string" ? { title: itemValue } : asObject(itemValue);
      const row = document.createElement("li");
      const body = document.createElement("div");
      const title = document.createElement("span");
      title.className = "plan-item-title";
      title.textContent = redactSensitiveText(firstDefined(item.title, item.name, item.label, item.stage, "Installation step"));
      body.append(title);

      const rawDetail = firstDefined(item.detail, item.commandPreview, item.arguments, item.description, item.path);
      if (rawDetail !== undefined && rawDetail !== null && rawDetail !== "") {
        const detail = document.createElement("span");
        detail.className = "plan-item-detail";
        detail.textContent = redactSensitiveText(Array.isArray(rawDetail) ? rawDetail.join(" ") : String(rawDetail));
        body.append(detail);
      }
      row.append(body);
      elements.planList.append(row);
    }
    elements.planStatus.className = `status-chip ${canInstallFromPreflight() ? "passed" : "warning"}`;
    elements.planStatus.textContent = canInstallFromPreflight() ? "Plan ready" : "Plan needs attention";
  }

  function installState() {
    return asObject(firstDefined(model.state?.install, model.state?.installation, model.state?.run));
  }

  function stageItems() {
    const installation = installState();
    const rawStages = firstDefined(installation.stages, model.state?.stages, model.state?.plan?.stages, []);
    return Array.isArray(rawStages) ? rawStages : [];
  }

  function installStatus() {
    const installation = installState();
    return normalizeStatus(firstDefined(installation.status, installation.state, model.state?.status, "pending"));
  }

  function calculateProgress(stages) {
    const installation = installState();
    const direct = Number(firstDefined(installation.progressPercent, installation.progress, model.state?.progressPercent));
    if (Number.isFinite(direct)) return Math.max(0, Math.min(100, Math.round(direct <= 1 && direct > 0 ? direct * 100 : direct)));
    if (!stages.length) return 0;
    const completed = stages.filter((stage) => COMPLETE_STATUSES.has(normalizeStatus(firstDefined(stage?.status, stage?.state)))).length;
    return Math.round((completed / stages.length) * 100);
  }

  function renderStages() {
    const stages = stageItems();
    const installation = installState();
    const status = installStatus();
    const progress = calculateProgress(stages);
    model.failedStageId = null;

    elements.heroStateLabel.textContent = statusLabel(status);
    elements.heroProgressLabel.textContent = `${progress}%`;
    elements.overallProgress.setAttribute("aria-valuenow", String(progress));
    elements.overallProgressBar.style.width = `${progress}%`;
    elements.installationSummary.textContent = redactSensitiveText(firstDefined(installation.message, installation.summary, `${statusLabel(status)} · ${progress}% complete`));
    elements.stageList.replaceChildren();

    if (!stages.length) {
      const empty = document.createElement("li");
      empty.className = "empty-row";
      empty.textContent = "Stages will appear after preflight.";
      elements.stageList.append(empty);
    } else {
      stages.forEach((stageValue, index) => {
        const stage = asObject(stageValue);
        const stageStatus = normalizeStatus(firstDefined(stage.status, stage.state, "pending"));
        const stageId = displayText(firstDefined(stage.id, stage.stageId, stage.key), String(index));
        if (!model.failedStageId && FAILURE_STATUSES.has(stageStatus)) model.failedStageId = stageId;

        const row = document.createElement("li");
        row.className = stageStatus;
        row.dataset.stageId = stageId;

        const marker = document.createElement("span");
        marker.className = "stage-marker";
        marker.setAttribute("aria-hidden", "true");
        marker.textContent = COMPLETE_STATUSES.has(stageStatus) ? "✓" : FAILURE_STATUSES.has(stageStatus) ? "!" : String(index + 1);

        const copy = document.createElement("div");
        copy.className = "stage-copy";
        const title = document.createElement("strong");
        title.textContent = redactSensitiveText(firstDefined(stage.title, stage.name, stage.label, `Stage ${index + 1}`));
        const detail = document.createElement("small");
        detail.textContent = redactSensitiveText(firstDefined(stage.message, stage.detail, stage.lastError, statusLabel(stageStatus)));
        copy.append(title, detail);

        const meta = document.createElement("span");
        meta.className = "stage-meta";
        const attempt = Number(firstDefined(stage.attempt, stage.attemptNumber));
        const limit = Number(firstDefined(stage.maxAttempts, stage.retryLimit));
        const attemptText = Number.isFinite(attempt) ? ` · attempt ${attempt}${Number.isFinite(limit) ? ` of ${limit}` : ""}` : "";
        meta.textContent = `${statusLabel(stageStatus)}${attemptText}`;

        row.setAttribute("aria-label", `${title.textContent}: ${meta.textContent}. ${detail.textContent}`);
        row.append(marker, copy, meta);
        elements.stageList.append(row);
      });
    }

    const failedStage = stages.map(asObject).find((stage, index) => {
      const id = displayText(firstDefined(stage.id, stage.stageId, stage.key), String(index));
      return id === model.failedStageId;
    });
    const hasRecoverableFailure = Boolean(model.failedStageId) || FAILURE_STATUSES.has(status);
    elements.stageRecovery.classList.toggle("hidden", !hasRecoverableFailure);
    if (hasRecoverableFailure) {
      elements.stageRecoveryMessage.textContent = redactSensitiveText(firstDefined(failedStage?.lastError, failedStage?.message, installation.lastError, installation.error, "Use retry for the failed stage, or resume from the last durable stage record."));
    }

    const uncertain = stages.map(asObject).find((stage) => normalizeStatus(stage.status) === "uncertain");
    model.uncertainStage = uncertain || null;
    elements.uncertainReconciliation.classList.toggle("hidden", !uncertain);
    if (uncertain) {
      elements.uncertainEvidence.textContent = redactSensitiveText(firstDefined(uncertain.indeterminateEvidence?.warning, uncertain.reconciliation, uncertain.lastError, "The prior privileged process may still be active or may have applied changes. A second launch is blocked."));
      elements.stageRecoveryMessage.textContent = "An indeterminate stage cannot be retried or resumed. Review external Exchange process and setup evidence, then record one explicit outcome below.";
    }

    if (model.lastAnnouncedInstallStatus !== status && (FAILURE_STATUSES.has(status) || COMPLETE_STATUSES.has(status))) {
      model.lastAnnouncedInstallStatus = status;
    }
  }

  function renderOpenCodeProgress() {
    const progress = asObject(model.state?.openCodeProgress);
    elements.openCodeProgress.textContent = progress.message ? redactSensitiveText(`${statusLabel(normalizeStatus(progress.phase))}: ${progress.message}`) : "No OpenCode operation is running.";
  }

  function logItems() {
    const raw = firstDefined(model.state?.logs, model.state?.events, installState().logs, []);
    return Array.isArray(raw) ? raw : [];
  }

  function renderLogs() {
    const logs = logItems().slice(-250);
    elements.logView.replaceChildren();
    if (!logs.length) {
      const empty = document.createElement("p");
      empty.className = "empty-row";
      empty.textContent = "No installer events have been recorded.";
      elements.logView.append(empty);
      return;
    }

    const fragment = document.createDocumentFragment();
    for (const logValue of logs) {
      const log = typeof logValue === "string" ? { message: logValue } : asObject(logValue);
      const line = document.createElement("p");
      line.className = "log-line";

      const time = document.createElement("time");
      const rawTime = firstDefined(log.timestamp, log.time, log.createdAt, "");
      time.textContent = displayText(rawTime, "—");
      if (rawTime) time.dateTime = String(rawTime);

      const level = document.createElement("span");
      level.className = "log-level";
      level.textContent = displayText(firstDefined(log.level, log.severity), "INFO").toUpperCase();

      const message = document.createElement("span");
      message.textContent = redactSensitiveText(firstDefined(log.message, log.detail, log.text, JSON.stringify(log)));
      line.append(time, level, message);
      fragment.append(line);
    }
    elements.logView.append(fragment);
    elements.logView.scrollTop = elements.logView.scrollHeight;
  }

  function renderControls() {
    if (!api) return;
    const installation = installState();
    const status = installStatus();
    const isActive = ACTIVE_STATUSES.has(status);
    const canResume = Boolean(firstDefined(installation.canResume, model.state?.canResume, ["interrupted", "cancelled", "blocked"].includes(status)));
    const cancelRequested = Boolean(firstDefined(installation.cancelRequested, status === "cancelling" || status === "cancel-pending"));
    const planReady = planItems().length > 0 && canInstallFromPreflight();

    const uncertain = Boolean(model.uncertainStage || model.state?.requiresReconciliation);
    setControlAvailability(elements.chooseMedia, !model.busy.has("media"), "Media selection is already in progress.");
    setControlAvailability(elements.inspectMedia, !model.busy.has("media") && Boolean(elements.mediaPath.value.trim()), model.busy.has("media") ? "Media inspection is already in progress." : "Choose a local media path first.");
    setControlAvailability(elements.runPreflight, !model.busy.has("preflight") && !isActive, isActive ? "Wait for the active installation stage to stop." : "Preflight is already running.");
    setControlAvailability(elements.startInstall, !model.busy.has("install") && !isActive && !canResume && !uncertain && planReady && elements.acceptLicense.checked, uncertain ? "Reconcile the indeterminate stage before starting another privileged process." : !elements.acceptLicense.checked ? "Review the plan and select the license acknowledgement." : !planReady ? "Run preflight and resolve every failed check first." : "Installation is already active.");
    elements.resumeInstall.classList.toggle("hidden", !canResume || isActive);
    setControlAvailability(elements.resumeInstall, !model.busy.has("install") && !uncertain, uncertain ? "Reconcile the indeterminate stage before resuming." : "Resume is already in progress.");
    elements.requestCancel.classList.toggle("hidden", !isActive);
    setControlAvailability(elements.requestCancel, !model.busy.has("cancel") && !cancelRequested && installation.canRequestCancel !== false, cancelRequested ? "Cancellation is already waiting for a safe boundary." : "Cancellation is unavailable for the current stage.");
    elements.safeCancelNote.classList.toggle("hidden", !cancelRequested);
    setControlAvailability(elements.retryStage, !model.busy.has("install") && Boolean(model.failedStageId) && !uncertain, uncertain ? "Record the reviewed indeterminate outcome before retrying." : "No conclusively failed stage is available.");
    setControlAvailability(elements.resumeFromRecovery, !model.busy.has("install") && hasDurableState() && !uncertain, uncertain ? "Record the reviewed indeterminate outcome before resuming." : "No durable recovery state is available.");
    setControlAvailability(elements.exportLogs, !model.busy.has("logs") && logItems().length > 0, "No redacted log entries are available to export.");
    setControlAvailability(elements.revealLogs, !model.busy.has("logs"), "The log-folder action is already running.");
    setControlAvailability(elements.retryLastAction, model.busy.size === 0 && Boolean(model.lastRetryableAction), "No failed action is available to retry.");
    const outcome = document.querySelector('input[name="reconciliation-outcome"]:checked');
    setControlAvailability(elements.recordReconciliation, Boolean(uncertain && outcome), "Select one reviewed outcome first.");
  }

  function hasDurableState() {
    const installation = installState();
    return Boolean(firstDefined(installation.durableStateAvailable, installation.checkpointAvailable, installation.canResume, stageItems().length > 0));
  }

  function render() {
    if (!model.state) return;
    const profile = firstDefined(model.state.profile, model.state.configuration, PROFILE_DEFAULTS);
    hydrateProfile(profile);
    renderPreflight();
    renderPlan();
    renderStages();
    renderLogs();
    renderOpenCodeProgress();
    renderControls();
    setAppStatus("ready", displayText(firstDefined(model.state.serviceStatusLabel, model.state.serviceStatus), "Installer service ready"));
    elements.profileSaveState.textContent = "Saved locally";
  }

  function scheduleRender() {
    if (model.renderQueued) return;
    model.renderQueued = true;
    window.requestAnimationFrame(() => {
      model.renderQueued = false;
      render();
    });
  }

  function bindProfileEvents() {
    elements.profileForm.addEventListener("input", (event) => {
      if (!(event.target instanceof HTMLInputElement) && !(event.target instanceof HTMLSelectElement)) return;
      if (!event.target.matches("[data-profile]")) return;
      updateRoleVisibility();
      scheduleProfileSave();
      renderControls();
    });
    elements.profileForm.addEventListener("change", updateRoleVisibility);
  }

  function bindActions() {
    document.addEventListener("click", (event) => {
      const control = event.target.closest?.('[aria-disabled="true"]');
      if (!control) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      showNotice(control.dataset.disabledReason || "This action is not available yet.", "warning", true);
    }, true);
    document.addEventListener("keydown", (event) => {
      if (!["Enter", " "].includes(event.key)) return;
      const control = event.target.closest?.('[aria-disabled="true"]');
      if (!control) return;
      event.preventDefault();
      showNotice(control.dataset.disabledReason || "This action is not available yet.", "warning", true);
    }, true);
    elements.retryLastAction.addEventListener("click", () => model.lastRetryableAction?.retry());

    elements.chooseMedia.addEventListener("click", () => runAction("media", "Choose Exchange media", async () => {
      const result = await api.chooseMedia();
      const selectedPath = displayText(firstDefined(result?.mediaPath, result?.path, result));
      if (!selectedPath) return result;
      elements.mediaPath.value = selectedPath;
      await api.updateProfile({ ...collectProfile(), mediaPath: selectedPath });
      return api.inspectMedia(selectedPath);
    }, "Exchange media selected and inspected."));

    elements.inspectMedia.addEventListener("click", () => {
      const path = validateMediaPath();
      if (!path) {
        elements.mediaPath.focus();
        return;
      }
      runAction("media", "Inspect Exchange media", async () => {
        await api.updateProfile(collectProfile());
        return api.inspectMedia(path);
      }, "Exchange media inspection completed.");
    });

    elements.runPreflight.addEventListener("click", () => runAction("preflight", "Run preflight", async () => {
      const saved = await persistProfile(false);
      if (!saved) throw new Error("The profile could not be saved before preflight.");
      return api.runPreflight();
    }, "Preflight finished. Review each result and the exact plan."));

    elements.acceptLicense.addEventListener("change", renderControls);

    elements.startInstall.addEventListener("click", () => runAction("install", "Start installation", async () => {
      if (!elements.acceptLicense.checked) throw new Error("Review the plan and confirm the applicable license terms first.");
      return api.startInstall({ accepted: true });
    }));

    elements.resumeInstall.addEventListener("click", () => runAction("install", "Resume installation", () => api.resumeInstall()));
    elements.resumeFromRecovery.addEventListener("click", () => runAction("install", "Resume from durable state", () => api.resumeInstall()));

    elements.retryStage.addEventListener("click", () => {
      if (!model.failedStageId) return;
      runAction("install", "Retry failed stage", () => api.retryStage(model.failedStageId));
    });
    elements.cheapLfsVerify.addEventListener("click", () => runAction("cheap-lfs-verify", "Verify Cheap LFS media metadata", async () => {
      const result = await api.verifyCheapLfsMedia();
      elements.cheapLfsStatus.textContent = `Verified ${result.releaseAssetCount} release assets with zero downloaded ISO bytes. Whole ISO: ${result.checked.sizeInBytes} bytes, SHA-256 ${result.checked.sha256}.`;
      elements.cheapLfsDownload.setAttribute("aria-disabled", "false");
      elements.cheapLfsDownload.removeAttribute("data-disabled-reason");
      return result;
    }, "Cheap LFS metadata verified without downloading the ISO."));
    elements.cheapLfsDownload.addEventListener("click", () => runAction("cheap-lfs-download", "Download verified Exchange ISO", async () => {
      const result = await api.hydrateCheapLfsMedia();
      elements.mediaPath.value = result.path;
      elements.cheapLfsStatus.textContent = `Hydrated and verified ${result.sizeInBytes} bytes at ${result.path}. SHA-256 ${result.sha256}.`;
      return result;
    }, "Exchange ISO hydration completed and its pinned digest matched."));

    for (const option of document.querySelectorAll('input[name="reconciliation-outcome"]')) option.addEventListener("change", renderControls);
    elements.recordReconciliation.addEventListener("click", () => {
      const outcome = document.querySelector('input[name="reconciliation-outcome"]:checked')?.value;
      const stage = model.uncertainStage;
      if (!outcome || !stage?.id || !stage?.reconciliationToken) return;
      runAction("reconcile", "Record reviewed stage outcome", () => api.reconcileStage({ stageId: stage.id, token: stage.reconciliationToken, outcome }), "The reviewed stage outcome was recorded. Refresh preflight before continuing.");
    });

    elements.requestCancel.addEventListener("click", () => runAction("cancel", "Request safe cancellation", () => api.requestCancel(), "Cancellation requested. The installer will stop at the next safe boundary."));

    elements.exportLogs.addEventListener("click", () => runAction("logs", "Export redacted logs", async () => {
      const result = await api.exportLogs();
      const path = displayText(firstDefined(result?.path, result?.filePath, result));
      showNotice(path ? `Redacted logs exported to ${path}` : "Redacted logs exported.", "success");
      return result;
    }));

    elements.revealLogs.addEventListener("click", () => runAction("logs", "Open log folder", () => api.revealLogs()));

    elements.openCodeRefresh.addEventListener("click", refreshOpenCodeStatus);
    elements.openCodeInstall.addEventListener("click", () => runAction("opencode-install", "Install or repair OpenCode", async () => {
      const result = await api.installOrRepairOpenCode();
      await refreshOpenCodeStatus();
      return result;
    }, "OpenCode installation verification finished."));
    elements.yoloMode.addEventListener("change", () => {
      if (elements.yoloMode.checked) {
        elements.yoloMode.checked = false;
        elements.yoloConfirmationField.classList.remove("hidden");
        elements.yoloConfirmation.focus();
      } else {
        setYoloMode(false);
      }
    });
    elements.yoloConfirmation.addEventListener("change", () => {
      if (elements.yoloConfirmation.value.trim() === "ENABLE BOUNDED YOLO") setYoloMode(true);
    });
    elements.repairRun.addEventListener("click", () => runAction("opencode-repair", "Generate bounded repair advice", async () => {
      const failedStage = model.failedStageId || model.state?.lastError?.stageId || null;
      const result = await api.runRepairAdvisor({ failedStageId: failedStage });
      renderRepairOutput(result);
      return result;
    }));
    elements.repairApply.addEventListener("click", () => {
      if (!model.repairPlan) return;
      runAction("opencode-apply", "Apply reviewed bounded repair plan", async () => {
        const result = await api.applyRepairPlan(model.repairPlan);
        model.repairPlan = null;
        elements.repairApply.disabled = true;
        renderRepairOutput(result);
        return result;
      });
    });
    elements.repairStop.addEventListener("click", () => runAction("opencode-stop", "Emergency stop repair adviser", () => api.cancelRepairAdvisor(), "Repair adviser stop requested."));
  }

  function bindNavigation() {
    const links = [...document.querySelectorAll("[data-nav-target]")];
    const sections = links.map((link) => document.getElementById(link.dataset.navTarget)).filter(Boolean);
    if (!("IntersectionObserver" in window)) return;
    const observer = new IntersectionObserver((entries) => {
      const visible = entries.filter((entry) => entry.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
      if (!visible) return;
      for (const link of links) {
        if (link.dataset.navTarget === visible.target.id) link.setAttribute("aria-current", "location");
        else link.removeAttribute("aria-current");
      }
    }, { rootMargin: "-25% 0px -60%", threshold: [0.05, 0.5] });
    sections.forEach((section) => observer.observe(section));
  }

  function disableUnavailableSurface() {
    setAppStatus("error", "Installer service unavailable");
    for (const control of document.querySelectorAll("button, input, select")) control.disabled = true;
    elements.actionRecoveryMessage.textContent = "The secure preload bridge is unavailable. Close and reopen the application; no installation was started.";
    elements.retryLastAction.textContent = "Retry connection";
    elements.retryLastAction.disabled = true;
    elements.actionRecovery.classList.remove("hidden");
  }

  async function initialize() {
    bindProfileEvents();
    bindNavigation();
    updateRoleVisibility();

    if (!api || typeof api.getState !== "function") {
      disableUnavailableSurface();
      return;
    }

    bindActions();
    try {
      await refreshState();
      if (typeof api.subscribe === "function") {
        const unsubscribe = api.subscribe((payload) => {
          const nextState = firstDefined(payload?.state, payload);
          if (nextState && typeof nextState === "object") {
            model.state = asObject(nextState);
            scheduleRender();
          }
        });
        if (typeof unsubscribe === "function") model.unsubscribe = unsubscribe;
      }
    } catch (error) {
      setAppStatus("error", "Installer service connection stopped");
      showActionRecovery("Reconnect to installer service", error, () => runAction("connect", "Reconnect to installer service", refreshState));
      showNotice(`Could not load installer state. ${errorMessage(error)}`, "error", true);
    }
  }

  window.addEventListener("beforeunload", () => {
    window.clearTimeout(model.saveTimer);
    if (typeof model.unsubscribe === "function") model.unsubscribe();
  });

  initialize();
})();

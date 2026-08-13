"use client";

import {
  ChangeEvent,
  FormEvent,
  KeyboardEvent,
  useEffect,
  useMemo,
  useRef,
  useState,
} from "react";
import docs from "./generated-docs";
import inventory from "../content/completeness-inventory.json";

type Language = "en" | "zh-HK" | "bilingual";
type Dock = "left" | "right" | "top" | "bottom";
type TabId =
  | "home"
  | "recovery"
  | "download"
  | "docs"
  | "local-tools"
  | "settings"
  | "completeness";

type Preferences = {
  language: Language;
  funnyEnglish: number;
  funnyCantonese: number;
  showDialogEmoji: boolean;
  theme: "light" | "dark" | "contrast";
  accent: string;
  density: "comfortable" | "compact";
  dock: Dock;
  schoolMode: boolean;
  schoolModeName: string;
  displayName: string;
  narrator: boolean;
  narratorLanguage: Language;
  narratorRate: number;
  narratorPitch: number;
  englishVoice: string;
  cantoneseVoice: string;
  pinnedTabs: TabId[];
};

type Notice = {
  id: string;
  title: string;
  body: string;
  tone: "info" | "success" | "warning" | "error";
  createdAt: string;
};

type LocalTicket = {
  id: string;
  category: string;
  description: string;
  severity: string;
  status: string;
  createdAt: string;
};

type ToyLock = {
  target: TabId;
  digest: string;
  locked: boolean;
  unlockUntil: number | null;
};

const STORAGE_KEY = "windows-server-setupper-docs-preferences-v1";
const NOTICE_KEY = "windows-server-setupper-docs-notices-v1";
const TICKET_KEY = "windows-server-setupper-docs-tickets-v1";
const LOCK_KEY = "windows-server-setupper-docs-toy-locks-v1";
const VOCABULARY_KEY = "windows-server-setupper-docs-vocabulary-v1";
const LOGO_KEY = "windows-server-setupper-docs-logo-v1";

const defaults: Preferences = {
  language: "en",
  funnyEnglish: 2,
  funnyCantonese: 3,
  showDialogEmoji: true,
  theme: "light",
  accent: "#315da8",
  density: "comfortable",
  dock: "left",
  schoolMode: false,
  schoolModeName: "School mode",
  displayName: "Windows Server Setupper",
  narrator: false,
  narratorLanguage: "en",
  narratorRate: 1,
  narratorPitch: 1,
  englishVoice: "auto",
  cantoneseVoice: "auto",
  pinnedTabs: ["home", "download"],
};

const tabs: Array<{
  id: TabId;
  group: string;
  en: string;
  zh: string;
  icon: string;
}> = [
  { id: "home", group: "Product", en: "Overview", zh: "總覽", icon: "◆" },
  { id: "recovery", group: "Product", en: "Recovery", zh: "復原", icon: "↻" },
  { id: "download", group: "Product", en: "Download", zh: "下載", icon: "↓" },
  { id: "docs", group: "Explore", en: "Documentation", zh: "文件", icon: "≡" },
  { id: "local-tools", group: "Explore", en: "Local tools", zh: "本機工具", icon: "⌁" },
  { id: "settings", group: "Configure", en: "Settings", zh: "設定", icon: "⚙" },
  { id: "completeness", group: "Configure", en: "Completeness", zh: "完整度", icon: "✓" },
];

const release = {
  name: "Resilient Error Recovery — 2026.08.13",
  tag: "recovery-2026.08.13-50b75f17",
  commit: "50b75f1781923489d1ff84691139104fcb17b818",
  published: "2026-08-13T19:07:01Z",
  installer: "WindowsServerTools-Setup-50b75f1781923489d1ff84691139104fcb17b818.exe",
  installerUrl:
    "https://github.com/cafepromenade/Windows-Server-Setupper/releases/download/recovery-2026.08.13-50b75f17/WindowsServerTools-Setup-50b75f1781923489d1ff84691139104fcb17b818.exe",
  releaseUrl:
    "https://github.com/cafepromenade/Windows-Server-Setupper/releases/tag/recovery-2026.08.13-50b75f17",
  commitUrl:
    "https://github.com/cafepromenade/Windows-Server-Setupper/commit/50b75f1781923489d1ff84691139104fcb17b818",
  sha256: "53c030076d2ddef4955ee0c45cf1beabf066a0f64be25512026cc38af1b89839",
  bytes: 6_876_543,
};

const shippedLogo = "./brand/windows-server-setupper-logo.png";

const currentProjectFacts = [
  ["Primary application", ".NET Framework 4.7.2 WPF desktop application"],
  ["Supported target", "Windows Server; administrative rights only when an operation requires them"],
  ["Recovery format", "windows-server-tools-recovery-v3"],
  ["Previous verified installer", release.installer],
  ["Previous installer SHA-256", release.sha256],
  ["Signing", "Intentionally unsigned; no code-signing certificate is used"],
  ["Release checks", "Build and packaging completed; tests, lint, reviews, runtime UI checks, and screenshots were not run"],
];

function safeParse<T>(value: string | null, fallback: T): T {
  if (!value) return fallback;
  try {
    return JSON.parse(value) as T;
  } catch {
    return fallback;
  }
}

function localized(language: Language, en: string, zh: string) {
  if (language === "zh-HK") return zh;
  if (language === "bilingual") return `${en} · ${zh}`;
  return en;
}

function downloadText(filename: string, text: string, type = "text/plain;charset=utf-8") {
  const url = URL.createObjectURL(new Blob([text], { type }));
  const anchor = document.createElement("a");
  anchor.href = url;
  anchor.download = filename;
  document.body.append(anchor);
  anchor.click();
  anchor.remove();
  URL.revokeObjectURL(url);
}

async function digestText(value: string) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(value));
  return Array.from(new Uint8Array(digest), (byte) => byte.toString(16).padStart(2, "0")).join("");
}

function base32Bytes(value: string) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const clean = value.toUpperCase().replace(/[\s=-]/g, "");
  if (!clean || [...clean].some((character) => !alphabet.includes(character))) {
    throw new Error("Use a valid Base32 secret.");
  }
  let bits = "";
  for (const character of clean) bits += alphabet.indexOf(character).toString(2).padStart(5, "0");
  const output: number[] = [];
  for (let index = 0; index + 8 <= bits.length; index += 8) {
    output.push(Number.parseInt(bits.slice(index, index + 8), 2));
  }
  return new Uint8Array(output);
}

async function totp(secret: string, when = Date.now()) {
  const counter = Math.floor(when / 30_000);
  const message = new ArrayBuffer(8);
  const view = new DataView(message);
  view.setUint32(4, counter);
  const key = await crypto.subtle.importKey(
    "raw",
    base32Bytes(secret),
    { name: "HMAC", hash: "SHA-1" },
    false,
    ["sign"],
  );
  const signature = new Uint8Array(await crypto.subtle.sign("HMAC", key, message));
  const offset = signature[signature.length - 1] & 15;
  const binary =
    ((signature[offset] & 127) << 24) |
    ((signature[offset + 1] & 255) << 16) |
    ((signature[offset + 2] & 255) << 8) |
    (signature[offset + 3] & 255);
  return String(binary % 1_000_000).padStart(6, "0");
}

function regexResult(pattern: string, flags: string, sample: string) {
  if (!pattern) return "Enter a pattern to preview matches.";
  if (pattern.length > 160 || sample.length > 5_000) return "Pattern or sample exceeds the local safety limit.";
  try {
    const expression = new RegExp(pattern, flags.replace("g", ""));
    return expression.test(sample) ? "Match found." : "No match.";
  } catch (error) {
    return error instanceof Error ? error.message : "Invalid pattern.";
  }
}

function SearchField({
  id,
  label,
  value,
  onChange,
  placeholder = "Search",
}: {
  id: string;
  label: string;
  value: string;
  onChange: (value: string) => void;
  placeholder?: string;
}) {
  const [open, setOpen] = useState(false);
  const [regex, setRegex] = useState(false);
  const [flags, setFlags] = useState("i");
  const [sample, setSample] = useState("");
  const result = regexResult(value, flags, sample);

  return (
    <div className="search-field" data-search-id={id}>
      <label htmlFor={id}>{label}</label>
      <div className="search-row">
        <input
          id={id}
          type="search"
          value={value}
          maxLength={160}
          placeholder={placeholder}
          onChange={(event) => onChange(event.target.value)}
          aria-describedby={`${id}-mode`}
        />
        <button
          type="button"
          className="icon-button"
          aria-expanded={open}
          aria-controls={`${id}-builder`}
          aria-label={`Open regular expression builder for ${label}`}
          onClick={() => setOpen((current) => !current)}
        >
          .*
        </button>
      </div>
      <span id={`${id}-mode`} className="supporting-text">
        {regex ? `Regular expression · flags ${flags || "none"}` : "Plain text · regular expressions are off"}
      </span>
      {open ? (
        <section className="regex-builder overlay-card" id={`${id}-builder`} aria-label={`${label} regular expression builder`}>
          <div className="section-heading compact-heading">
            <div>
              <p className="eyebrow">ECMAScript regular expression</p>
              <h3>Build and test this search</h3>
            </div>
            <button type="button" className="icon-button" aria-label="Close regular expression builder" onClick={() => setOpen(false)}>
              ×
            </button>
          </div>
          <label className="switch-line">
            <input type="checkbox" checked={regex} onChange={(event) => setRegex(event.target.checked)} />
            Use the pattern for this search
          </label>
          <label>
            Pattern
            <input value={value} maxLength={160} onChange={(event) => onChange(event.target.value)} />
          </label>
          <div className="chip-row" aria-label="Regular expression flags">
            {["i", "m", "s", "u"].map((flag) => (
              <label className="filter-chip" key={flag}>
                <input
                  type="checkbox"
                  checked={flags.includes(flag)}
                  onChange={(event) =>
                    setFlags((current) =>
                      event.target.checked ? `${current}${flag}` : current.replace(flag, ""),
                    )
                  }
                />
                {flag}
              </label>
            ))}
          </div>
          <label>
            Sample text
            <textarea value={sample} maxLength={5_000} onChange={(event) => setSample(event.target.value)} />
          </label>
          <output className="validation-message" aria-live="polite">{result}</output>
          <p className="supporting-text">
            The builder supports literals, character classes, anchors, groups, alternation, quantifiers, flags, and capture groups. Evaluation stays on this device and is bounded.
          </p>
        </section>
      ) : null}
    </div>
  );
}

function Status({ tone, children }: { tone: "verified" | "warning" | "local" | "pending"; children: React.ReactNode }) {
  return <span className={`status status-${tone}`}>{children}</span>;
}

function Card({ children, className = "" }: { children: React.ReactNode; className?: string }) {
  return <section className={`card ${className}`.trim()}>{children}</section>;
}

function SettingRow({
  title,
  description,
  provenance,
  children,
}: {
  title: string;
  description: string;
  provenance: string;
  children: React.ReactNode;
}) {
  return (
    <div className="setting-row">
      <div>
        <h3>{title}</h3>
        <details>
          <summary>What this changes</summary>
          <p>{description}</p>
        </details>
        <p className="provenance">{provenance}</p>
      </div>
      <div className="setting-control">{children}</div>
    </div>
  );
}

function Segmented<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string;
  value: T;
  options: Array<{ value: T; label: string }>;
  onChange: (value: T) => void;
}) {
  return (
    <fieldset className="segmented">
      <legend>{label}</legend>
      {options.map((option) => (
        <label key={option.value} className={value === option.value ? "selected" : ""}>
          <input
            type="radio"
            name={label}
            value={option.value}
            checked={value === option.value}
            onChange={() => onChange(option.value)}
          />
          {option.label}
        </label>
      ))}
    </fieldset>
  );
}

export function SiteShell() {
  const [preferences, setPreferences] = useState<Preferences>(defaults);
  const [activeTab, setActiveTab] = useState<TabId>("home");
  const [query, setQuery] = useState("");
  const [docQuery, setDocQuery] = useState("");
  const [settingsQuery, setSettingsQuery] = useState("");
  const [groupQuery, setGroupQuery] = useState("");
  const [masterQuery, setMasterQuery] = useState("");
  const [paletteQuery, setPaletteQuery] = useState("");
  const [paletteOpen, setPaletteOpen] = useState(false);
  const [tabMenu, setTabMenu] = useState<{ id: TabId; x: number; y: number } | null>(null);
  const [appearanceTarget, setAppearanceTarget] = useState<string | null>(null);
  const [notifications, setNotifications] = useState<Notice[]>([]);
  const [tickets, setTickets] = useState<LocalTicket[]>([]);
  const [locks, setLocks] = useState<ToyLock[]>([]);
  const [lockedPrompt, setLockedPrompt] = useState<TabId | null>(null);
  const [unlockText, setUnlockText] = useState("");
  const [lockTarget, setLockTarget] = useState<TabId>("docs");
  const [lockPassword, setLockPassword] = useState("");
  const [vocabularyState, setVocabularyState] = useState("No personal vocabulary file loaded.");
  const [logoData, setLogoData] = useState("");
  const [logoState, setLogoState] = useState("Using the shipped WST monogram.");
  const [voiceFilter, setVoiceFilter] = useState("");
  const [voices, setVoices] = useState<SpeechSynthesisVoice[]>([]);
  const [scheduleEnabled, setScheduleEnabled] = useState(false);
  const [scheduleStart, setScheduleStart] = useState("09:00");
  const [scheduleEnd, setScheduleEnd] = useState("17:00");
  const [scheduleDays, setScheduleDays] = useState<string[]>(["Mon", "Tue", "Wed", "Thu", "Fri"]);
  const [converterFile, setConverterFile] = useState<File | null>(null);
  const [converterTarget, setConverterTarget] = useState("pretty-json");
  const [converterState, setConverterState] = useState("Choose a local JSON or text file to begin.");
  const [ollamaState, setOllamaState] = useState("Not checked. No request has been made.");
  const [totpSecret, setTotpSecret] = useState("");
  const [totpCode, setTotpCode] = useState("— — —");
  const [ticketCategory, setTicketCategory] = useState("Toy lock recovery");
  const [ticketDescription, setTicketDescription] = useState("");
  const [docId, setDocId] = useState<string>(docs[0]?.id ?? "overview");
  const [customPanel, setCustomPanel] = useState({ x: 24, y: 24, width: 520 });
  const mainRef = useRef<HTMLElement>(null);

  const effectiveLanguage = preferences.schoolMode ? "en" : preferences.language;
  const text = (en: string, zh: string) => localized(effectiveLanguage, en, zh);

  useEffect(() => {
    // Browser storage is an external source and is deliberately hydrated after the initial render.
    const timer = window.setTimeout(() => {
      const loaded = safeParse<Preferences>(localStorage.getItem(STORAGE_KEY), defaults);
      setPreferences({ ...defaults, ...loaded, schoolModeName: loaded.schoolModeName || defaults.schoolModeName });
      setNotifications(safeParse<Notice[]>(localStorage.getItem(NOTICE_KEY), []));
      setTickets(safeParse<LocalTicket[]>(localStorage.getItem(TICKET_KEY), []));
      setLocks(safeParse<ToyLock[]>(localStorage.getItem(LOCK_KEY), []));
      const vocabulary = localStorage.getItem(VOCABULARY_KEY);
      if (vocabulary) {
        const parsed = safeParse<{ entries?: Record<string, string> }>(vocabulary, {});
        setVocabularyState(`${Object.keys(parsed.entries ?? {}).length} private replacements loaded on this device.`);
      }
      const logo = localStorage.getItem(LOGO_KEY) ?? "";
      if (logo) {
        setLogoData(logo);
        setLogoState("A validated local custom mark is active.");
      }
    }, 0);
    return () => window.clearTimeout(timer);
  }, []);

  useEffect(() => {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(preferences));
  }, [preferences]);
  useEffect(() => localStorage.setItem(NOTICE_KEY, JSON.stringify(notifications)), [notifications]);
  useEffect(() => localStorage.setItem(TICKET_KEY, JSON.stringify(tickets)), [tickets]);
  useEffect(() => localStorage.setItem(LOCK_KEY, JSON.stringify(locks)), [locks]);

  useEffect(() => {
    const updateVoices = () => setVoices(speechSynthesis.getVoices());
    if ("speechSynthesis" in window) {
      updateVoices();
      speechSynthesis.addEventListener("voiceschanged", updateVoices);
      return () => speechSynthesis.removeEventListener("voiceschanged", updateVoices);
    }
  }, []);

  useEffect(() => {
    const shortcut = (event: globalThis.KeyboardEvent) => {
      if (event.ctrlKey && event.shiftKey && event.key.toLowerCase() === "f") {
        event.preventDefault();
        setPaletteOpen(true);
      }
      if (event.key === "Escape") {
        setPaletteOpen(false);
        setTabMenu(null);
        setAppearanceTarget(null);
      }
    };
    window.addEventListener("keydown", shortcut);
    return () => window.removeEventListener("keydown", shortcut);
  }, []);

  useEffect(() => {
    if ("serviceWorker" in navigator) {
      navigator.serviceWorker.register("./sw.js").catch(() => undefined);
    }
  }, []);

  useEffect(() => {
    const active = locks.find((lock) => lock.target === activeTab && lock.locked);
    // The lock list is an external persisted record; synchronize the active prompt with it.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    if (active && (!active.unlockUntil || active.unlockUntil < Date.now())) setLockedPrompt(activeTab);
  }, [activeTab, locks]);

  function addNotice(title: string, body: string, tone: Notice["tone"] = "info") {
    const notice = { id: crypto.randomUUID(), title, body, tone, createdAt: new Date().toISOString() };
    setNotifications((current) => [notice, ...current].slice(0, 100));
  }

  function updatePreference<K extends keyof Preferences>(key: K, value: Preferences[K]) {
    setPreferences((current) => ({ ...current, [key]: value }));
  }

  function activateTab(id: TabId) {
    const lock = locks.find((item) => item.target === id && item.locked && (!item.unlockUntil || item.unlockUntil < Date.now()));
    if (lock) {
      setLockedPrompt(id);
      return;
    }
    setActiveTab(id);
    setTabMenu(null);
    requestAnimationFrame(() => mainRef.current?.focus());
  }

  function speak(message: string) {
    if (!preferences.narrator || !("speechSynthesis" in window)) return;
    speechSynthesis.cancel();
    const utterance = new SpeechSynthesisUtterance(message);
    utterance.rate = preferences.narratorRate;
    utterance.pitch = preferences.narratorPitch;
    const desired = effectiveLanguage === "zh-HK" ? preferences.cantoneseVoice : preferences.englishVoice;
    utterance.voice = voices.find((voice) => voice.voiceURI === desired) ?? null;
    speechSynthesis.speak(utterance);
  }

  const filteredDocs = useMemo(() => {
    const needle = docQuery.trim().toLocaleLowerCase();
    if (!needle) return docs;
    return docs.filter((doc) => `${doc.title} ${doc.body}`.toLocaleLowerCase().includes(needle));
  }, [docQuery]);

  const filteredTabs = tabs.filter((tab) =>
    `${tab.en} ${tab.zh} ${tab.group}`.toLocaleLowerCase().includes(query.toLocaleLowerCase()),
  );

  const paletteCommands = [
    ...tabs.map((tab) => ({ label: `Open ${tab.en}`, action: () => activateTab(tab.id) })),
    { label: "Switch to light theme", action: () => updatePreference("theme", "light") },
    { label: "Switch to dark theme", action: () => updatePreference("theme", "dark") },
    { label: "Open verified installer", action: () => window.open(release.installerUrl, "_self") },
    { label: "Focus personal vocabulary upload", action: () => document.getElementById("vocabulary-file")?.focus() },
    { label: "Focus app logo upload", action: () => document.getElementById("logo-file")?.focus() },
  ].filter((command) => command.label.toLocaleLowerCase().includes(paletteQuery.toLocaleLowerCase()));

  async function handleVocabulary(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    if (file.size > 65_536) {
      setVocabularyState("Rejected: file exceeds the 64 KiB limit.");
      return;
    }
    const raw = await file.text();
    try {
      const parsed = JSON.parse(raw) as { version?: unknown; entries?: unknown };
      if (parsed.version !== 1 || !parsed.entries || typeof parsed.entries !== "object" || Array.isArray(parsed.entries)) {
        throw new Error("Expected version 1 with an entries object.");
      }
      const entries = Object.entries(parsed.entries as Record<string, unknown>);
      if (entries.length > 100) throw new Error("The file contains more than 100 entries.");
      for (const [key, value] of entries) {
        if (key.length > 80 || typeof value !== "string" || value.length > 240 || ["__proto__", "prototype", "constructor"].includes(key)) {
          throw new Error("An entry exceeds the schema limits or uses an unsafe key.");
        }
      }
      localStorage.setItem(VOCABULARY_KEY, JSON.stringify({ version: 1, entries: Object.fromEntries(entries) }));
      setVocabularyState(`${entries.length} private replacements loaded on this device.`);
      addNotice("Personal vocabulary loaded", "The validated file is active only in this browser profile.", "success");
    } catch (error) {
      setVocabularyState(`Rejected: ${error instanceof Error ? error.message : "invalid JSON"}`);
    } finally {
      event.target.value = "";
    }
  }

  async function handleLogo(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    if (file.size > 1_048_576) {
      setLogoState("Rejected: the logo exceeds the 1 MiB limit.");
      return;
    }
    const bytes = new Uint8Array(await file.arrayBuffer());
    const png = bytes.length > 8 && bytes.slice(0, 8).join(",") === "137,80,78,71,13,10,26,10";
    const jpeg = bytes.length > 3 && bytes[0] === 255 && bytes[1] === 216 && bytes[2] === 255;
    const webp = bytes.length > 12 && new TextDecoder().decode(bytes.slice(0, 4)) === "RIFF" && new TextDecoder().decode(bytes.slice(8, 12)) === "WEBP";
    if (!png && !jpeg && !webp) {
      setLogoState("Rejected: choose a genuine PNG, JPEG, or WebP image.");
      return;
    }
    const reader = new FileReader();
    reader.onload = () => {
      const result = String(reader.result ?? "");
      const image = new Image();
      image.onload = () => {
        if (image.naturalWidth > 2048 || image.naturalHeight > 2048 || image.naturalWidth * image.naturalHeight > 4_194_304) {
          setLogoState("Rejected: decoded dimensions exceed 2048 × 2048 or four million pixels.");
          return;
        }
        setLogoData(result);
        localStorage.setItem(LOGO_KEY, result);
        setLogoState(`Validated locally at ${image.naturalWidth} × ${image.naturalHeight}.`);
        addNotice("Custom mark applied", "The image stays in this browser profile and changes presentation only.", "success");
      };
      image.onerror = () => setLogoState("Rejected: the image decoder could not open the file.");
      image.src = result;
    };
    reader.readAsDataURL(file);
    event.target.value = "";
  }

  async function convertFile() {
    if (!converterFile) return;
    if (converterFile.size > 2_097_152) {
      setConverterState("Rejected: this browser tool limits each source to 2 MiB.");
      return;
    }
    try {
      const raw = await converterFile.text();
      let output = raw;
      let extension = "txt";
      if (converterTarget === "pretty-json") {
        output = `${JSON.stringify(JSON.parse(raw), null, 2)}\n`;
        extension = "json";
      } else if (converterTarget === "ndjson") {
        const value = JSON.parse(raw);
        const rows = Array.isArray(value) ? value : [value];
        output = `${rows.map((row) => JSON.stringify(row)).join("\n")}\n`;
        extension = "jsonl";
      } else if (converterTarget === "markdown") {
        const value = JSON.parse(raw);
        if (!Array.isArray(value) || !value.length || typeof value[0] !== "object") throw new Error("Markdown tables require a non-empty JSON array of objects.");
        const keys = Object.keys(value[0]);
        output = `| ${keys.join(" | ")} |\n| ${keys.map(() => "---").join(" | ")} |\n${value
          .map((row) => `| ${keys.map((key) => String(row[key] ?? "").replaceAll("|", "\\|")).join(" | ")} |`)
          .join("\n")}\n`;
        extension = "md";
      }
      const base = converterFile.name.replace(/\.[^.]+$/, "") || "converted";
      downloadText(`${base}.${extension}`, output);
      setConverterState(`Converted ${converterFile.name} locally. The source was unchanged.`);
      addNotice("Conversion complete", `${base}.${extension} is ready.`, "success");
    } catch (error) {
      setConverterState(`Conversion failed: ${error instanceof Error ? error.message : "unsupported input"}`);
    }
  }

  async function checkOllama() {
    setOllamaState("Checking the documented local endpoint…");
    try {
      const [versionResponse, tagsResponse] = await Promise.all([
        fetch("http://127.0.0.1:11434/api/version", { signal: AbortSignal.timeout(4_000) }),
        fetch("http://127.0.0.1:11434/api/tags", { signal: AbortSignal.timeout(4_000) }),
      ]);
      if (!versionResponse.ok || !tagsResponse.ok) throw new Error(`local API returned ${versionResponse.status}/${tagsResponse.status}`);
      const version = (await versionResponse.json()) as { version?: string };
      const tags = (await tagsResponse.json()) as { models?: unknown[] };
      setOllamaState(`Local Ollama ${version.version ?? "unknown version"} is reachable with ${tags.models?.length ?? 0} installed model tags.`);
    } catch (error) {
      setOllamaState(`Local Ollama is unavailable or blocked by browser cross-origin rules: ${error instanceof Error ? error.message : "request failed"}. No cloud fallback was used.`);
    }
  }

  async function generateTotp() {
    try {
      const code = await totp(totpSecret);
      setTotpCode(`${code.slice(0, 3)} ${code.slice(3)}`);
    } catch (error) {
      setTotpCode(error instanceof Error ? error.message : "Invalid secret");
    }
  }

  async function createLock(event: FormEvent) {
    event.preventDefault();
    if (lockPassword.length < 6) {
      addNotice("Toy lock not created", "Use at least six characters. This is still only a local speed bump.", "warning");
      return;
    }
    const digest = await digestText(`${lockTarget}:${lockPassword}`);
    setLocks((current) => [
      ...current.filter((lock) => lock.target !== lockTarget),
      { target: lockTarget, digest, locked: true, unlockUntil: null },
    ]);
    setLockPassword("");
    addNotice("Toy lock created", `${tabs.find((tab) => tab.id === lockTarget)?.en} now asks for its own password.`, "success");
  }

  async function unlock() {
    if (!lockedPrompt) return;
    const existing = locks.find((lock) => lock.target === lockedPrompt);
    const digest = await digestText(`${lockedPrompt}:${unlockText}`);
    if (!existing || digest !== existing.digest) {
      addNotice("Value did not match", "Try again, or clear this site's storage through your browser to reset toy locks.", "error");
      return;
    }
    setLocks((current) => current.map((lock) => (lock.target === lockedPrompt ? { ...lock, unlockUntil: Date.now() + 15 * 60_000 } : lock)));
    const target = lockedPrompt;
    setLockedPrompt(null);
    setUnlockText("");
    setActiveTab(target);
  }

  function createTicket(event: FormEvent) {
    event.preventDefault();
    if (!ticketDescription.trim()) return;
    const ticket: LocalTicket = {
      id: `LOCAL-${new Date().toISOString().slice(0, 10).replaceAll("-", "")}-${String(tickets.length + 1).padStart(4, "0")}`,
      category: ticketCategory,
      description: ticketDescription.trim(),
      severity: "Spectacularly unstaffed",
      status: "Resolved locally: follow the browser storage instructions",
      createdAt: new Date().toISOString(),
    };
    setTickets((current) => [ticket, ...current]);
    setTicketDescription("");
    addNotice("Local ticket created", `${ticket.id} was stored only on this device.`, "info");
  }

  const filteredInventory = inventory.features.filter((feature) =>
    `${feature.name} ${feature.status} ${feature.article}`.toLocaleLowerCase().includes(masterQuery.toLocaleLowerCase()),
  );

  const renderHome = () => (
    <div className="page-stack">
      <section className="hero">
        <div className="hero-copy">
          <p className="eyebrow">Windows Server Setupper · local administration toolkit</p>
          <h1>{text("Server setup that remembers what finished.", "伺服器設定識得邊啲已經做完。")}</h1>
          <p className="hero-lede">
            {text(
              "Configure roles, baseline settings, directory services, shared folders, and selected software with durable recovery and explicit uncertain-outcome review.",
              "設定角色、基準、目錄服務、共享資料夾同指定軟件；有持久復原，結果未明就老實要求你覆核。",
            )}
          </p>
          <div className="button-row">
            <a className="filled-button" href={release.installerUrl}>Download previous verified installer</a>
            <button type="button" className="tonal-button" onClick={() => activateTab("recovery")}>Explore recovery</button>
          </div>
          <p className="supporting-text">
            Version 1.0.0.0 · release {release.tag} · intentionally unsigned · SHA-256 published below
          </p>
        </div>
        <div className="hero-diagram" aria-label="Recovery workflow: prepare, run, record, resume">
          <div className="diagram-core">WST</div>
          {["Prepare", "Run", "Record", "Resume"].map((label, index) => (
            <span key={label} style={{ "--index": index } as React.CSSProperties}>{label}</span>
          ))}
        </div>
      </section>
      <div className="metric-grid">
        <Card><p className="metric">146</p><p>Historical focused recovery checks; not rerun for the released candidate</p></Card>
        <Card><p className="metric">7</p><p>Durable operation states, including blocked and indeterminate</p></Card>
        <Card><p className="metric">1</p><p>Process-wide coordinator for server-changing operations</p></Card>
      </div>
      <Card>
        <div className="section-heading">
          <div><p className="eyebrow">Previous release evidence</p><h2>Previous verified download</h2></div>
          <Status tone="verified">Prior verified asset</Status>
        </div>
        <dl className="facts-grid">
          {currentProjectFacts.map(([term, value]) => <div key={term}><dt>{term}</dt><dd>{value}</dd></div>)}
        </dl>
      </Card>
      <Card>
        <div className="section-heading"><div><p className="eyebrow">Feature map</p><h2>Everything documented here</h2></div></div>
        <div className="feature-grid">
          {[
            ["Durable recovery", "Atomic version 3 records, dependency-aware resume, bounded retries."],
            ["Truthful uncertainty", "A running process that cannot be proven stopped is never guessed safe to repeat."],
            ["Guided Exchange setup", "A mostly pre-filled local installer that never pre-fills credentials."],
            ["Protected execution", "Trusted system paths, constrained child processes, and output bounds."],
            ["One-click builds", "Repository scripts build the application and unsigned installer."],
            ["Local companion tools", "Private browser-only preferences, conversion, narration, and TOTP calculation."],
          ].map(([title, body]) => <article key={title}><h3>{title}</h3><p>{body}</p></article>)}
        </div>
      </Card>
    </div>
  );

  const renderRecovery = () => (
    <div className="page-stack">
      <header className="page-header"><p className="eyebrow">Resilient by construction</p><h1>Recovery never turns uncertainty into success.</h1><p>Every operation is recorded before and after execution. Completed independent work remains complete, while blocked or uncertain work waits for review.</p></header>
      <div className="state-flow" aria-label="Recovery state flow">
        {["pending", "running", "retrying", "failed", "blocked", "indeterminate", "succeeded"].map((state) => <div key={state} className={`state state-${state}`}><strong>{state}</strong><span>{state === "indeterminate" ? "Needs reconciliation" : state === "succeeded" ? "Preserved on restart" : "Recorded durably"}</span></div>)}
      </div>
      <div className="two-column">
        <Card><h2>Safe automatic retry</h2><ul className="check-list"><li>Only operations declared idempotent qualify.</li><li>Attempts are bounded and delayed.</li><li>Persistence failure after start becomes uncertain.</li><li>Nonzero process exits remain failures.</li></ul></Card>
        <Card><h2>Reviewed reconciliation</h2><ol><li><strong>It completed — continue</strong> preserves the action as succeeded.</li><li><strong>Stopped without completing — retry</strong> begins a new reviewed generation.</li></ol><p>One answer is never applied to several uncertain operations.</p></Card>
      </div>
      <Card><h2>Security and failure boundaries</h2><div className="feature-grid"><article><h3>Protected storage</h3><p>Recovery state is bounded, canonical, hashed, and stored under protected machine data.</p></article><article><h3>Contained processes</h3><p>Child processes are created suspended, assigned to a kill-on-close job, then resumed only after containment succeeds.</p></article><article><h3>Evidence-preserving repair</h3><p>Corrupt checkpoints are archived and verified before an empty recovery state is created.</p></article></div></Card>
    </div>
  );

  const renderDownload = () => (
    <div className="page-stack">
      <header className="page-header"><p className="eyebrow">Previous release published {new Date(release.published).toLocaleString()}</p><h1>{release.name}</h1><p>The immutable asset link below names the previous verified release and installer. The pending final release has no link until publication, and this page never guesses one.</p></header>
      <Card className="download-card">
        <div className="download-mark" aria-hidden="true">↓</div>
        <div><h2>{release.installer}</h2><p>{release.bytes.toLocaleString()} bytes · unsigned executable</p><code>{release.sha256}</code><div className="button-row"><a className="filled-button" href={release.installerUrl}>Download installer</a><a className="text-button" href={release.releaseUrl}>Read release notes</a></div></div>
      </Card>
      <aside className="warning-panel"><strong>Unknown Publisher warning expected</strong><p>The installer is intentionally unsigned and may trigger an operating-system reputation warning. Verify the SHA-256 value before running it. No signature claim is made.</p></aside>
      <Card><h2>Verification boundary</h2><ul className="check-list"><li><code>build.bat /s</code> completed for the runnable application.</li><li><code>build-installer.bat /s</code> completed and verified structure, provenance, digest, and the absence of a PE certificate table.</li><li>Tests, linting, reviews, audits, runtime UI launch, and screenshots were intentionally not run in the expedited delivery.</li><li>The release came from <a href={release.commitUrl}><code>{release.commit}</code></a>.</li></ul></Card>
    </div>
  );

  const renderDocs = () => {
    const current = docs.find((doc) => doc.id === docId) ?? filteredDocs[0] ?? docs[0];
    return <div className="docs-layout">
      <aside className="docs-index">
        <SearchField id="documentation-search" label="Search offline documentation" value={docQuery} onChange={setDocQuery} />
        <p className="supporting-text">{filteredDocs.length} of {docs.length} bundled articles</p>
        <nav aria-label="Documentation articles">{filteredDocs.map((doc) => <button type="button" key={doc.id} className={current?.id === doc.id ? "active" : ""} onClick={() => setDocId(doc.id)}>{doc.title}<span>{doc.category}</span></button>)}</nav>
      </aside>
      <article className="article-viewer">
        <p className="eyebrow">{current?.category}</p>
        <h1>{current?.title}</h1>
        <div className="article-copy">{current?.sections.map((section) => <section key={section.heading}><h2>{section.heading}</h2>{section.paragraphs.map((paragraph) => <p key={paragraph}>{paragraph}</p>)}</section>)}</div>
        <footer><strong>Suggested articles</strong><div className="chip-row">{current?.suggested.map((id) => { const target = docs.find((doc) => doc.id === id); return target ? <button type="button" className="filter-chip" key={id} onClick={() => setDocId(id)}>{target.title}</button> : null; })}</div></footer>
      </article>
    </div>;
  };

  const renderLocalTools = () => (
    <div className="page-stack">
      <header className="page-header"><p className="eyebrow">Runs in this browser profile</p><h1>Local companion tools</h1><p>These tools do not upload source files, vocabulary data, images, prompts, or authenticator secrets. Browser limitations are stated where they matter.</p></header>
      <div className="tool-grid">
        <Card>
          <div className="section-heading"><div><p className="eyebrow">File converter</p><h2>Bounded local conversion</h2></div><Status tone="local">Device only</Status></div>
          <label>Source file<input type="file" accept=".json,.txt,.md,application/json,text/plain" onChange={(event) => setConverterFile(event.target.files?.[0] ?? null)} /></label>
          <Segmented label="Target format" value={converterTarget} onChange={setConverterTarget} options={[{ value: "pretty-json", label: "Formatted JSON" }, { value: "ndjson", label: "JSON Lines" }, { value: "markdown", label: "Markdown table" }]} />
          <p className="supporting-text">Documents/PDF, Images, Audio, Video, Archives, Spreadsheets, and Binary Encodings remain visible in the catalog but unavailable because this static site does not bundle the required sandboxed adapters.</p>
          <button type="button" className="filled-button" disabled={!converterFile} onClick={convertFile}>Convert locally</button>
          <output className="validation-message" aria-live="polite">{converterState}</output>
        </Card>
        <Card>
          <div className="section-heading"><div><p className="eyebrow">Ollama manager</p><h2>Local runtime health</h2></div><Status tone="pending">User initiated</Status></div>
          <p>The static site can probe Ollama’s documented loopback API only after you choose the action. Browser cross-origin rules may block the request even when Ollama is running.</p>
          <button type="button" className="tonal-button" onClick={checkOllama}>Check local Ollama</button>
          <output className="validation-message" aria-live="polite">{ollamaState}</output>
          <details><summary>Capability boundary</summary><p>The full model catalog, pulls, deletes, streaming chat, hardware-fit evidence, and allowlisted harness launch belong in the installed application. This site never invents models, uses a proxy, or claims that Ollama launches arbitrary programs.</p></details>
        </Card>
        <Card>
          <div className="section-heading"><div><p className="eyebrow">Authenticator</p><h2>In-memory TOTP calculator</h2></div><Status tone="local">Not stored</Status></div>
          <label>Base32 secret<input type="password" autoComplete="off" value={totpSecret} onChange={(event) => setTotpSecret(event.target.value)} /></label>
          <button type="button" className="tonal-button" onClick={generateTotp}>Calculate current code</button>
          <output className="totp-code" aria-live="polite">{totpCode}</output>
          <p className="supporting-text">RFC 6238 · SHA-1 · 6 digits · 30 seconds. The value remains in memory and is never written to site storage. QR import, secure vault storage, and secrets export are unavailable in a static browser page.</p>
        </Card>
        <Card>
          <div className="section-heading"><div><p className="eyebrow">Support Tickets</p><h2>The entirely local support desk</h2></div></div>
          <p className="plain-disclosure">Nothing is sent anywhere. No ticket exists outside this browser profile, no network request is made, no data is collected, and nobody is reading it.</p>
          <form onSubmit={createTicket} className="form-stack">
            <Segmented label="Category" value={ticketCategory} onChange={setTicketCategory} options={[{ value: "Toy lock recovery", label: "Toy lock recovery" }, { value: "Local data reset", label: "Local data reset" }, { value: "Documentation", label: "Documentation" }]} />
            <label>Description<textarea value={ticketDescription} maxLength={500} onChange={(event) => setTicketDescription(event.target.value)} /></label>
            <button className="filled-button" type="submit">Create local ticket</button>
          </form>
          {tickets.length ? <ul className="ticket-list">{tickets.map((ticket) => <li key={ticket.id}><strong>{ticket.id}</strong><span>{ticket.category} · {ticket.status}</span><small>{ticket.description}</small></li>)}</ul> : <p className="empty-state">No local tickets. The desk is enjoying a record-breaking response time.</p>}
          <details><summary>Reset route</summary><p>Use your browser’s site-data controls for this origin to clear local settings and toy locks. This page does not delete storage for you.</p></details>
        </Card>
      </div>
    </div>
  );

  const renderSettings = () => (
    <div className="page-stack">
      <header className="page-header"><p className="eyebrow">Persisted on this device</p><h1>Settings</h1><p>Every setting states whether it came from a saved choice or a compiled default. Use <kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> to find and focus any destination.</p></header>
      <SearchField id="settings-search" label="Search settings" value={settingsQuery} onChange={setSettingsQuery} />
      <div className="settings-tabs" role="tablist" aria-label="Settings sections"><button role="tab" aria-selected="true">Language & voice</button><button role="tab" aria-selected="false">Appearance</button><button role="tab" aria-selected="false">Schedules</button><button role="tab" aria-selected="false">Privacy</button></div>
      <Card>
        <SettingRow title="Display language" description="Changes the site’s authored interface copy. Bilingual mode presents English first, then Cantonese." provenance={`Current value: ${preferences.language}; ${localStorage.getItem(STORAGE_KEY) ? "saved on this device" : "compiled default is English"}.`}>
          <Segmented label="Language" value={preferences.language} onChange={(value) => updatePreference("language", value)} options={[{ value: "en", label: "English" }, { value: "zh-HK", label: "廣東話" }, { value: "bilingual", label: "English + 廣東話" }]} />
        </SettingRow>
        {!preferences.schoolMode ? <>
          <SettingRow title="English funny level" description="Styles English explanations, including warnings and failures, without changing facts." provenance={`Current value: ${preferences.funnyEnglish}; compiled default is 2.`}><label>Level {preferences.funnyEnglish}<input type="range" min="1" max="5" value={preferences.funnyEnglish} onChange={(event) => updatePreference("funnyEnglish", Number(event.target.value))} /></label></SettingRow>
          <SettingRow title="Cantonese funny level" description="Styles Cantonese explanations independently from English." provenance={`Current value: ${preferences.funnyCantonese}; compiled default is 3.`}><label>Level {preferences.funnyCantonese}<input type="range" min="1" max="5" value={preferences.funnyCantonese} onChange={(event) => updatePreference("funnyCantonese", Number(event.target.value))} /></label></SettingRow>
          <SettingRow title="Dialog emoji decoration" description="Adds a relevant non-semantic emoji to dialogs and message boxes only. Buttons and accessible names remain unchanged." provenance={`Current value: ${preferences.showDialogEmoji ? "shown" : "hidden"}; compiled default is shown.`}><label className="switch-line"><input type="checkbox" checked={preferences.showDialogEmoji} onChange={(event) => updatePreference("showDialogEmoji", event.target.checked)} />Show decorations</label></SettingRow>
        </> : null}
        <SettingRow title={preferences.schoolModeName} description="Forces English and suppresses Cantonese, playful copy, personal vocabulary, and surprise content. It is a local experience lock, not a security boundary." provenance={`Current value: ${preferences.schoolMode ? "on" : "off"}; compiled default is off.`}><label className="switch-line"><input type="checkbox" checked={preferences.schoolMode} onChange={(event) => updatePreference("schoolMode", event.target.checked)} />{preferences.schoolMode ? "On" : "Off"}</label><label>Mode name<input value={preferences.schoolModeName} maxLength={40} onChange={(event) => updatePreference("schoolModeName", event.target.value || "School mode")} /></label></SettingRow>
        <SettingRow title="Narrator" description="Speaks selected site events through installed browser speech voices. Speech is off by default, serialized, and cancelled before a newer utterance." provenance={`Current value: ${preferences.narrator ? "on" : "off"}; compiled default is off.`}><label className="switch-line"><input type="checkbox" checked={preferences.narrator} onChange={(event) => updatePreference("narrator", event.target.checked)} />Enable narration</label><div className="range-pair"><label>Rate {preferences.narratorRate.toFixed(1)}<input type="range" min="0.5" max="2" step="0.1" value={preferences.narratorRate} onChange={(event) => updatePreference("narratorRate", Number(event.target.value))} /></label><label>Pitch {preferences.narratorPitch.toFixed(1)}<input type="range" min="0" max="2" step="0.1" value={preferences.narratorPitch} onChange={(event) => updatePreference("narratorPitch", Number(event.target.value))} /></label></div><button className="tonal-button" type="button" onClick={() => speak("Windows Server Setupper narrator preview")}>Preview voice</button></SettingRow>
      </Card>
      <Card>
        <div className="section-heading"><div><p className="eyebrow">Installed voices</p><h2>Voice selection</h2></div><Status tone="local">Runtime list</Status></div>
        <SearchField id="voice-search" label="Filter installed voices" value={voiceFilter} onChange={setVoiceFilter} />
        <div className="voice-list" role="radiogroup" aria-label="English narrator voice"><label><input type="radio" name="english-voice" checked={preferences.englishVoice === "auto"} onChange={() => updatePreference("englishVoice", "auto")} />Choose automatically</label>{voices.filter((voice) => voice.name.toLowerCase().includes(voiceFilter.toLowerCase())).slice(0, 30).map((voice) => <label key={voice.voiceURI}><input type="radio" name="english-voice" checked={preferences.englishVoice === voice.voiceURI} onChange={() => updatePreference("englishVoice", voice.voiceURI)} />{voice.name} <span>{voice.lang}{voice.localService ? " · local" : " · network-backed"}</span></label>)}</div>
        <p className="supporting-text">{voices.length ? `${voices.length} voices reported by this browser.` : "Voice enumeration is empty. The browser may populate it later or speech synthesis may be unavailable."}</p>
      </Card>
      <Card>
        <div className="section-heading"><div><p className="eyebrow">Material 3 appearance</p><h2>Theme, accent, density, and mark</h2></div></div>
        <div className="setting-grid"><Segmented label="Theme" value={preferences.theme} onChange={(value) => updatePreference("theme", value)} options={[{ value: "light", label: "Light" }, { value: "dark", label: "Dark" }, { value: "contrast", label: "High contrast" }]} /><Segmented label="Density" value={preferences.density} onChange={(value) => updatePreference("density", value)} options={[{ value: "comfortable", label: "Comfortable" }, { value: "compact", label: "Compact" }]} /><label>Accent color<input type="color" value={preferences.accent} onChange={(event) => updatePreference("accent", event.target.value)} /><input value={preferences.accent} pattern="#[0-9A-Fa-f]{6}" onChange={(event) => /^#[0-9A-Fa-f]{6}$/.test(event.target.value) && updatePreference("accent", event.target.value)} /></label><label>Displayed app name<input value={preferences.displayName} maxLength={60} onChange={(event) => updatePreference("displayName", event.target.value || defaults.displayName)} /><span className="supporting-text">Presentation only. Package identity, update feed, and storage keys never change.</span></label></div>
        <div className="upload-row"><label htmlFor="logo-file" className="file-button">Choose local custom mark</label><input id="logo-file" className="visually-hidden" type="file" accept="image/png,image/jpeg,image/webp" onChange={handleLogo} /><button type="button" className="text-button" onClick={() => { setLogoData(""); localStorage.removeItem(LOGO_KEY); setLogoState("Using the shipped WST monogram."); }}>Reset mark</button><span>{logoState}</span></div>
        <button type="button" className="tonal-button" onClick={() => setAppearanceTarget("Settings card")}>Edit this panel’s appearance…</button>
      </Card>
      <Card>
        <div className="section-heading"><div><p className="eyebrow">Scheduled settings</p><h2>Local time window</h2></div><Status tone="local">Browser local time</Status></div>
        <label className="switch-line"><input type="checkbox" checked={scheduleEnabled} onChange={(event) => setScheduleEnabled(event.target.checked)} />Enable this rule</label>
        <div className="setting-grid"><label>Start time<input type="time" value={scheduleStart} onChange={(event) => setScheduleStart(event.target.value)} /></label><label>End time<input type="time" value={scheduleEnd} onChange={(event) => setScheduleEnd(event.target.value)} /></label></div>
        <fieldset><legend>Weekdays</legend><div className="chip-row">{["Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun"].map((day) => <label className="filter-chip" key={day}><input type="checkbox" checked={scheduleDays.includes(day)} onChange={(event) => setScheduleDays((current) => event.target.checked ? [...current, day] : current.filter((item) => item !== day))} />{day}</label>)}</div></fieldset>
        <p className="supporting-text">The rule uses the browser’s configured local timezone and daylight-saving behavior. HTTPS API and Home Assistant sources are documented but unavailable in this static surface because it has no privileged credential vault or server-side request boundary.</p>
      </Card>
      {!preferences.schoolMode ? <Card>
        <div className="section-heading"><div><p className="eyebrow">Private local data</p><h2>Personal vocabulary JSON</h2></div><Status tone="local">No network</Status></div>
        <p>Schema: <code>{`{"version":1,"entries":{"source":"replacement"}}`}</code>. Maximum 64 KiB, 100 entries, 80-character keys, and 240-character values. Unsafe object keys and unknown versions are rejected as a whole.</p>
        <div className="upload-row"><label htmlFor="vocabulary-file" className="file-button">Choose vocabulary JSON</label><input id="vocabulary-file" className="visually-hidden" type="file" accept="application/json,.json" onChange={handleVocabulary} /><button type="button" className="text-button" onClick={() => { localStorage.removeItem(VOCABULARY_KEY); setVocabularyState("No personal vocabulary file loaded."); }}>Clear private cache</button><span>{vocabularyState}</span></div>
      </Card> : null}
      <Card>
        <div className="section-heading"><div><p className="eyebrow">Toy locks</p><h2>One local password per tab</h2></div></div>
        <p>These are self-imposed speed bumps, not security, protection, or encryption. Clear this site’s browser storage to reset every lock.</p>
        <form onSubmit={createLock} className="form-stack"><Segmented label="Tab to lock" value={lockTarget} onChange={setLockTarget} options={tabs.map((tab) => ({ value: tab.id, label: tab.en }))} /><label>New password<input type="password" value={lockPassword} autoComplete="new-password" onChange={(event) => setLockPassword(event.target.value)} /></label><button className="filled-button" type="submit">Create this tab’s toy lock</button></form>
        <ul className="lock-list">{locks.map((lock) => <li key={lock.target}><strong>{tabs.find((tab) => tab.id === lock.target)?.en}</strong><span>{lock.unlockUntil && lock.unlockUntil > Date.now() ? "Unlocked for this 15-minute window" : "Locked"}</span><button type="button" className="text-button" onClick={() => setLocks((current) => current.filter((item) => item.target !== lock.target))}>Remove lock</button></li>)}</ul>
      </Card>
    </div>
  );

  const renderCompleteness = () => (
    <div className="page-stack">
      <header className="page-header"><p className="eyebrow">Hand-written contract inventory</p><h1>Surface completeness</h1><p>This list is intentionally explicit. The focused negative regression removes each named row and each required proof field in turn; validation must fail before restoration passes.</p></header>
      <SearchField id="completeness-search" label="Search completeness inventory" value={masterQuery} onChange={setMasterQuery} />
      <div className="completeness-summary"><Status tone="verified">{inventory.features.filter((feature) => feature.status === "implemented").length} implemented</Status><Status tone="pending">{inventory.features.filter((feature) => feature.status !== "implemented").length} bounded or pending</Status><span>{inventory.version}</span></div>
      <div className="inventory-table" role="table" aria-label="Feature completeness inventory"><div role="row" className="table-head"><span role="columnheader">Feature</span><span role="columnheader">Implementation</span><span role="columnheader">Documentation</span><span role="columnheader">Evidence</span></div>{filteredInventory.map((feature) => <div role="row" key={feature.id}><span role="cell"><strong>{feature.name}</strong><small>{feature.status}</small></span><span role="cell"><code>{feature.implementation}</code></span><span role="cell"><code>{feature.article}</code></span><span role="cell">{feature.test}<small>{feature.capture}</small></span></div>)}</div>
      <aside className="warning-panel"><strong>Real capture evidence is pending</strong><p>This delegated background build explicitly did not open a browser or capture the site. The inventory records that boundary instead of treating a source assertion as visual proof.</p></aside>
      <Card><h2>Navigation discovery lab</h2><div className="setting-grid"><SearchField id="current-strip-search" label="Search current tab strip" value={query} onChange={setQuery} /><SearchField id="group-tabs-search" label="Search tabs in Product group" value={groupQuery} onChange={setGroupQuery} /><SearchField id="groups-search" label="Search tab groups" value={groupQuery} onChange={setGroupQuery} /><SearchField id="master-tabs-search" label="Search all open tabs" value={masterQuery} onChange={setMasterQuery} /></div></Card>
    </div>
  );

  const content = activeTab === "home" ? renderHome() : activeTab === "recovery" ? renderRecovery() : activeTab === "download" ? renderDownload() : activeTab === "docs" ? renderDocs() : activeTab === "local-tools" ? renderLocalTools() : activeTab === "settings" ? renderSettings() : renderCompleteness();

  return (
    <div className="site-root" data-theme={preferences.theme} data-density={preferences.density} data-dock={preferences.dock} style={{ "--accent": preferences.accent } as React.CSSProperties}>
      <a className="skip-link" href="#main-content">Skip to content</a>
      <header className="top-app-bar">
        <button type="button" className="brand-button" onClick={() => activateTab("home")} aria-label={`Open ${preferences.displayName} overview`}>
          <span className="brand-mark">
            {/* The shipped and user-selected marks are bounded local files; framework image optimization is intentionally not involved. */}
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img
              src={logoData || shippedLogo}
              alt="Windows Server Setupper logo"
              onError={(event) => {
                event.currentTarget.hidden = true;
                event.currentTarget.parentElement?.classList.add("brand-mark-fallback");
              }}
            />
          </span>
          <strong>{preferences.displayName}</strong>
        </button>
        <div className="top-actions"><Status tone="verified">Previous release available</Status><button type="button" className="tonal-button compact-button" onClick={() => setPaletteOpen(true)}><kbd>Ctrl</kbd>+<kbd>Shift</kbd>+<kbd>F</kbd> Commands</button></div>
      </header>
      <nav className="tab-strip" aria-label="Main documentation tabs">
        <SearchField id="tab-strip-search" label="Search this tab strip" value={query} onChange={setQuery} />
        <div className="tab-groups">{["Product", "Explore", "Configure"].map((group) => {
          const groupTabs = filteredTabs.filter((tab) => tab.group === group);
          if (!groupTabs.length) return null;
          return <section key={group} className="tab-group"><h2>{group}</h2><div role="tablist" aria-label={`${group} tabs`} aria-orientation={preferences.dock === "left" || preferences.dock === "right" ? "vertical" : "horizontal"}>{groupTabs.map((tab) => <button key={tab.id} type="button" role="tab" aria-selected={activeTab === tab.id} aria-controls={`panel-${tab.id}`} className={activeTab === tab.id ? "active" : ""} onClick={() => activateTab(tab.id)} onContextMenu={(event) => { event.preventDefault(); if (event.shiftKey) setAppearanceTarget(`${tab.en} tab`); else setTabMenu({ id: tab.id, x: event.clientX, y: event.clientY }); }}><span className="tab-icon" aria-hidden="true">{tab.icon}</span><span>{text(tab.en, tab.zh)}</span>{preferences.pinnedTabs.includes(tab.id) ? <span className="pin" aria-label="Pinned">•</span> : null}{locks.some((lock) => lock.target === tab.id && lock.locked) ? <span aria-label="Toy locked">▣</span> : null}</button>)}</div></section>;
        })}</div>
        <button type="button" className="dock-button" onClick={() => updatePreference("dock", preferences.dock === "left" ? "top" : preferences.dock === "top" ? "right" : preferences.dock === "right" ? "bottom" : "left")}>Dock: {preferences.dock}</button>
      </nav>
      <main id="main-content" ref={mainRef} tabIndex={-1} className="main-content" role="tabpanel" aria-label={`${tabs.find((tab) => tab.id === activeTab)?.en} page`}>
        {content}
      </main>
      <footer className="site-footer"><span>Windows Server Setupper documentation · version 1.0.0.0</span><a href="https://github.com/cafepromenade/Windows-Server-Setupper">Source repository</a><a href={release.releaseUrl}>Previous verified release</a><span>Final release download: pending publication</span><span>No analytics · no remote fonts · local preferences only</span></footer>

      {tabMenu ? <div className="context-menu overlay-card" role="menu" style={{ left: tabMenu.x, top: tabMenu.y }}><SearchField id="tab-context-search" label="Filter tab actions" value={groupQuery} onChange={setGroupQuery} /><button role="menuitem" type="button" onClick={() => updatePreference("pinnedTabs", preferences.pinnedTabs.includes(tabMenu.id) ? preferences.pinnedTabs.filter((id) => id !== tabMenu.id) : [...preferences.pinnedTabs, tabMenu.id])}>{preferences.pinnedTabs.includes(tabMenu.id) ? "Unpin tab" : "Pin tab"}<kbd>Alt+P</kbd></button><button role="menuitem" type="button" onClick={() => { setAppearanceTarget(`${tabs.find((tab) => tab.id === tabMenu.id)?.en} tab`); setTabMenu(null); }}>Edit tab appearance…<kbd>Shift+right-click</kbd></button><button role="menuitem" type="button" onClick={() => { setLockTarget(tabMenu.id); setActiveTab("settings"); setTabMenu(null); }}>Lock this tab…</button><button role="menuitem" type="button" onClick={() => setTabMenu(null)}>Close menu<kbd>Esc</kbd></button></div> : null}

      {appearanceTarget ? <section className="appearance-panel overlay-card" style={{ left: customPanel.x, top: customPanel.y, width: customPanel.width }} aria-label={`Appearance editor for ${appearanceTarget}`}><div className="section-heading compact-heading"><div><p className="eyebrow">Appearance editor</p><h2>{appearanceTarget}</h2></div><button className="icon-button" type="button" aria-label="Close appearance editor" onClick={() => setAppearanceTarget(null)}>×</button></div><SearchField id="appearance-search" label="Search appearance properties" value={settingsQuery} onChange={setSettingsQuery} /><label>Width {customPanel.width}px<input type="range" min="320" max="800" value={customPanel.width} onChange={(event) => setCustomPanel((current) => ({ ...current, width: Number(event.target.value) }))} /></label><label>Accent color<input type="color" value={preferences.accent} onChange={(event) => updatePreference("accent", event.target.value)} /></label><label>Corner radius<input type="range" min="0" max="48" defaultValue="24" /></label><p className="supporting-text">Typography, background, border, radius, spacing, hover, focus, and reset controls are represented here. Unsupported platform-only properties remain visible in the completeness article with their boundary.</p><button type="button" className="text-button" onClick={() => { updatePreference("accent", defaults.accent); setCustomPanel({ x: 24, y: 24, width: 520 }); }}>Reset this editor</button></section> : null}

      {paletteOpen ? <div className="dialog-scrim" role="presentation"><section className="command-palette overlay-card" role="dialog" aria-modal="true" aria-label="Command palette"><div className="section-heading compact-heading"><div><p className="eyebrow">Command palette</p><h2>Go directly to a feature or setting</h2></div><button type="button" className="icon-button" aria-label="Close command palette" onClick={() => setPaletteOpen(false)}>×</button></div><SearchField id="command-search" label="Search every command, destination, and setting" value={paletteQuery} onChange={setPaletteQuery} /><div className="command-results">{paletteCommands.map((command) => <button type="button" key={command.label} onClick={() => { command.action(); setPaletteOpen(false); }}><span>{command.label}</span><span>Open</span></button>)}</div></section></div> : null}

      {lockedPrompt ? <div className="dialog-scrim"><section className="unlock-dialog overlay-card" role="dialog" aria-modal="true" aria-labelledby="unlock-title"><p aria-hidden="true" className="dialog-emoji">{preferences.showDialogEmoji ? "🔒" : ""}</p><h2 id="unlock-title">Unlock {tabs.find((tab) => tab.id === lockedPrompt)?.en}</h2><p>This is a local toy lock, not a security boundary. Clear this site’s browser storage if the password is forgotten.</p><label>Password<input type="password" value={unlockText} onChange={(event) => setUnlockText(event.target.value)} onKeyDown={(event: KeyboardEvent<HTMLInputElement>) => event.key === "Enter" && unlock()} /></label><div className="button-row"><button type="button" className="filled-button" onClick={unlock}>Unlock for 15 minutes</button><button type="button" className="text-button" onClick={() => { setLockedPrompt(null); setUnlockText(""); }}>Emergency exit</button></div></section></div> : null}

      <aside className="toast-stack" aria-live="polite" aria-label="Recent notifications">{notifications.slice(0, 3).map((notice) => <article className={`toast toast-${notice.tone}`} key={notice.id}><div><strong>{preferences.showDialogEmoji ? notice.tone === "success" ? "✅ " : notice.tone === "error" ? "⚠️ " : "ℹ️ " : ""}{notice.title}</strong><p>{notice.body}</p></div><button type="button" className="icon-button" aria-label={`Dismiss ${notice.title}`} onClick={() => setNotifications((current) => current.filter((item) => item.id !== notice.id))}>×</button></article>)}</aside>
    </div>
  );
}

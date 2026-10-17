const stream = document.getElementById("stream");
const input = document.getElementById("input");
const sendBtn = document.getElementById("send");
const micBtn = document.getElementById("mic");
const attachBtn = document.getElementById("attach");
const fileInput = document.getElementById("file");
const docType = document.getElementById("docType");
const uaeBtn = document.getElementById("uaeBtn");
const filesEl = document.getElementById("files");
let conversationId = localStorage.getItem("edeConv");
let recognizing = false;
let recognition;

function md(text) {
  return text
    .replace(/&/g, "&amp;").replace(/</g, "&lt;")
    .replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>")
    .replace(/`([^`]+)`/g, "<code>$1</code>")
    .replace(/\n/g, "<br>");
}
function bubble(role, text, mode) {
  const el = document.createElement("div");
  el.className = "msg " + role;
  const who = role === "assistant" ? "EDE AI" : (mode === "voice" ? "You · voice" : "You");
  el.innerHTML = `<span class="who">${who}</span>${md(text || "")}`;
  stream.appendChild(el);
  stream.scrollTop = stream.scrollHeight;
}
function setComposerEnabled(on) {
  input.disabled = !on;
  sendBtn.disabled = !on;
  micBtn.disabled = !on;
  attachBtn.disabled = !on;
  input.placeholder = on
    ? "Send all required details in one message, or use the microphone…"
    : "Sign in with UAE PASS to start the checklist…";
}
function render(data) {
  stream.innerHTML = "";
  for (const m of data.messages || []) bubble(m.role, m.text, m.inputMode);
  filesEl.innerHTML = (data.documents || []).map(d => `<span class="chip">${d.documentType}: ${d.fileName}</span>`).join("");
  setComposerEnabled(!!data.uaePassSignedIn);
  if (data.uaePassSignedIn) {
    uaeBtn.disabled = true;
    uaeBtn.querySelector(".full").textContent = "UAE PASS verified";
  }
  conversationId = data.conversationId;
  localStorage.setItem("edeConv", conversationId);
}

async function api(url, opts) {
  const res = await fetch(url, opts);
  if (!res.ok) throw new Error(await res.text());
  return res.json();
}
async function boot() {
  setComposerEnabled(false);
  try {
    const h = await api("/api/health");
    document.getElementById("regStatus").textContent = `${h.register.toLocaleString()} register packs`;
  } catch { document.getElementById("regStatus").textContent = "Register unavailable"; }
  try {
    if (conversationId) render(await api("/api/chat/" + conversationId));
    else render(await api("/api/chat/start", { method: "POST" }));
  } catch {
    localStorage.removeItem("edeConv");
    render(await api("/api/chat/start", { method: "POST" }));
  }
}
async function send(text, mode) {
  const value = (text ?? input.value).trim();
  if (!value) return;
  input.value = "";
  bubble("user", value, mode || "text");
  const data = await api(`/api/chat/${conversationId}/message`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ text: value, inputMode: mode || "text" })
  });
  render(data);
}
uaeBtn.onclick = async () => render(await api(`/api/chat/${conversationId}/signin`, { method: "POST" }));
sendBtn.onclick = () => send();
input.addEventListener("keydown", e => {
  if (e.key === "Enter" && !e.shiftKey) { e.preventDefault(); send(); }
});
attachBtn.onclick = () => fileInput.click();
fileInput.onchange = async () => {
  const file = fileInput.files[0];
  if (!file) return;
  const form = new FormData();
  form.append("file", file);
  form.append("documentType", docType.value);
  bubble("user", `Uploaded ${file.name} (${docType.value})`);
  render(await api(`/api/chat/${conversationId}/upload`, { method: "POST", body: form }));
  fileInput.value = "";
};
micBtn.onclick = () => {
  const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
  if (!SR) { alert("This browser has no speech recognition. Type instead, or set OPENAI_API_KEY for Whisper."); return; }
  if (!recognition) {
    recognition = new SR();
    recognition.lang = "en-AE";
    recognition.interimResults = false;
    recognition.onresult = e => send(e.results[0][0].transcript, "voice");
    recognition.onend = () => { recognizing = false; micBtn.classList.remove("live"); micBtn.textContent = "Voice"; };
  }
  if (recognizing) { recognition.stop(); return; }
  recognizing = true;
  micBtn.classList.add("live");
  micBtn.textContent = "Listening…";
  recognition.start();
};
boot();

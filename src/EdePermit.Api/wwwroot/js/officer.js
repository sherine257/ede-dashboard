const loginView = document.getElementById("loginView");
const portal = document.getElementById("portal");
const queueEl = document.getElementById("queue");
const caseEl = document.getElementById("case");
const analyticsView = document.getElementById("analyticsView");
const queueView = document.getElementById("queueView");
let currentId = null;

async function api(url, opts) {
  const res = await fetch(url, { credentials: "same-origin", ...opts });
  if (res.status === 401) throw new Error("unauthorized");
  if (!res.ok) throw new Error(await res.text());
  if (res.status === 204) return null;
  return res.json();
}
function badge(status) {
  const warn = /review|follow|mixed|correction/i.test(status);
  const ok = /complete|auto/i.test(status);
  return `<span class="badge ${ok ? "ok" : warn ? "warn" : "bad"}">${status}</span>`;
}
async function ensureSession() {
  try {
    await api("/api/officer/session");
    loginView.style.display = "none";
    portal.style.display = "flex";
    await loadQueue();
    return true;
  } catch {
    loginView.style.display = "block";
    portal.style.display = "none";
    return false;
  }
}
document.getElementById("loginBtn").onclick = async () => {
  try {
    await api("/api/officer/login", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ password: document.getElementById("password").value })
    });
    document.getElementById("loginErr").textContent = "";
    await ensureSession();
  } catch {
    document.getElementById("loginErr").textContent = "Invalid password.";
  }
};
document.getElementById("logout").onclick = async () => {
  await api("/api/officer/logout", { method: "POST" });
  await ensureSession();
};
document.getElementById("tabQueue").onclick = () => {
  document.getElementById("tabQueue").classList.add("on");
  document.getElementById("tabAnalytics").classList.remove("on");
  queueView.style.display = "grid";
  analyticsView.style.display = "none";
};
document.getElementById("tabAnalytics").onclick = async () => {
  document.getElementById("tabAnalytics").classList.add("on");
  document.getElementById("tabQueue").classList.remove("on");
  queueView.style.display = "none";
  analyticsView.style.display = "block";
  await loadAnalytics();
};

async function loadQueue() {
  const rows = await api("/api/officer/requests");
  queueEl.innerHTML = rows.map(r => `
    <div class="q-item ${r.id === currentId ? "active" : ""}" data-id="${r.id}">
      <b>${r.applicationNumber}</b>
      <small>${r.applicantName || "Unknown"} · ${r.classification || ""} · ${r.unmatched} unmatched</small>
      <div>${badge(r.status)}</div>
    </div>`).join("") || "<div class='q-item'>No requests yet.</div>";
  queueEl.querySelectorAll(".q-item[data-id]").forEach(el => el.onclick = () => openCase(el.dataset.id));
  if (!currentId && rows[0]) openCase(rows[0].id);
}
async function openCase(id) {
  currentId = id;
  const d = await api("/api/officer/requests/" + id);
  const v = d.request.validation || { medicines: [], findings: [], missingFields: [] };
  const chat = (d.messages || []).map(m => `<div class="msg ${m.role}" style="max-width:100%"><span class="who">${m.role} · ${m.inputMode}</span>${m.text.replace(/</g,"&lt;").replace(/\*\*(.+?)\*\*/g,"<strong>$1</strong>").replace(/\n/g,"<br>")}</div>`).join("");
  const meds = (v.medicines || []).map(m => `
    <tr>
      <td><b>${m.name}</b><br><small>${m.activeIngredient}</small></td>
      <td>${m.quantity} ${m.unit} / ${m.durationDays}d</td>
      <td>${m.matched ? m.registryId : "No record found"}<br>${m.dispensingMode}</td>
      <td>${m.autoEligible ? badge("Auto") : badge("HITL")}<br>${(m.failedRules||[]).join("<br>")}</td>
    </tr>`).join("");
  const docs = (d.documents || []).map(x => `<a class="link" href="/api/officer/documents/${x.id}" target="_blank">${x.documentType}: ${x.fileName}</a>`).join(" ");
  const audit = (d.audit || []).map(a => `<div><small>${a.createdUtc} · ${a.actor} · ${a.action}</small><div>${a.detail || ""}</div></div>`).join("");
  caseEl.innerHTML = `
    <div style="display:flex;justify-content:space-between;gap:12px;align-items:center">
      <div><h2 style="margin:0">${d.request.applicationNumber}</h2><small>${d.request.applicantName} · ${d.request.classification}</small></div>
      ${badge(d.request.status)}
    </div>
    <div class="card"><h3>Extracted data</h3>
      <pre style="white-space:pre-wrap;font-size:12px">${JSON.stringify(d.request.snapshot, null, 2)}</pre>
    </div>
    <div class="card"><h3>Medicine checks against ALL_REG_PACKS.xlsx</h3>
      <table><thead><tr><th>Medicine</th><th>Supply</th><th>Register</th><th>Rules</th></tr></thead><tbody>${meds || "<tr><td colspan=4>None</td></tr>"}</tbody></table>
    </div>
    <div class="card"><h3>Validation findings</h3>${(v.findings||[]).map(f => `<div>${f.ruleId}: ${f.message}</div>`).join("") || "—"}</div>
    <div class="card"><h3>Documents</h3>${docs || "None uploaded"}</div>
    <div class="card"><h3>Conversation</h3>${chat}</div>
    <div class="card"><h3>Audit history</h3>${audit || "—"}</div>
    <div class="card"><h3>Officer decision</h3>
      <textarea id="notes" style="width:100%;min-height:70px;border:1px solid var(--line);border-radius:8px">${d.request.officerNotes || ""}</textarea>
      <div style="display:flex;gap:8px;margin-top:8px">
        <button class="icon" data-act="correction">Request correction</button>
        <button class="icon" data-act="reject">Reject</button>
        <button class="send" data-act="approve">Approve and complete</button>
      </div>
    </div>`;
  caseEl.querySelectorAll("[data-act]").forEach(btn => btn.onclick = async () => {
    await api(`/api/officer/requests/${id}/decision`, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ decision: btn.dataset.act, notes: document.getElementById("notes").value })
    });
    await loadQueue();
    await openCase(id);
  });
  [...queueEl.querySelectorAll(".q-item")].forEach(el => el.classList.toggle("active", el.dataset.id === id));
}
async function loadAnalytics() {
  const a = await api("/api/officer/analytics");
  analyticsView.innerHTML = `
    <h2>Officer analytics</h2>
    <div class="kpis">
      <div class="kpi"><strong>${a.totalRequests}</strong><small>Total requests</small></div>
      <div class="kpi"><strong>${a.outcomes.officerReview}</strong><small>Officer review</small></div>
      <div class="kpi"><strong>${a.medicineValidation.matched}/${a.medicineValidation.total}</strong><small>Matched medicines</small></div>
      <div class="kpi"><strong>${a.inputModes.voice}</strong><small>Voice messages</small></div>
    </div>
    <div class="card"><h3>Status</h3><div class="chart-box"><canvas id="statusChart"></canvas></div></div>
    <div class="card"><h3>Request trend</h3><div class="chart-box"><canvas id="trendChart"></canvas></div></div>
    <div class="card"><h3>Medicine validation</h3><div class="chart-box"><canvas id="medChart"></canvas></div></div>
    <div class="card"><h3>Voice vs text</h3><div class="chart-box"><canvas id="modeChart"></canvas></div></div>`;
  const colors = ["#0d293c","#3d8080","#a65d05","#157347","#b33a3a","#5b3d91"];
  const fit = { responsive: true, maintainAspectRatio: false };
  new Chart(document.getElementById("statusChart"), {
    type: "doughnut",
    options: fit,
    data: { labels: Object.keys(a.byStatus), datasets: [{ data: Object.values(a.byStatus), backgroundColor: colors }] }
  });
  new Chart(document.getElementById("trendChart"), {
    type: "line",
    options: { ...fit, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } },
    data: { labels: a.trend.map(x => x.date), datasets: [{ label: "Requests", data: a.trend.map(x => x.count), borderColor: "#3d8080" }] }
  });
  new Chart(document.getElementById("medChart"), {
    type: "bar",
    options: { ...fit, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } },
    data: { labels: ["Matched","Unmatched","Controlled","Injectable"], datasets: [{ label: "Medicines", data: [a.medicineValidation.matched, a.medicineValidation.unmatched, a.medicineValidation.controlled, a.medicineValidation.injectable], backgroundColor: "#0d293c" }] }
  });
  new Chart(document.getElementById("modeChart"), {
    type: "pie",
    options: fit,
    data: { labels: ["Text","Voice"], datasets: [{ data: [a.inputModes.text, a.inputModes.voice], backgroundColor: ["#2e435b","#3d8080"] }] }
  });
}
ensureSession();

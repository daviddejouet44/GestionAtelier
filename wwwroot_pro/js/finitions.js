// finitions.js — Standalone page for finitions profile (profile 4) to manage finition steps
// Accessed via QR code on the PDF fiche: /pro/finitions.html?job=<fileName>

const STEP_LABELS = {
  embellissement: "✨ Embellissement",
  rainage:        "📐 Rainage",
  pliage:         "📄 Pliage",
  faconnage:      "⚙️ Façonnage",
  coupe:          "✂️ Coupe",
  emballage:      "📦 Emballage",
  depart:         "🚪 Départ",
  livraison:      "🚚 Livraison"
};
const STEP_ORDER = ["embellissement","rainage","pliage","faconnage","coupe","emballage","depart","livraison"];

let authToken = null;
let currentUser = null;

function esc(s) { return String(s).replace(/&/g,'&amp;').replace(/</g,'&lt;').replace(/>/g,'&gt;').replace(/"/g,'&quot;'); }

function getParams() {
  const p = new URLSearchParams(window.location.search);
  return { job: p.get("job") || "" };
}

function getTokenFromStorage() {
  return localStorage.getItem("authToken") || sessionStorage.getItem("authToken") || null;
}

async function parseToken(token) {
  try {
    const decoded = atob(token);
    const parts = decoded.split(':');
    if (parts.length < 3) return null;
    return { id: parts[0], login: parts[1], profile: parseInt(parts[2]) };
  } catch { return null; }
}

async function doLogin(login, pwd) {
  const r = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ login, password: pwd })
  }).then(r => r.json()).catch(() => ({ ok: false }));
  if (r.ok && r.token) {
    localStorage.setItem("authToken", r.token);
    return r.token;
  }
  return null;
}

async function loadFinitions(job) {
  const main = document.getElementById("main-section");
  main.innerHTML = '<div id="loading">Chargement des étapes…</div>';
  main.style.display = "block";

  const jobId = encodeURIComponent(job);

  try {
    const [stepsData, fabData] = await Promise.all([
      fetch(`/api/fabrication/${jobId}/finition-steps`, {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({ ok: false })),
      fetch(`/api/fabrication?fileName=${encodeURIComponent(job)}`, {
        headers: { "Authorization": `Bearer ${authToken}` }
      }).then(r => r.json()).catch(() => ({}))
    ]);

    const steps = stepsData.ok ? stepsData.finitionSteps : {};
    const hasEnnob    = Array.isArray(fabData.ennoblissement) && fabData.ennoblissement.length > 0;
    const hasRainage  = fabData.rainage === true;
    const hasPlis     = fabData.plis && fabData.plis.trim() !== "";
    const hasFaconnage = (fabData.faconnageBinding && fabData.faconnageBinding.trim() !== "") ||
                         (Array.isArray(fabData.faconnage) && fabData.faconnage.length > 0);
    const applicable = {
      embellissement: hasEnnob,
      rainage: hasRainage,
      pliage: hasPlis,
      faconnage: hasFaconnage,
      coupe: false,
      emballage: true,
      depart: true,
      livraison: true
    };

    const applicableKeys = STEP_ORDER.filter(k => applicable[k]);
    const doneCount = applicableKeys.filter(k => steps[k]?.done).length;
    const totalCount = applicableKeys.length;
    const pct = totalCount > 0 ? Math.round(doneCount * 100 / totalCount) : 0;

    // Only profiles 3 (admin) and 4 (finitions) can edit; profile 2 can view
    const canEdit = currentUser && (currentUser.profile === 3 || currentUser.profile === 4);

    main.innerHTML = `
      <h1>✂️ Étapes de finition</h1>
      <p class="subtitle">Connecté en tant que <strong>${esc(currentUser?.login || "")}</strong> — Profil ${currentUser?.profile || "?"}</p>
      <div class="dossier-info">
        <strong>Dossier :</strong> ${esc(fabData.numeroDossier || job)}<br/>
        ${fabData.client ? `<strong>Client :</strong> ${esc(fabData.client)}` : ""}
      </div>
      <div class="progress-bar-wrap"><div class="progress-bar" style="width:${pct}%"></div></div>
      <p style="font-size:13px;color:#6b7280;margin-bottom:12px;">${doneCount} / ${totalCount} étape(s) complétée(s)</p>
      <div id="steps-list"></div>
      ${!canEdit ? '<p style="color:#9ca3af;font-size:12px;margin-top:12px;">Accès lecture seule.</p>' : ''}
      <button class="btn-sm" id="btn-refresh" style="margin-top:16px;width:100%;">🔄 Actualiser</button>
      <button class="btn-sm" id="btn-logout" style="margin-top:8px;width:100%;color:#ef4444;border-color:#ef4444;">Déconnexion</button>
    `;

    const stepsList = main.querySelector("#steps-list");
    let previousStepDone = true;

    STEP_ORDER.forEach((key) => {
      if (!applicable[key]) return;
      const stepData = steps[key] || {};
      const isDone = stepData.done === true;
      const locked = !canEdit || !previousStepDone;

      const row = document.createElement("div");
      row.className = "step-row" + (isDone ? " done" : "") + (locked && !isDone ? " locked" : "");

      const cb = document.createElement("input");
      cb.type = "checkbox";
      cb.checked = isDone;
      cb.disabled = locked;

      const labelDiv = document.createElement("div");
      labelDiv.style.flex = "1";

      const nameEl = document.createElement("div");
      nameEl.className = "step-name" + (isDone ? " done-text" : "") + (!isDone && !previousStepDone ? " locked-text" : "");
      nameEl.textContent = STEP_LABELS[key];
      if (locked && !isDone) nameEl.title = "Validez l'étape précédente d'abord";
      labelDiv.appendChild(nameEl);

      if (key === "faconnage") {
        const subOptions = [];
        if (Array.isArray(fabData.faconnage) && fabData.faconnage.length > 0) subOptions.push(...fabData.faconnage);
        if (fabData.faconnageBinding?.trim()) subOptions.push(fabData.faconnageBinding.trim());
        if (subOptions.length > 0) {
          const sub = document.createElement("div");
          sub.className = "step-sub";
          sub.textContent = "Options : " + subOptions.join(", ");
          labelDiv.appendChild(sub);
        }
      }

      if (isDone && stepData.doneAt) {
        const meta = document.createElement("div");
        meta.className = "step-meta";
        const dt = new Date(stepData.doneAt);
        meta.textContent = `${stepData.doneBy || ""} — ${dt.toLocaleDateString('fr-FR')} ${dt.toLocaleTimeString('fr-FR', { hour: '2-digit', minute: '2-digit' })}`;
        labelDiv.appendChild(meta);
      }
      if (key === "emballage" && isDone && stepData.conditionnement) {
        const cond = document.createElement("div");
        cond.className = "step-sub";
        cond.textContent = `Conditionnement : ${stepData.conditionnement}`;
        labelDiv.appendChild(cond);
      }
      if (key === "livraison" && isDone && stepData.tracking) {
        const trk = document.createElement("div");
        trk.className = "step-sub";
        trk.textContent = `Suivi : ${stepData.tracking}`;
        labelDiv.appendChild(trk);
      }

      if (!locked) {
        cb.onchange = async () => {
          const newDone = cb.checked;
          let conditionnement = null;
          let tracking = null;

          if (key === "emballage" && newDone) {
            conditionnement = prompt("Conditionnement (obligatoire) :");
            if (!conditionnement || conditionnement.trim() === "") { cb.checked = false; return; }
          }
          if (key === "livraison" && newDone) {
            tracking = prompt("Numéro de suivi (optionnel) :");
          }

          const body = { step: key, done: newDone };
          if (conditionnement) body.conditionnement = conditionnement;
          if (tracking) body.tracking = tracking;

          try {
            const r = await fetch(`/api/fabrication/${jobId}/finition-step`, {
              method: "PUT",
              headers: { "Content-Type": "application/json", "Authorization": `Bearer ${authToken}` },
              body: JSON.stringify(body)
            }).then(r => r.json()).catch(() => ({ ok: false }));

            if (r.ok) {
              await loadFinitions(job);
            } else {
              cb.checked = !newDone;
              alert("❌ " + (r.error || "Erreur lors de la mise à jour"));
            }
          } catch {
            cb.checked = !newDone;
            alert("❌ Erreur réseau");
          }
        };
      }

      row.appendChild(cb);
      row.appendChild(labelDiv);
      stepsList.appendChild(row);

      previousStepDone = isDone;
    });

    main.querySelector("#btn-refresh").onclick = () => loadFinitions(job);
    main.querySelector("#btn-logout").onclick = () => {
      localStorage.removeItem("authToken");
      sessionStorage.removeItem("authToken");
      window.location.reload();
    };

  } catch(err) {
    main.innerHTML = `<div style="color:#dc2626;">Erreur : ${esc(err.message)}</div>`;
  }
}

async function init() {
  const loadingEl = document.getElementById("loading");
  const loginSection = document.getElementById("login-section");
  const mainSection = document.getElementById("main-section");

  const { job } = getParams();

  // Try existing token
  const storedToken = getTokenFromStorage();
  if (storedToken) {
    const user = await parseToken(storedToken);
    if (user) {
      // Verify token is still valid
      try {
        const r = await fetch("/api/auth/me", {
          headers: { "Authorization": `Bearer ${storedToken}` }
        }).then(r => r.json()).catch(() => ({ ok: false }));
        if (r.ok) {
          authToken = storedToken;
          currentUser = user;
          loadingEl.style.display = "none";

          if (currentUser.profile !== 3 && currentUser.profile !== 4 && currentUser.profile !== 2) {
            mainSection.innerHTML = '<div id="access-denied">⛔ Accès non autorisé. Ce QR code est réservé aux opérateurs Finitions.</div>';
            mainSection.style.display = "block";
            return;
          }

          if (!job) {
            mainSection.innerHTML = '<div style="color:#9ca3af;font-size:14px;">Aucun dossier spécifié dans l\'URL (paramètre <code>?job=…</code> manquant).</div>';
            mainSection.style.display = "block";
            return;
          }

          await loadFinitions(job);
          return;
        }
      } catch { /* fall through to login */ }
    }
  }

  // Show login form
  loadingEl.style.display = "none";
  loginSection.style.display = "block";

  document.getElementById("login-btn").onclick = async () => {
    const login = document.getElementById("login-input").value.trim();
    const pwd = document.getElementById("pwd-input").value;
    const errEl = document.getElementById("error-msg");
    errEl.textContent = "";
    if (!login || !pwd) { errEl.textContent = "Login et mot de passe requis."; return; }

    const token = await doLogin(login, pwd);
    if (!token) { errEl.textContent = "Identifiants incorrects."; return; }

    const user = await parseToken(token);
    if (!user) { errEl.textContent = "Erreur de décodage du token."; return; }

    if (user.profile !== 3 && user.profile !== 4 && user.profile !== 2) {
      errEl.textContent = "⛔ Accès non autorisé. Ce QR code est réservé aux opérateurs Finitions.";
      return;
    }

    authToken = token;
    currentUser = user;
    loginSection.style.display = "none";

    if (!job) {
      mainSection.innerHTML = '<div style="color:#9ca3af;font-size:14px;">Aucun dossier spécifié dans l\'URL (paramètre <code>?job=…</code> manquant).</div>';
      mainSection.style.display = "block";
      return;
    }

    await loadFinitions(job);
  };

  document.getElementById("pwd-input").onkeydown = (e) => {
    if (e.key === "Enter") document.getElementById("login-btn").click();
  };
}

init();

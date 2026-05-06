// fabrication.js — Fiche de fabrication (formulaire dynamique)
import { authToken, deliveriesByPath, fnKey, normalizePath, showNotification, FIN_PROD_FOLDER, currentUser } from './core.js';
import { calendar, submissionCalendar } from './calendar.js';

// Fixed DOM refs
const fabModal = document.getElementById("fab-modal");
const fabClose = document.getElementById("fab-close");
const fabSave = document.getElementById("fab-save");
const fabPdf = document.getElementById("fab-pdf");
const fabFinProd = document.getElementById("fab-finprod");
const fabPrisma = document.getElementById("fab-prisma");
const fabStageBanner = document.getElementById("fab-stage-banner");
const fabHistory = document.getElementById("fab-history");
const fabRemove = document.getElementById("fab-delivery-remove");
const fabDynamicForm = document.getElementById("fab-dynamic-form");

// Nouveaux champs dates clés et production
const fabDateReception = document.getElementById("fab-date-reception");
const fabDateEnvoi = document.getElementById("fab-date-envoi");
const fabDateImpression = document.getElementById("fab-date-impression");
const fabDateFinitions = document.getElementById("fab-date-finitions");
const fabTempsProduit = document.getElementById("fab-temps-produit");

export let fabCurrentPath = null;

// Cache
const _fabCache = {};
const FAB_CACHE_TTL = 5 * 60 * 1000;
let _formConfigCache = null;
let _formConfigCacheTs = 0;
window._invalidateFabFormConfig = () => { _formConfigCache = null; _formConfigCacheTs = 0; };

async function fetchCached(url) {
  const now = Date.now();
  if (_fabCache[url] && now - _fabCache[url].ts < FAB_CACHE_TTL) return _fabCache[url].data;
  const data = await fetch(url).then(r => r.json()).catch(() => []);
  _fabCache[url] = { data, ts: now };
  return data;
}

async function fetchFormConfig() {
  const now = Date.now();
  if (_formConfigCache && now - _formConfigCacheTs < FAB_CACHE_TTL) return _formConfigCache;
  try {
    const cfg = await fetch("/api/settings/form-config").then(r => r.json());
    _formConfigCache = cfg; _formConfigCacheTs = now; return cfg;
  } catch(e) { return null; }
}

// Field ID to HTML element ID mapping
const FIELD_HTML_IDS = {
  "numeroDossier":         "fab-numero-dossier",
  "client":                "fab-client",
  "operateur":             "fab-operateur",
  "delai":                 "fab-delai",
  "typeTravail":           "fab-type",
  "formatFini":            "fab-format",
  "quantite":              "fab-quantite",
  "moteurImpression":      "fab-moteur",
  "donneurOrdreNom":       "fab-donneur-nom",
  "donneurOrdrePrenom":    "fab-donneur-prenom",
  "donneurOrdreTelephone": "fab-donneur-tel",
  "donneurOrdreEmail":     "fab-donneur-email",
  "rectoVerso":            "fab-recto-verso",
  "formeDecoupe":          "fab-forme-decoupe",
  "pagination":            "fab-pagination",
  "formatFeuilleMachine":  "fab-format-feuille",
  "media1":                "fab-media1",
  "media1Fabricant":       "fab-media1-fabricant",
  "media2":                "fab-media2",
  "media2Fabricant":       "fab-media2-fabricant",
  "media3":                "fab-media3",
  "media3Fabricant":       "fab-media3-fabricant",
  "media4":                "fab-media4",
  "media4Fabricant":       "fab-media4-fabricant",
  "couvertureMedia":       "fab-media-couverture",
  "couvertureFabricant":   "fab-media-couverture-fabricant",
  "bat":                   "fab-bat",
  "mailValidationBat":     "fab-mail-bat",
  "mailValidationDevis":   "fab-mail-devis",
  "rainage":               "fab-rainage",
  "ennoblissement":        "fab-ennoblissement-container",
  "faconnageBinding":      "fab-faconnage-binding",
  "plis":                  "fab-plis",
  "sortie":                "fab-sortie",
  "nombreFeuilles":        "fab-nombre-feuilles",
  "dateDepart":            "fab-date-depart",
  "dateLivraison":         "fab-date-livraison",
  "planningMachine":       "fab-planning-machine",
  "passes":                "fab-passes-display",
  "retraitLivraison":      "fab-retrait-livraison",
  "adresseLivraison":      "fab-adresse-livraison",
  "justifsQuantite":       "fab-justifs-qte",
  "justifsAdresse":        "fab-justifs-adresse",
  "repartitions":          "fab-repartitions-container",
  "notes":                 "fab-notes",
  "dateReception":              "fab-date-reception",
  "dateEnvoi":                  "fab-date-envoi",
  "dateProductionFinitions":    "fab-date-finitions",
  "dateImpression":             "fab-date-impression",
  "tempsProduitMinutes":        "fab-temps-produit",
  // ── Process d'impression ─────────────────────────────────────────────────
  "process":                    "fab-process",
  "bascule":                    "fab-bascule",
  "couleurs":                   "fab-couleurs",
  "couleursAccompagnement":     "fab-couleurs-accompagnement",
};
function gElId(id) { return FIELD_HTML_IDS[id] || ('fab-' + id); }
function gEl(id) { return document.getElementById(gElId(id)); }
function fmtDate2(v) {
  try {
    if (!v) return '';
    const s = String(v);
    // Extract the date part directly from ISO strings to avoid timezone conversion:
    // "2026-01-15T00:00:00Z", "2026-01-15T02:00:00+02:00", "2026-01-15" → "2026-01-15"
    if (/^\d{4}-\d{2}-\d{2}/.test(s)) return s.slice(0, 10);
    return new Date(v).toISOString().split('T')[0];
  } catch(e) { return ''; }
}

// Settings vars
let _coverProducts = [];
let _sheetCalcRules = {};
let _deliveryDelayHours = 48;
let _passesConfig = { faconnage:0, pelliculageRecto:0, pelliculageRectoVerso:0, rainage:0, dorure:0, dosCarreColle:0 };
let _keyDatesConfig = {
  livraison: { sendOffsetHours: 48, finitionsOffsetHours: 72, impressionOffsetHours: 96 },
  retrait: { sendOffsetHours: 0, finitionsOffsetHours: 24, impressionOffsetHours: 48 }
};
let _grammageTimeRules = [];
let _jdfEnabled = false;

function updateRainageAuto() {
  const couv = gEl("couvertureMedia"); const rain = gEl("rainage"); const lbl = document.getElementById("fab-rainage-label");
  if (!couv || !rain) return;
  const m = (couv.value || '').match(/(\d+)\s*g/i);
  if (m && parseInt(m[1]) > 170) { rain.checked=true; rain.disabled=true; if(lbl) lbl.textContent='Oui (auto)'; }
  else { rain.disabled=false; if(lbl) lbl.textContent=rain.checked?'Oui':'Non'; }
}

function updateCouvertureVisibility() {
  const typeEl = gEl("typeTravail");
  const show = typeEl ? _coverProducts.includes(typeEl.value) : false;
  if (fabDynamicForm) {
    fabDynamicForm.querySelectorAll('[data-depends-on="typeTravail"]:not([data-depends-on-value])').forEach(el => { el.style.display = show ? '' : 'none'; });
  }
}

// Generic: show/hide fields that depend on a specific value from another field
function updateDependsOnFields() {
  if (!fabDynamicForm) return;
  fabDynamicForm.querySelectorAll('[data-depends-on][data-depends-on-value]').forEach(wrap => {
    const dep = wrap.dataset.dependsOn;
    const depVal = wrap.dataset.dependsOnValue;
    const el = gEl(dep);
    if (!el) return;
    const currentVal = el.value || '';
    wrap.style.display = currentVal === depVal ? '' : 'none';
  });
}

function updateNombreFeuilles() {
  const typeEl=gEl("typeTravail"); const qteEl=gEl("quantite"); const nfEl=gEl("nombreFeuilles");
  const type=typeEl?typeEl.value:''; const qte=parseInt(qteEl?qteEl.value:'0')||0;
  if(type && _sheetCalcRules[type] && qte>0 && nfEl && !nfEl._manuallyEdited) nfEl.value=Math.ceil(qte/_sheetCalcRules[type]);
}

function updateDateLivraison() {
  const depEl=gEl("dateDepart"); const livEl=gEl("dateLivraison");
  if(!depEl||!livEl||!depEl.value) return;
  const lDate=new Date(new Date(depEl.value).getTime()+_deliveryDelayHours*3600000);
  if(!livEl._manuallyEdited) livEl.value=lDate.toISOString().split('T')[0];
}

function updateKeyDates() {
  const recEl=document.getElementById('fab-date-reception'); if(!recEl||!recEl.value) return;
  const recTs=new Date(recEl.value+'T00:00:00').getTime();
  const envEl=document.getElementById('fab-date-envoi');
  const finEl=document.getElementById('fab-date-finitions');
  const impEl=document.getElementById('fab-date-impression');
  // Use retrait offsets if "Retrait imprimerie" is selected, else livraison offsets
  const rlEl=document.getElementById('fab-retrait-livraison');
  const isRetrait=rlEl&&(rlEl.value||'').toLowerCase().includes('retrait');
  const cfg=isRetrait?_keyDatesConfig.retrait:_keyDatesConfig.livraison;
  // Helper: convert epoch ms to local YYYY-MM-DD (avoids UTC day-shift at DST boundaries)
  function _localDate(ts) {
    const d = new Date(ts);
    return d.getFullYear() + '-' + String(d.getMonth() + 1).padStart(2, '0') + '-' + String(d.getDate()).padStart(2, '0');
  }
  // Only fill a date field if it is empty OR not protected (not loaded from DB / not manually set).
  // This prevents overwriting dates entered by the user when another field (e.g. temps de production) changes.
  if(envEl&&!envEl.dataset.protected) envEl.value=_localDate(recTs-cfg.sendOffsetHours*3600000);
  if(finEl&&!finEl.dataset.protected) finEl.value=_localDate(recTs-cfg.finitionsOffsetHours*3600000);
  if(impEl&&!impEl.dataset.protected) impEl.value=_localDate(recTs-cfg.impressionOffsetHours*3600000);
}

function updateTempsProduction() {
  const motEl=gEl('moteurImpression'); const nfEl=gEl('nombreFeuilles'); const tpEl=document.getElementById('fab-temps-produit');
  if(!tpEl) return;
  // Don't overwrite if user manually set a value (marked by dataset.manual)
  if(tpEl.dataset.manual==='1') return;
  const moteur=(motEl?motEl.value:'').trim();
  const nf=parseInt(nfEl?nfEl.value:'0')||0;
  if(!moteur||!nf){tpEl.value='';return;}
  // Try to extract grammage from media1
  const m1El=gEl('media1'); const m1Val=m1El?m1El.value:'';
  const gMatch=m1Val.match(/(\d+)\s*g/i);
  const grammage=gMatch?parseInt(gMatch[1]):null;
  let timePerSheet=null;
  if(grammage!==null){
    const rule=_grammageTimeRules.find(r=>r.engineName===moteur&&grammage>=r.grammageMin&&grammage<=r.grammageMax);
    if(rule) timePerSheet=rule.timePerSheetSeconds;
  }
  if(timePerSheet===null){
    const rule=_grammageTimeRules.find(r=>r.engineName===moteur);
    if(rule) timePerSheet=rule.timePerSheetSeconds;
  }
  if(timePerSheet===null){tpEl.value='';return;}
  const totalSecs=nf*timePerSheet;
  const mins=Math.round(totalSecs/60);
  tpEl.value=mins;
}

function getEnnoblissementSelected() {
  const c=gEl("ennoblissement");
  if(!c) return [];
  const arr=[];
  // Vernis sélectif (radio)
  const vernisEl=c.querySelector('input[type="radio"][value="Vernis sélectif"]:checked');
  if(vernisEl) arr.push(vernisEl.value);
  // Dorure à chaud (radio)
  const dorureEl=c.querySelector('input[type="radio"][name*="fab-ennob-dorure"]:checked');
  if(dorureEl&&dorureEl.value) arr.push(dorureEl.value);
  // Pelliculage (checkboxes)
  c.querySelectorAll('.fab-ennob-pelliculage:checked').forEach(cb=>arr.push(cb.value));
  // Fallback: legacy flat checkboxes (e.g., older form configs still using fab-ennob-cb)
  c.querySelectorAll('.fab-ennob-cb:checked').forEach(cb=>{ if(!arr.includes(cb.value)) arr.push(cb.value); });
  return arr;
}

function updatePassesDisplay() {
  const disp=gEl("passes"); if(!disp) return;
  const ennob=getEnnoblissementSelected();
  const fb=(gEl("faconnageBinding")||{value:""}).value;
  const rv=(gEl("rainage")||{checked:false}).checked;
  const lines=[];
  if(fb && _passesConfig.faconnage>0) lines.push('Façonnage : +'+_passesConfig.faconnage+' feuilles');
  if(ennob.some(e=>e.includes('Pelliculage')&&e.includes('recto/verso'))&&_passesConfig.pelliculageRectoVerso>0) lines.push('Pelliculage recto/verso : +'+_passesConfig.pelliculageRectoVerso+' feuilles');
  else if(ennob.some(e=>e.includes('Pelliculage')&&e.includes('recto'))&&_passesConfig.pelliculageRecto>0) lines.push('Pelliculage recto : +'+_passesConfig.pelliculageRecto+' feuilles');
  if(rv&&_passesConfig.rainage>0) lines.push('Rainage : +'+_passesConfig.rainage+' feuilles');
  if(ennob.some(e=>e.includes('Dorure'))&&_passesConfig.dorure>0) lines.push('Dorure : +'+_passesConfig.dorure+' feuilles');
  if(fb==='Dos carré collé'&&_passesConfig.dosCarreColle>0) lines.push('Dos carré collé : +'+_passesConfig.dosCarreColle+' exemplaires');
  disp.innerHTML=lines.length>0
    ? lines.map(l=>'<span style="display:inline-block;background:#f3f4f6;padding:3px 8px;border-radius:4px;margin:2px;font-size:12px;">'+l+'</span>').join('')
    : '<span style="color:#9ca3af;font-size:12px;">Aucune passe applicable</span>';
}

function addRepartitionRow(quantite, adresse) {
  quantite=quantite||''; adresse=adresse||'';
  const container=gEl("repartitions"); if(!container) return;
  const row=document.createElement('div');
  row.className='fab-repartition-row';
  row.style.cssText='display:flex;gap:8px;align-items:flex-start;margin-bottom:6px;';
  row.innerHTML='<textarea class="fab-rep-adresse" placeholder="Adresse de livraison" rows="2" style="flex:1;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;resize:vertical;">'+adresse+'</textarea>'
    +'<input type="number" class="fab-rep-qte" placeholder="Qté" value="'+quantite+'" style="width:80px;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" min="0" />'
    +'<button class="fab-rep-remove btn" style="padding:4px 10px;font-size:12px;color:#ef4444;flex-shrink:0;">🗑️</button>';
  row.querySelector('.fab-rep-remove').onclick=()=>row.remove();
  container.appendChild(row);
}

function getRepartitions() {
  const container=gEl("repartitions"); if(!container) return [];
  return Array.from(container.querySelectorAll('.fab-repartition-row')).map(row=>({
    quantite:parseInt(row.querySelector('.fab-rep-qte').value)||null,
    adresse:row.querySelector('.fab-rep-adresse').value.trim()||null
  })).filter(r=>r.quantite||r.adresse);
}

const ENNOBLISSEMENT_OPTIONS=[
  'Vernis sélectif','Dorure à chaud : Or','Dorure à chaud : Argent',
  'Pelliculage : Mat recto','Pelliculage : Mat recto/verso',
  'Pelliculage : Brillant recto','Pelliculage : Brillant recto/verso',
  'Pelliculage : Soft Touch recto','Pelliculage : Soft Touch recto/verso',
];

function renderFabForm(config, opts) {
  if(!fabDynamicForm) return;
  fabDynamicForm.innerHTML='';
  const {engines=[],types=[],papers=[],sheetFormats=[],faconnageOptions=[],bindingOptions=[],foldsOptions=[],outputOptions=[]}=opts;
  const paperHtml='<option value="">— Sélectionner —</option>'+papers.map(p=>'<option value="'+p+'">'+p+'</option>').join('');
  const fields=(config.fields||[]).filter(f=>f.visible).sort((a,b)=>a.order-b.order);
  const sections=config.sections||[];
  const sectionMap={}; sections.forEach(s=>{sectionMap[s]=[];});
  fields.forEach(f=>{const sec=f.section||'_other';if(!sectionMap[sec])sectionMap[sec]=[];sectionMap[sec].push(f);});
  const orderedSections=[...sections,...Object.keys(sectionMap).filter(s=>!sections.includes(s))];

  orderedSections.forEach(section=>{
    const sf=sectionMap[section]; if(!sf||sf.length===0) return;
    const hdr=document.createElement('div');
    hdr.className='fab-form-group fab-full-width fab-section-header';
    hdr.innerHTML='<span>'+section+'</span>';
    fabDynamicForm.appendChild(hdr);

    sf.forEach(field=>{
      const isFull=field.width==='full';
      const wrap=document.createElement('div');
      wrap.className='fab-form-group'+(isFull?' fab-full-width':'');
      if(field.dependsOn){wrap.dataset.dependsOn=field.dependsOn;if(field.dependsOnValue)wrap.dataset.dependsOnValue=field.dependsOnValue;wrap.style.display='none';}
      const elId=gElId(field.id);
      const reqStar=field.required?' <span class="required-star">*</span>':'';
      const calcNote=field.calculationRule?' <small style="color:#9ca3af;font-size:10px;">(calculé)</small>':'';
      const roAttr=field.readOnly?' readonly style="background:#f3f4f6;color:#6b7280;cursor:not-allowed;"':'';
      const roSel=field.readOnly?' disabled style="background:#f3f4f6;color:#6b7280;"':'';
      const selPH='<option value="">— Sélectionner —</option>';

      if(field.type==='text'||field.type==='number'){
        wrap.innerHTML='<label>'+field.label+reqStar+calcNote+'</label><input id="'+elId+'" type="'+field.type+'"'+roAttr+' />';
      } else if(field.type==='date'){
        wrap.innerHTML='<label>'+field.label+reqStar+calcNote+'</label><input id="'+elId+'" type="date"'+roAttr+' />';
      } else if(field.type==='textarea'){
        wrap.innerHTML='<label>'+field.label+'</label><textarea id="'+elId+'" rows="3"></textarea>';
      } else if(field.type==='select'){
        let optHtml=selPH;
        if(field.id==='moteurImpression') optHtml+=engines.map(e=>{const n=typeof e==='object'?(e.name||''):String(e||'');return '<option value="'+n+'">'+n+'</option>';}).join('');
        else if(field.id==='typeTravail') optHtml+=types.map(t=>'<option value="'+t+'">'+t+'</option>').join('');
        else if(['media1','media2','media3','media4','couvertureMedia'].includes(field.id)) optHtml=paperHtml;
        else if(field.id==='formatFeuilleMachine') optHtml+=sheetFormats.map(f2=>'<option value="'+f2+'">'+f2+'</option>').join('');
        else if(field.id==='faconnageBinding') optHtml+=(bindingOptions.length?bindingOptions:field.options||[]).map(o=>'<option value="'+o+'">'+o+'</option>').join('');
        else if(field.id==='plis') optHtml+=(foldsOptions.length?foldsOptions:field.options||[]).map(o=>'<option value="'+o+'">'+o+'</option>').join('');
        else if(field.id==='sortie') optHtml+=(outputOptions.length?outputOptions:field.options||[]).map(o=>'<option value="'+o+'">'+o+'</option>').join('');
        else if(Array.isArray(field.options)) optHtml+=field.options.map(o=>'<option value="'+o+'">'+o+'</option>').join('');
        wrap.innerHTML='<label>'+field.label+reqStar+'</label><select id="'+elId+'"'+roSel+'>'+optHtml+'</select>';
      } else if(field.type==='multiselect'){
        // Structured finitions UI: Vernis sélectif (radio), Dorure à chaud (radio), Pelliculage (checkboxes)
        const PELLICULAGE_OPTS = [
          ['Pelliculage : Mat recto',           'Mat recto'],
          ['Pelliculage : Mat recto/verso',     'Mat recto/verso'],
          ['Pelliculage : Brillant recto',      'Brillant recto'],
          ['Pelliculage : Brillant recto/verso','Brillant recto/verso'],
          ['Pelliculage : Soft Touch recto',    'Soft Touch recto'],
          ['Pelliculage : Soft Touch recto/verso','Soft Touch recto/verso'],
        ];
        const radioStyle='display:inline-flex;align-items:center;gap:5px;padding:4px 10px;background:#f3f4f6;border-radius:6px;font-size:13px;cursor:pointer;border:1px solid #e5e7eb;';
        const cbStyle='display:inline-flex;align-items:center;gap:5px;padding:4px 10px;background:#f3f4f6;border-radius:6px;font-size:13px;cursor:pointer;border:1px solid #e5e7eb;';
        const vernisHtml='<div style="margin-bottom:8px;">'
          +'<span style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Vernis sélectif</span>'
          +'<div style="display:flex;flex-wrap:wrap;gap:6px;">'
          +'<label style="'+radioStyle+'"><input type="radio" name="fab-ennob-vernis-'+elId+'" value="" checked /> Non</label>'
          +'<label style="'+radioStyle+'"><input type="radio" name="fab-ennob-vernis-'+elId+'" value="Vernis sélectif" /> Oui</label>'
          +'</div></div>';
        const dorureHtml='<div style="margin-bottom:8px;">'
          +'<span style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Dorure à chaud</span>'
          +'<div style="display:flex;flex-wrap:wrap;gap:6px;">'
          +'<label style="'+radioStyle+'"><input type="radio" name="fab-ennob-dorure-'+elId+'" value="" checked /> Non</label>'
          +'<label style="'+radioStyle+'"><input type="radio" name="fab-ennob-dorure-'+elId+'" value="Dorure à chaud : Or" /> Or</label>'
          +'<label style="'+radioStyle+'"><input type="radio" name="fab-ennob-dorure-'+elId+'" value="Dorure à chaud : Argent" /> Argent</label>'
          +'</div></div>';
        const pellHtml='<div>'
          +'<span style="font-size:12px;font-weight:600;color:#374151;display:block;margin-bottom:4px;">Pelliculage <small style="font-weight:400;color:#6b7280;">(plusieurs choix possibles)</small></span>'
          +'<div style="display:flex;flex-wrap:wrap;gap:6px;">'
          +PELLICULAGE_OPTS.map(([val,lbl])=>'<label style="'+cbStyle+'"><input type="checkbox" class="fab-ennob-pelliculage" value="'+val+'" /> '+lbl+'</label>').join('')
          +'</div></div>';
        wrap.innerHTML='<label>'+field.label+'</label><div id="'+elId+'" style="padding:6px 0;">'+vernisHtml+dorureHtml+pellHtml+'</div>';
      } else if(field.type==='checkbox'){
        wrap.innerHTML='<label>'+field.label+'</label><label style="display:inline-flex;align-items:center;gap:8px;cursor:pointer;"><input id="'+elId+'" type="checkbox" style="width:16px;height:16px;" /><span id="fab-rainage-label">Non</span></label>';
      } else if(field.type==='file-import'){
        const btnId=field.id==='mailValidationBat'?'fab-import-mail-bat':'fab-import-mail-devis';
        const fileId=field.id==='mailValidationBat'?'fab-mail-bat-file':'fab-mail-devis-file';
        const nameId=field.id==='mailValidationBat'?'fab-mail-bat-name':'fab-mail-devis-name';
        wrap.innerHTML='<label>'+field.label+'</label><div id="'+elId+'" style="display:flex;align-items:center;gap:8px;"><button id="'+btnId+'" class="btn" style="font-size:12px;padding:4px 10px;">📎 Importer</button><span id="'+nameId+'" style="font-size:12px;color:#6b7280;"></span><input id="'+fileId+'" type="file" accept=".eml,.msg" style="display:none;" /></div>';
      } else if(field.type==='calculated'){
        if(field.id==='passes'){
          wrap.innerHTML='<label>'+field.label+'</label><div id="'+elId+'" style="font-size:13px;color:#374151;padding:4px 0;"></div>';
        } else {
          wrap.innerHTML='<label>'+field.label+calcNote+'</label><input id="'+elId+'" type="number" placeholder="auto" />';
        }
      } else if(field.type==='group'){
        wrap.innerHTML='<label>'+field.label+'</label><div id="'+elId+'"></div><button id="fab-repartitions-add" class="btn" style="margin-top:8px;font-size:12px;padding:4px 12px;">➕ Ajouter un point de livraison</button>';
      } else {
        wrap.innerHTML='<label>'+field.label+reqStar+'</label><input id="'+elId+'" type="text"'+roAttr+' />';
      }
      fabDynamicForm.appendChild(wrap);
    });

    if(section==='Finitions'){
      // Note: Façonnage (finitions) checkboxes section removed per UX decision
    }
  });

  // Only append Dates clés section if not already rendered via form-config
  if(!fabDynamicForm.querySelector('#fab-date-reception')){
    const kdHdr=document.createElement('div');
    kdHdr.className='fab-form-group fab-full-width fab-section-header';
    kdHdr.innerHTML='<span>Dates clés</span>';
    fabDynamicForm.appendChild(kdHdr);

    const kdGrid=document.createElement('div');
    kdGrid.className='fab-form-group fab-full-width';
    kdGrid.style.cssText='display:grid;grid-template-columns:repeat(auto-fill,minmax(200px,1fr));gap:12px;';
    kdGrid.innerHTML=''
      +'<div><label style="font-size:12px;color:#374151;font-weight:500;display:block;margin-bottom:4px;">Date de réception souhaitée</label>'
      +'<input id="fab-date-reception" type="date" style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" /></div>'
      +'<div><label style="font-size:12px;color:#374151;font-weight:500;display:block;margin-bottom:4px;">Date d\'envoi <small style="color:#9ca3af;font-weight:normal;">(indicatif)</small></label>'
      +'<input id="fab-date-envoi" type="date" readonly style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;background:#f3f4f6;color:#6b7280;" /></div>'
      +'<div><label style="font-size:12px;color:#374151;font-weight:500;display:block;margin-bottom:4px;">Date production Finitions <small style="color:#9ca3af;font-weight:normal;">(indicatif)</small></label>'
      +'<input id="fab-date-finitions" type="date" readonly style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;background:#f3f4f6;color:#6b7280;" /></div>'
      +'<div><label style="font-size:12px;color:#374151;font-weight:500;display:block;margin-bottom:4px;">Date d\'impression <small style="color:#9ca3af;font-weight:normal;">(indicatif)</small></label>'
      +'<input id="fab-date-impression" type="date" readonly style="width:100%;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;background:#f3f4f6;color:#6b7280;" /></div>';
    fabDynamicForm.appendChild(kdGrid);
  }

  // Only append Temps de production section if not already rendered via form-config
  if(!fabDynamicForm.querySelector('#fab-temps-produit')){
    const tpHdr=document.createElement('div');
    tpHdr.className='fab-form-group fab-full-width fab-section-header';
    tpHdr.innerHTML='<span>Temps de production</span>';
    fabDynamicForm.appendChild(tpHdr);

    const tpWrap=document.createElement('div');
    tpWrap.className='fab-form-group';
    tpWrap.innerHTML='<label>Temps théorique de production <small style="color:#9ca3af;font-size:10px;">(calculé auto, modifiable)</small></label>'
      +'<div style="display:flex;align-items:center;gap:8px;">'
      +'<input id="fab-temps-produit" type="number" placeholder="auto" style="width:100px;padding:6px 8px;border:1px solid #d1d5db;border-radius:6px;font-size:13px;" />'
      +'<span style="font-size:13px;color:#6b7280;">minutes</span>'
      +'</div>'
      +'<small style="color:#9ca3af;font-size:11px;margin-top:2px;">Calculé automatiquement, ou saisissez manuellement pour forcer la valeur.</small>';
    fabDynamicForm.appendChild(tpWrap);
  }

  // JDF button (will be shown/hidden based on JDF config)
  const jdfWrap=document.createElement('div');
  jdfWrap.className='fab-form-group fab-full-width';
  jdfWrap.id='fab-jdf-section';
  jdfWrap.style.display='none';
  jdfWrap.innerHTML='<button id="fab-generate-jdf" class="btn btn-primary" style="font-size:13px;padding:6px 16px;">📄 Générer JDF</button>'
    +'<span id="fab-jdf-msg" style="margin-left:10px;font-size:13px;"></span>';
  fabDynamicForm.appendChild(jdfWrap);
} // end renderFabForm

function populateFabForm(d, faconnageOptions) {
  function fmtDate(v){return fmtDate2(v);}
  function fmtDateTime(v){try{return new Date(v).toISOString().slice(0,16);}catch(e2){return '';}}
  function set(id,val){
    const el=gEl(id); if(!el) return;
    if(el.type==='checkbox'){el.checked=!!val;const lbl=document.getElementById('fab-rainage-label');if(lbl)lbl.textContent=el.checked?'Oui':'Non';}
    else if(el.type==='date'){el.value=val?fmtDate(val):'';}
    else if(el.type==='datetime-local'){el.value=val?fmtDateTime(val):'';}
    else{el.value=val!=null?val:'';}
  }
  set('numeroDossier',d.numeroDossier);set('client',d.client);set('operateur',d.operateur);
  set('typeTravail',d.typeTravail);set('formatFini',d.format);set('quantite',d.quantite);
  set('moteurImpression',d.moteurImpression||d.machine);
  set('donneurOrdreNom',d.donneurOrdreNom);set('donneurOrdrePrenom',d.donneurOrdrePrenom);
  set('donneurOrdreTelephone',d.donneurOrdreTelephone);set('donneurOrdreEmail',d.donneurOrdreEmail);
  set('rectoVerso',d.rectoVerso);set('formeDecoupe',d.formeDecoupe);set('pagination',d.pagination);
  set('formatFeuilleMachine',d.formatFeuille);
  set('media1',d.media1);set('media1Fabricant',d.media1Fabricant);set('media2',d.media2);set('media2Fabricant',d.media2Fabricant);
  set('media3',d.media3);set('media3Fabricant',d.media3Fabricant);set('media4',d.media4);set('media4Fabricant',d.media4Fabricant);
  set('couvertureMedia',d.mediaCouverture);set('couvertureFabricant',d.mediaCouvertureFabricant);
  set('bat',d.bat);set('retraitLivraison',d.retraitLivraison);set('adresseLivraison',d.adresseLivraison);
  set('plis',d.plis);set('sortie',d.sortie);set('faconnageBinding',d.faconnageBinding);set('notes',d.notes);
  set('justifsQuantite',d.justifsClientsQuantite!=null?d.justifsClientsQuantite:'');set('justifsAdresse',d.justifsClientsAdresse);
  set('dateDepart',d.dateDepart);set('planningMachine',d.planningMachine);set('rainage',d.rainage);
  const livEl=gEl("dateLivraison");
  if(livEl){livEl.value=d.dateLivraison?fmtDate(d.dateLivraison):'';}
  if(livEl){livEl._manuallyEdited=!!d.dateLivraison;}
  const nfEl=gEl("nombreFeuilles");
  if(nfEl){nfEl.value=d.nombreFeuilles||'';nfEl._manuallyEdited=!!d.nombreFeuilles;}
  const ennob=gEl("ennoblissement");
  if(ennob){
    const chk=Array.isArray(d.ennoblissement)?d.ennoblissement:[];
    // Vernis sélectif (radio)
    const vernisVal=chk.includes('Vernis sélectif')?'Vernis sélectif':'';
    const vernisEl=ennob.querySelector('input[type="radio"][value="'+vernisVal+'"][name*="fab-ennob-vernis"]');
    if(vernisEl) vernisEl.checked=true;
    // Dorure à chaud (radio)
    const dorureVal=chk.find(v=>v.startsWith('Dorure'))||'';
    const dorureEl=ennob.querySelector('input[type="radio"][name*="fab-ennob-dorure"][value="'+dorureVal+'"]');
    if(dorureEl) dorureEl.checked=true;
    // Pelliculage (checkboxes)
    ennob.querySelectorAll('.fab-ennob-pelliculage').forEach(cb=>{cb.checked=chk.includes(cb.value);});
    // Legacy flat checkboxes fallback
    ennob.querySelectorAll('.fab-ennob-cb').forEach(cb=>{cb.checked=chk.includes(cb.value);});
  }
  const facCont=document.getElementById('fab-faconnage-container');
  if(facCont){
    let cFac=[];
    if(Array.isArray(d.faconnage))cFac=d.faconnage;
    else if(typeof d.faconnage==='string'&&d.faconnage.startsWith('['))try{cFac=JSON.parse(d.faconnage);}catch(e2){}
    const opts2=Array.isArray(faconnageOptions)?faconnageOptions:[];
    if(opts2.length===0){facCont.innerHTML='<span style="color:#9ca3af;font-size:12px;">Aucune option</span>';}
    else{facCont.innerHTML='';opts2.forEach(opt=>{const lbl2=document.createElement('label');lbl2.style.cssText='display:inline-flex;align-items:center;gap:5px;padding:4px 10px;background:#f3f4f6;border-radius:6px;font-size:13px;cursor:pointer;border:1px solid #e5e7eb;';const cb2=document.createElement('input');cb2.type='checkbox';cb2.className='fab-faconnage-cb';cb2.value=opt;cb2.checked=cFac.includes(opt);lbl2.appendChild(cb2);lbl2.appendChild(document.createTextNode(opt));facCont.appendChild(lbl2);});}
  }
  const mdn=document.getElementById('fab-mail-devis-name');const mbn=document.getElementById('fab-mail-bat-name');
  if(mdn)mdn.textContent=d.mailDevisFileName||'';if(mbn)mbn.textContent=d.mailBatFileName||'';
  const repCont=gEl("repartitions");
  if(repCont){repCont.innerHTML='';(Array.isArray(d.repartitions)?d.repartitions:[]).forEach(r=>addRepartitionRow(r.quantite||'',r.adresse||''));}
  const ndEl2=gEl("numeroDossier");if(ndEl2){ndEl2.style.borderColor="";ndEl2.style.boxShadow="";}
  const tyEl2=gEl("typeTravail");if(tyEl2){tyEl2.style.borderColor="";tyEl2.style.boxShadow="";}
  // Key dates
  const recEl=document.getElementById('fab-date-reception');
  const envEl=document.getElementById('fab-date-envoi');
  const finEl=document.getElementById('fab-date-finitions');
  const impEl=document.getElementById('fab-date-impression');
  if(recEl) recEl.value=d.dateReception?fmtDate(d.dateReception):'';
  if(envEl) { envEl.value=d.dateEnvoi?fmtDate(d.dateEnvoi):''; if(envEl.value) envEl.dataset.protected='1'; else delete envEl.dataset.protected; }
  if(finEl) { finEl.value=d.dateProductionFinitions?fmtDate(d.dateProductionFinitions):''; if(finEl.value) finEl.dataset.protected='1'; else delete finEl.dataset.protected; }
  if(impEl) { impEl.value=d.dateImpression?fmtDate(d.dateImpression):''; if(impEl.value) impEl.dataset.protected='1'; else delete impEl.dataset.protected; }
  const tpEl=document.getElementById('fab-temps-produit');
  if(tpEl) {
    tpEl.value=d.tempsProduitMinutes!=null?d.tempsProduitMinutes:'';
    // Mark as manually-set if loaded from DB so updateTempsProduction() won't overwrite it
    if(d.tempsProduitMinutes!=null) tpEl.dataset.manual='1'; else delete tpEl.dataset.manual;
  }
  // Process d'impression
  set('process', d.process);
  set('bascule', d.bascule);
  set('couleurs', d.couleurs);
  set('couleursAccompagnement', d.couleursAccompagnement);
  // If dateReception is set but calculated dates are missing, recalculate
  if(recEl&&recEl.value&&(!envEl||!envEl.value)) updateKeyDates();
}

function attachFormHandlers(fabCurrentFileName) {
  if(!fabDynamicForm) return;
  fabDynamicForm.addEventListener('change',e=>{
    const id=e.target.id;
    if(id===gElId('couvertureMedia')){updateRainageAuto();updatePassesDisplay();}
    if(id===gElId('rainage')){const l=document.getElementById('fab-rainage-label');const r=gEl('rainage');if(l&&r)l.textContent=r.checked?'Oui':'Non';updatePassesDisplay();}
    if(id===gElId('typeTravail')){updateNombreFeuilles();updateCouvertureVisibility();}
    if(id===gElId('faconnageBinding')||e.target.classList.contains('fab-ennob-cb')
       ||e.target.classList.contains('fab-ennob-pelliculage')
       ||(e.target.type==='radio'&&e.target.name&&(e.target.name.includes('fab-ennob-vernis')||e.target.name.includes('fab-ennob-dorure'))))updatePassesDisplay();
    if(id===gElId('dateLivraison')){const el=gEl('dateLivraison');if(el)el._manuallyEdited=true;}
    if(id===gElId('nombreFeuilles')){const el=gEl('nombreFeuilles');if(el)el._manuallyEdited=true;}
    if(id===gElId('process')) updateDependsOnFields();
  });
  fabDynamicForm.addEventListener('input',e=>{
    const id=e.target.id;
    if(id===gElId('quantite'))updateNombreFeuilles();
    if(id===gElId('dateDepart'))updateDateLivraison();
    if(id==='fab-date-reception'||id==='fab-retrait-livraison'){
      // When the user explicitly changes the reception date or delivery type, remove protection
      // from the calculated date fields so they are recalculated from the new anchor.
      ['fab-date-envoi','fab-date-finitions','fab-date-impression'].forEach(fid=>{
        const el=document.getElementById(fid);if(el)delete el.dataset.protected;
      });
      updateKeyDates();
    }
    if(id===gElId('nombreFeuilles')){const el=gEl('nombreFeuilles');if(el)el._manuallyEdited=true;}
    if(id==='fab-temps-produit'){const el=document.getElementById('fab-temps-produit');if(el)el.dataset.manual=el.value?'1':'';}
    if(id===gElId('moteurImpression')||id===gElId('media1'))updateTempsProduction();
  });
  const repsAdd=document.getElementById('fab-repartitions-add'); if(repsAdd)repsAdd.onclick=()=>addRepartitionRow();
  const iBat=document.getElementById('fab-import-mail-bat');const fBat=document.getElementById('fab-mail-bat-file');
  if(iBat)iBat.onclick=()=>fBat&&fBat.click();
  if(fBat)fBat.onchange=async()=>{
    const file=fBat.files[0];if(!file)return;
    const fd=new FormData();fd.append('file',file);fd.append('fileName',fabCurrentFileName);
    try{const r=await fetch('/api/fabrication/import-mail-bat',{method:'POST',headers:{'Authorization':'Bearer '+authToken},body:fd}).then(r2=>r2.json());
    if(r.ok){const n=document.getElementById('fab-mail-bat-name');if(n)n.textContent=file.name;showNotification('✅ Mail BAT importé','success');}else showNotification('❌ '+(r.error||"Erreur d'import"),'error');
    }catch(err){showNotification('❌ Erreur réseau','error');}};
  const iDevis=document.getElementById('fab-import-mail-devis');const fDevis=document.getElementById('fab-mail-devis-file');
  if(iDevis)iDevis.onclick=()=>fDevis&&fDevis.click();
  if(fDevis)fDevis.onchange=async()=>{
    const file=fDevis.files[0];if(!file)return;
    const fd=new FormData();fd.append('file',file);fd.append('fileName',fabCurrentFileName);
    try{const r=await fetch('/api/fabrication/import-mail-devis',{method:'POST',headers:{'Authorization':'Bearer '+authToken},body:fd}).then(r2=>r2.json());
    if(r.ok){const n=document.getElementById('fab-mail-devis-name');if(n)n.textContent=file.name;showNotification('✅ Mail devis importé','success');}else showNotification('❌ '+(r.error||"Erreur d'import"),'error');
    }catch(err){showNotification('❌ Erreur réseau','error');}};
  // JDF button
  const jdfBtn=document.getElementById('fab-generate-jdf');
  if(jdfBtn){
    jdfBtn.onclick=async()=>{
      const msgEl=document.getElementById('fab-jdf-msg');
      if(msgEl) msgEl.textContent='Génération en cours...';
      const fn=fnKey(fabCurrentPath);
      try{
        const r=await fetch('/api/fabrication/generate-jdf',{method:'POST',headers:{'Content-Type':'application/json','Authorization':'Bearer '+authToken},body:JSON.stringify({fullPath:fabCurrentPath,fileName:fn})}).then(r2=>r2.json());
        if(r.ok){showNotification('✅ JDF généré','success');if(msgEl)msgEl.textContent='';}
        else{showNotification('❌ '+(r.error||'Erreur JDF'),'error');if(msgEl)msgEl.textContent='';}
      }catch(e){showNotification('❌ Erreur réseau','error');if(msgEl)msgEl.textContent='';}
    };
  }
}

export function initFabrication() {
  fabClose.onclick=()=>fabModal.classList.add('hidden');
  document.addEventListener('keydown',e=>{if(e.key==='Escape')fabModal.classList.add('hidden');});
  fabSave.onclick=async()=>{
    if(!fabCurrentPath)return;
    const ndEl=gEl('numeroDossier');const tyEl=gEl('typeTravail');let hasError=false;
    if(!ndEl||!ndEl.value.trim()){if(ndEl){ndEl.style.borderColor='#ef4444';ndEl.style.boxShadow='0 0 0 3px rgba(239,68,68,0.2)';}hasError=true;}else{if(ndEl){ndEl.style.borderColor='';ndEl.style.boxShadow='';} }
    if(!tyEl||!tyEl.value){if(tyEl){tyEl.style.borderColor='#ef4444';tyEl.style.boxShadow='0 0 0 3px rgba(239,68,68,0.2)';}hasError=true;}else{if(tyEl){tyEl.style.borderColor='';tyEl.style.boxShadow='';} }
    if(hasError){showNotification('❌ Numéro de dossier et Type de travail sont obligatoires','error');return;}
    const ok=await saveFabrication();
    if(ok){fabModal.classList.add('hidden');showNotification('✅ Fiche enregistrée','success');}
  };
  fabPdf.onclick=async()=>{
    if(!fabCurrentPath)return;
    await saveFabrication();await new Promise(res=>setTimeout(res,300));
    const fn=fnKey(fabCurrentPath);
    try{const r=await fetch('/api/fabrication/pdf?fileName='+encodeURIComponent(fn)+'&fullPath='+encodeURIComponent(fabCurrentPath)+'&save=true',{headers:{'Authorization':'Bearer '+authToken}});
    if(r.ok){const blob=await r.blob();window.open(URL.createObjectURL(blob),'_blank');showNotification('✅ PDF généré et enregistré','success');}
    else{const err=await r.json().catch(()=>({}));showNotification('❌ '+(err.error||'Impossible de générer le PDF'),'error');}
    }catch(err2){showNotification('❌ Erreur réseau','error');}};
  fabFinProd.onclick=async()=>{
    if(!fabCurrentPath){alert('Erreur : chemin introuvable');return;}
    if(!confirm("Marquer comme 'Fin de production' ?"))return;
    const moveResp=await fetch('/api/jobs/move',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({source:fabCurrentPath,destination:FIN_PROD_FOLDER,overwrite:true})}).then(r=>r.json()).catch(()=>({ok:false}));
    if(!moveResp.ok){alert('Erreur : '+(moveResp.error||''));return;}
    const movedPath=moveResp.moved||fabCurrentPath;
    await fetch('/api/jobs/lock',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({fullPath:movedPath})}).catch(err=>console.warn('[fabrication] Lock failed:',err));
    fabModal.classList.add('hidden');alert('Fin de production marquée');
    if(window._refreshKanban)await window._refreshKanban();
    if(window._refreshSubmissionView)await window._refreshSubmissionView();
    if(typeof calendar!=='undefined'&&calendar)calendar.refetchEvents();
    if(typeof submissionCalendar!=='undefined'&&submissionCalendar)submissionCalendar.refetchEvents();
  };
  if(fabPrisma)fabPrisma.style.display='none';

  // Export button: show inline format picker then download
  const fabExport = document.getElementById('fab-export');
  if (fabExport) {
    fabExport.onclick = () => {
      if (!fabCurrentPath) return;
      // Show a small inline format picker
      const existing = document.getElementById('fab-export-picker');
      if (existing) { existing.remove(); return; }
      const picker = document.createElement('span');
      picker.id = 'fab-export-picker';
      picker.style.cssText = 'display:inline-flex;gap:6px;margin-left:8px;align-items:center;';
      picker.innerHTML = '<span style="font-size:12px;color:#374151;">Format :</span>';
      ['xml', 'csv'].forEach(fmt => {
        const btn = document.createElement('button');
        btn.className = 'btn btn-sm';
        btn.style.cssText = 'font-size:12px;padding:4px 10px;';
        btn.textContent = fmt.toUpperCase();
        btn.onclick = async (e) => {
          e.stopPropagation();
          picker.remove();
          const fn = fnKey(fabCurrentPath);
          try {
            const resp = await fetch(
              `/api/integrations/export?format=${fmt}`,
              { headers: { 'Authorization': `Bearer ${authToken}` } }
            );
            if (!resp.ok) { showNotification('❌ Erreur export', 'error'); return; }
            const blob = await resp.blob();
            const url = URL.createObjectURL(blob);
            const a = document.createElement('a');
            a.href = url; a.download = `export-${fn}.${fmt}`;
            document.body.appendChild(a); a.click(); document.body.removeChild(a);
            URL.revokeObjectURL(url);
            showNotification(`✅ Export ${fmt.toUpperCase()} téléchargé`, 'success');
          } catch(err) { showNotification('❌ Erreur réseau', 'error'); }
        };
        picker.appendChild(btn);
      });
      fabExport.insertAdjacentElement('afterend', picker);
    };
  }
}

export async function openFabrication(fullPath, prefillData = null) {
  fabCurrentPath=normalizePath(fullPath);
  const fabCurrentFileName=fnKey(fabCurrentPath);
  if(fabDynamicForm){fabDynamicForm.style.opacity='0.5';fabDynamicForm.style.pointerEvents='none';}
  if(fabStageBanner)fabStageBanner.style.display='none';
  fabModal.classList.remove('hidden');
  const [j,engines,types,papers,faconnageOptions,stageData,sheetFormats,coverProducts,sheetCalcRulesResp,deliveryDelayResp,passesConfigResp,formConfig,keyDatesResp,grammageTimeResp,jdfConfigResp,bindingOptionsResp,foldsOptionsResp,outputOptionsResp]=await Promise.all([
    fetch('/api/fabrication?fileName='+encodeURIComponent(fabCurrentFileName),{headers:{'Authorization':'Bearer '+authToken}}).then(r=>r.json()).catch(()=>({})),
    fetchCached('/api/config/print-engines'),
    fetchCached('/api/config/work-types'),
    fetchCached('/api/config/paper-catalog'),
    fetch('/api/settings/faconnage-options',{headers:{'Authorization':'Bearer '+authToken}}).then(r=>r.json()).catch(()=>[]),
    fetch('/api/file-stage?fileName='+encodeURIComponent(fabCurrentFileName),{headers:{'Authorization':'Bearer '+authToken}}).then(r=>r.json()).catch(()=>null),
    fetch('/api/settings/sheet-formats').then(r=>r.json()).catch(()=>[]),
    fetch('/api/settings/cover-products').then(r=>r.json()).catch(()=>[]),
    fetch('/api/settings/sheet-calculation-rules').then(r=>r.json()).catch(()=>({rules:{}})),
    fetch('/api/settings/delivery-delay').then(r=>r.json()).catch(()=>({delayHours:48})),
    fetch('/api/settings/passes-config').then(r=>r.json()).catch(()=>({config:{}})),
    fetchFormConfig(),
    fetch('/api/config/key-dates-offsets',{headers:{'Authorization':'Bearer '+authToken}}).then(r=>r.json()).catch(()=>({config:{livraisonEnvoiHeures:48,livraisonFinitionsHeures:72,livraisonImpressionHeures:96,retraitEnvoiHeures:0,retraitFinitionsHeures:24,retraitImpressionHeures:48}})),
    fetch('/api/settings/grammage-time-config').then(r=>r.json()).catch(()=>({rules:[]})),
    fetch('/api/settings/jdf-config').then(r=>r.json()).catch(()=>({enabled:false,fields:[]})),
    fetch('/api/settings/binding-options').then(r=>r.json()).catch(()=>({ok:false,options:[]})),
    fetch('/api/settings/folds-options').then(r=>r.json()).catch(()=>({ok:false,options:[]})),
    fetch('/api/settings/output-options').then(r=>r.json()).catch(()=>({ok:false,options:[]}))
  ]);

  // Auto-prefill from portal order when fiche is new and filename matches a web order (WEB-...)
  // Only triggered when no existing fabrication data and no explicit prefillData provided
  let autoPrefill = null;
  const isNewSheet = !j || j.ok === false || (!j.numeroDossier && !j.client && !j.typeTravail);
  const _lcFileName = fabCurrentFileName.toLowerCase();
  const _isWebFile = _lcFileName.startsWith('web-') || _lcFileName.startsWith('bat_web-');
  if (isNewSheet && prefillData === null && _isWebFile) {
    try {
      // Strip BAT_ prefix if present, then extract order number (all lowercase — server uses case-insensitive regex)
      const _baseName = _lcFileName.startsWith('bat_') ? _lcFileName.substring(4) : _lcFileName;
      const orderNum = _baseName.includes('__') ? _baseName.split('__')[0] : _baseName.split('.')[0];
      const byJobResp = await fetch('/api/admin/portal/orders/by-job?numeroDossier=' + encodeURIComponent(orderNum), {
        headers: { 'Authorization': 'Bearer ' + authToken }
      }).then(r => r.json()).catch(() => ({}));
      if (byJobResp.ok && byJobResp.found && byJobResp.order && byJobResp.order.id) {
        const detailResp = await fetch('/api/admin/portal/orders/' + byJobResp.order.id + '/detail', {
          headers: { 'Authorization': 'Bearer ' + authToken }
        }).then(r => r.json()).catch(() => ({}));
        if (detailResp.ok && detailResp.order) {
          const o = detailResp.order;
          const notesArr = [o.notes, o.comments].filter(Boolean);
          // Build ennoblissement array from portal finition fields
          const ennobArr = [];
          if (o.vernisSelectif) ennobArr.push('Vernis sélectif');
          if (o.dorureAChaud && o.dorureAChaud !== 'Non' && o.dorureAChaud !== '') ennobArr.push(o.dorureAChaud);
          if (Array.isArray(o.pelliculage)) ennobArr.push(...o.pelliculage);
          // Map delivery points to repartitions
          const repartitions = Array.isArray(o.deliveryPoints) && o.deliveryPoints.length > 0
            ? o.deliveryPoints.map(pt => ({ quantite: pt.quantity, adresse: pt.address }))
            : null;
          autoPrefill = {
            numeroDossier:          o.orderNumber || null,
            client:                 detailResp.clientDisplayName || o.title || null,
            quantite:               o.quantity || null,
            typeTravail:            o.typeTravail || null,
            format:                 o.formatFini || o.format || null,
            rectoVerso:             o.recto || null,
            faconnage:              Array.isArray(o.finitions) ? o.finitions : [],
            retraitLivraison:       o.deliveryMode === 'livraison' ? 'Livraison' : o.deliveryMode === 'retrait' ? 'Retrait imprimerie' : (o.deliveryMode || null),
            adresseLivraison:       o.deliveryAddress || null,
            dateReception:          o.desiredDeliveryDate || null,
            dateEnvoi:              o.dateEnvoi || null,
            dateImpression:         o.dateImpression || null,
            dateProductionFinitions: o.dateProductionFinitions || null,
            donneurOrdreNom:        o.donneurOrdreNom || null,
            donneurOrdrePrenom:     o.donneurOrdrePrenom || null,
            donneurOrdreTelephone:  o.donneurOrdreTelephone || null,
            donneurOrdreEmail:      o.donneurOrdreEmail || null,
            media1:                 o.media1 || null,
            media2:                 o.media2 || null,
            media3:                 o.media3 || null,
            media4:                 o.media4 || null,
            mediaCouverture:        o.mediaCouverture || null,
            bat:                    (o.bat === 'avec' ? 'Numérique' : o.bat === 'sans' ? 'Non' : o.bat) || null,
            rainage:                !!o.rainage,
            ennoblissement:         ennobArr,
            plis:                   o.plis || null,
            pagination:             o.pagination || null,
            formatFeuille:          o.formatFeuille || null,
            formeDecoupe:           o.formeDecoupe || null,
            faconnageBinding:       o.faconnageBinding || null,
            notes:                  notesArr.length > 0 ? notesArr.join('\n') : null,
            justifsClientsQuantite: o.quantiteJustifs || null,
            justifsClientsAdresse:  o.adresseJustifs || null,
            repartitions:           repartitions,
          };
        }
      }
    } catch(e) { /* non-blocking — prefill failure must not block fiche opening */ }
  }

  // Merge prefill data over loaded data (prefill wins for non-empty / non-null values)
  const effectivePrefill = autoPrefill || prefillData;
  const jBase = (j && j.ok !== false) ? (j || {}) : {};
  let d;
  if (effectivePrefill) {
    const prefillEntries = Object.entries(effectivePrefill).filter(([, v]) => {
      if (v == null || v === '') return false;
      if (Array.isArray(v) && v.length === 0) return false;
      return true;
    });
    d = Object.assign({}, jBase, Object.fromEntries(prefillEntries));
  } else {
    d = jBase;
  }
  _coverProducts=Array.isArray(coverProducts)?coverProducts:[];
  _sheetCalcRules=(sheetCalcRulesResp&&sheetCalcRulesResp.rules)?sheetCalcRulesResp.rules:{};
  _deliveryDelayHours=(deliveryDelayResp&&deliveryDelayResp.delayHours)?deliveryDelayResp.delayHours:48;
  _passesConfig=(passesConfigResp&&passesConfigResp.config)?passesConfigResp.config:{faconnage:0,pelliculageRecto:0,pelliculageRectoVerso:0,rainage:0,dorure:0,dosCarreColle:0};
  const kdc=(keyDatesResp&&keyDatesResp.config)?keyDatesResp.config:{};
  _keyDatesConfig={
    livraison:{sendOffsetHours:kdc.livraisonEnvoiHeures??48,finitionsOffsetHours:kdc.livraisonFinitionsHeures??72,impressionOffsetHours:kdc.livraisonImpressionHeures??96},
    retrait:{sendOffsetHours:kdc.retraitEnvoiHeures??0,finitionsOffsetHours:kdc.retraitFinitionsHeures??24,impressionOffsetHours:kdc.retraitImpressionHeures??48}
  };
  _grammageTimeRules=Array.isArray(grammageTimeResp.rules)?grammageTimeResp.rules:[];
  _jdfEnabled=!!(jdfConfigResp.enabled);
  const config=formConfig||{fields:[],sections:[]};
  renderFabForm(config,{engines:Array.isArray(engines)?engines:[],types:Array.isArray(types)?types:[],papers:Array.isArray(papers)?papers:[],sheetFormats:Array.isArray(sheetFormats)?sheetFormats:[],faconnageOptions:Array.isArray(faconnageOptions)?faconnageOptions:[],bindingOptions:Array.isArray(bindingOptionsResp?.options)?bindingOptionsResp.options:[],foldsOptions:Array.isArray(foldsOptionsResp?.options)?foldsOptionsResp.options:[],outputOptions:Array.isArray(outputOptionsResp?.options)?outputOptionsResp.options:[]});
  populateFabForm(d,Array.isArray(faconnageOptions)?faconnageOptions:[]);
  updateDependsOnFields(); // show/hide process-dependent fields (e.g. Bascule when Offset selected)
  // If prefill provided, show a banner
  if(autoPrefill) {
    const banner = document.createElement('div');
    banner.style.cssText = 'background:#f0fdf4;border:1px solid #86efac;border-radius:8px;padding:8px 14px;margin-bottom:12px;font-size:12px;color:#15803d;display:flex;align-items:center;gap:8px;';
    banner.innerHTML = '🔗 <strong>Formulaire pré-rempli</strong> depuis la commande portail — vérifiez et complétez les champs avant d\'enregistrer.';
    if(fabDynamicForm) fabDynamicForm.insertAdjacentElement('beforebegin', banner);
  } else if(prefillData && Object.keys(prefillData).length > 0) {
    const banner = document.createElement('div');
    banner.style.cssText = 'background:#eff6ff;border:1px solid #93c5fd;border-radius:8px;padding:8px 14px;margin-bottom:12px;font-size:12px;color:#1e40af;display:flex;align-items:center;gap:8px;';
    banner.innerHTML = '🔗 <strong>Formulaire pré-rempli</strong> depuis XML ou ERP/W2P — vérifiez et complétez les champs avant d\'enregistrer.';
    if(fabDynamicForm) fabDynamicForm.insertAdjacentElement('beforebegin', banner);
  }
  attachFormHandlers(fabCurrentFileName);
  // Profile 6 (Opérateur restreint): lock date key fields
  if(currentUser && currentUser.profile === 6 && fabDynamicForm) {
    const lockedDateIds = ['fab-date-reception','fab-date-envoi','fab-date-finitions','fab-date-impression'];
    lockedDateIds.forEach(id => {
      const el = document.getElementById(id);
      if(el) { el.setAttribute('readonly',''); el.style.background='#f3f4f6'; el.style.color='#6b7280'; el.style.cursor='not-allowed'; }
    });
  }
  const delaiEl=gEl('delai');
  if(delaiEl){const dd=deliveriesByPath[fabCurrentFileName];delaiEl.value=d.delai?fmtDate2(d.delai):dd||'';}
  if(fabHistory){fabHistory.innerHTML='';(d.history||[]).forEach(h=>{const div=document.createElement('div');div.textContent=new Date(h.date).toLocaleDateString('fr-FR',{day:'2-digit',month:'2-digit',year:'numeric',hour:'2-digit',minute:'2-digit'})+' — '+h.user+' — '+h.action;fabHistory.appendChild(div);});}
  if(fabStageBanner&&stageData&&stageData.ok&&stageData.folder){fabStageBanner.textContent='📍 Étape actuelle : '+stageData.folder;fabStageBanner.style.display='block';if(stageData.fullPath)fabCurrentPath=normalizePath(stageData.fullPath);}
  fabRemove.onclick=async()=>{
    if(!fabCurrentFileName)return;if(!confirm('Retirer du planning ?'))return;
    // Try to delete delivery entry (may not exist if job was not in manual planning)
    await fetch('/api/delivery?fileName='+encodeURIComponent(fabCurrentFileName),{method:'DELETE',headers:{'Authorization':'Bearer '+authToken}}).catch(()=>{});
    delete deliveriesByPath[fabCurrentFileName];delete deliveriesByPath[fabCurrentFileName+'_time'];
    // Mark as excluded from fab calendar events (removes key-date events from calendar)
    await fetch('/api/fabrication/exclude-planning',{method:'PUT',headers:{'Content-Type':'application/json','Authorization':'Bearer '+authToken},body:JSON.stringify({fileName:fabCurrentFileName,exclude:true})}).catch(()=>{});
    // Clear the planning date field in the form
    const delaiEl=gEl('delai');if(delaiEl){delaiEl.value='';delete delaiEl._manuallyEdited;}
    if(calendar)calendar.refetchEvents();if(submissionCalendar)submissionCalendar.refetchEvents();
    if(window._refreshKanban)await window._refreshKanban();if(window._updateGlobalAlert)window._updateGlobalAlert();
    showNotification('✅ Retiré du planning','success');
  };
  updateCouvertureVisibility();updateRainageAuto();
  const nfEl2=gEl('nombreFeuilles');if(!nfEl2||!nfEl2._manuallyEdited)updateNombreFeuilles();
  updatePassesDisplay();
  updateTempsProduction();
  // Show JDF button if enabled
  const jdfSection=document.getElementById('fab-jdf-section');
  if(jdfSection) jdfSection.style.display=_jdfEnabled?'':'none';
  if(fabDynamicForm){fabDynamicForm.style.opacity='';fabDynamicForm.style.pointerEvents='';}
}

export async function saveFabrication() {
  if(!fabCurrentPath)return false;
  const fileName=fnKey(fabCurrentPath);
  const get=id=>{const el=gEl(id);return el?el.value:null;};
  const getN=id=>{const el=gEl(id);return el?(parseInt(el.value)||null):null;};
  const getCb=id=>{const el=gEl(id);return el?el.checked:null;};
  const facCont=document.getElementById('fab-faconnage-container');
  const payload={
    fullPath:fabCurrentPath,fileName,
    moteurImpression:get('moteurImpression'),machine:get('moteurImpression'),
    operateur:get('operateur')||null,quantite:getN('quantite'),typeTravail:get('typeTravail'),
    format:get('formatFini'),rectoVerso:get('rectoVerso'),formeDecoupe:get('formeDecoupe')||null,
    bat:get('bat')||null,retraitLivraison:get('retraitLivraison')||null,adresseLivraison:get('adresseLivraison')||null,
    client:get('client'),numeroDossier:get('numeroDossier')||null,notes:get('notes'),
    faconnage:facCont?Array.from(facCont.querySelectorAll('.fab-faconnage-cb:checked')).map(cb=>cb.value):[],
    delai:get('delai')||null,media1:get('media1')||null,media2:get('media2')||null,media3:get('media3')||null,media4:get('media4')||null,
    donneurOrdreNom:get('donneurOrdreNom')||null,donneurOrdrePrenom:get('donneurOrdrePrenom')||null,
    donneurOrdreTelephone:get('donneurOrdreTelephone')||null,donneurOrdreEmail:get('donneurOrdreEmail')||null,
    pagination:get('pagination')||null,formatFeuille:get('formatFeuilleMachine')||null,
    media1Fabricant:get('media1Fabricant')||null,media2Fabricant:get('media2Fabricant')||null,
    media3Fabricant:get('media3Fabricant')||null,media4Fabricant:get('media4Fabricant')||null,
    mediaCouverture:get('couvertureMedia')||null,mediaCouvertureFabricant:get('couvertureFabricant')||null,
    rainage:getCb('rainage'),
    ennoblissement:getEnnoblissementSelected(),
    faconnageBinding:get('faconnageBinding')||null,plis:get('plis')||null,sortie:get('sortie')||null,
    nombreFeuilles:getN('nombreFeuilles'),dateDepart:get('dateDepart')||null,
    dateLivraison:get('dateLivraison')||null,planningMachine:get('planningMachine')||null,
    dateReception:(()=>{const el=document.getElementById('fab-date-reception');return el&&el.value?el.value:null;})(),
    dateReceptionSouhaitee:(()=>{const el=document.getElementById('fab-date-reception-souhaitee')||document.getElementById('fab-date-reception');return el&&el.value?el.value:null;})(),
    dateEnvoi:(()=>{const el=document.getElementById('fab-date-envoi');return el&&el.value?el.value:null;})(),
    dateProductionFinitions:(()=>{const el=document.getElementById('fab-date-finitions');return el&&el.value?el.value:null;})(),
    dateImpression:(()=>{const el=document.getElementById('fab-date-impression');return el&&el.value?el.value:null;})(),
    tempsProduitMinutes:(()=>{const el=document.getElementById('fab-temps-produit');return el&&el.value?parseInt(el.value)||null:null;})(),
    finitionsChecked:(()=>{const cont=document.getElementById('fab-finitions-checked-container');return cont?Array.from(cont.querySelectorAll('.fab-finitions-cb:checked')).map(cb=>cb.value):null;})(),
    justifsClientsQuantite:getN('justifsQuantite'),justifsClientsAdresse:get('justifsAdresse')||null,
    repartitions:getRepartitions(),
    process:get('process')||null,
    bascule:get('bascule')||null,
    couleurs:get('couleurs')||null,
    couleursAccompagnement:get('couleursAccompagnement')||null,
  };
  const r=await fetch('/api/fabrication',{method:'PUT',headers:{'Content-Type':'application/json','Authorization':'Bearer '+authToken},body:JSON.stringify(payload)}).then(r2=>r2.json());
  if(!r.ok){alert('Erreur : '+r.error);return false;}return true;
}

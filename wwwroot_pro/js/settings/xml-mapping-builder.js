import { authToken, showNotification, esc } from '../core.js';

/**
 * Enhanced XML mapping builder with drag-and-drop support
 * Allows visual mapping between XML elements and fabrication sheet fields
 */

let _draggedElement = null; // Track dragged XML element for D&D
let _currentXmlDoc = null;  // Store current XML document for reference

/**
 * Parse XML file and display its structure as a tree for drag-and-drop mapping
 */
export async function renderXmlMappingBuilder(panel, cfg, ficheFields) {
  const mapping = cfg.xmlImport?.mapping || {};
  
  panel.innerHTML = `
    <div class="settings-section-card" style="border-left:4px solid #f59e0b;">
      <h4>📋 Constructeur de mapping XML avancé</h4>
      <p style="color:#6b7280;font-size:13px;margin-bottom:16px;">
        Importez un fichier XML pour visualiser sa structure. Glissez les éléments XML vers les champs de la fiche pour créer le mapping.
      </p>

      <!-- XML File Upload & Preview -->
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-bottom:20px;">
        <div>
          <h5 style="margin-top:0;font-size:13px;font-weight:600;color:#374151;">1️⃣ Importer un exemple XML</h5>
          <input type="file" id="xml-builder-file" accept=".xml" class="settings-input" style="margin-bottom:8px;" />
          <button id="xml-builder-load-btn" class="btn btn-sm btn-primary">Charger la structure</button>
          <div id="xml-builder-load-msg" style="margin-top:8px;font-size:12px;color:#6b7280;"></div>
        </div>

        <div>
          <h5 style="margin-top:0;font-size:13px;font-weight:600;color:#374151;">2️⃣ Sources XML configurées</h5>
          <div style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;padding:10px;font-size:12px;">
            <p id="xml-sources-list" style="margin:0;color:#6b7280;">Aucune source configurée</p>
          </div>
        </div>
      </div>

      <!-- Main mapping interface -->
      <div style="display:grid;grid-template-columns:1fr 1fr;gap:20px;margin-bottom:20px;border-top:1px solid #e5e7eb;padding-top:20px;">
        <!-- Left: XML Structure Tree (draggable items) -->
        <div>
          <h5 style="margin-top:0;font-size:13px;font-weight:600;color:#374151;">📄 Structure XML</h5>
          <div id="xml-tree" style="background:#f9fafb;border:1px solid #e5e7eb;border-radius:6px;padding:12px;min-height:300px;font-size:12px;font-family:monospace;overflow-y:auto;max-height:500px;">
            <p style="color:#9ca3af;margin:0;">Importez un fichier XML pour afficher sa structure</p>
          </div>
        </div>

        <!-- Right: Field Mappings (drop zones) -->
        <div>
          <h5 style="margin-top:0;font-size:13px;font-weight:600;color:#374151;">🎯 Champs fiche</h5>
          <div id="xml-mapping-zones" style="display:flex;flex-direction:column;gap:8px;background:#f0f9ff;border:1px solid #bae6fd;border-radius:6px;padding:12px;min-height:300px;overflow-y:auto;max-height:500px;">
            ${ficheFields.map(f => `
              <div class="xml-drop-zone" data-field="${esc(f.key)}" 
                style="background:white;border:2px dashed #1e40af;border-radius:4px;padding:10px;cursor:grab;min-height:40px;display:flex;align-items:center;justify-content:space-between;transition:all 0.2s ease;">
                <div style="flex:1;min-width:0;">
                  <div style="font-weight:600;color:#374151;word-break:break-word;">${esc(f.key)}</div>
                  <div style="font-size:11px;color:#6b7280;">${esc(f.label)}</div>
                </div>
                <div class="xml-field-value" style="flex:0 0 auto;margin-left:8px;padding:4px 8px;background:#eff6ff;border:1px solid #bae6fd;border-radius:3px;font-size:11px;color:#0369a1;word-break:break-all;max-width:150px;overflow:hidden;text-overflow:ellipsis;">
                  ${esc(mapping[f.key] || '—')}
                </div>
              </div>
            `).join('')}
          </div>
        </div>
      </div>

      <!-- Current mapping review -->
      <div style="background:#f0fdf4;border:1px solid #86efac;border-radius:6px;padding:12px;margin-bottom:16px;">
        <h5 style="margin:0 0 10px;font-size:13px;font-weight:600;color:#166534;">✅ Mapping actuel</h5>
        <div id="xml-mapping-review" style="font-size:12px;color:#166534;max-height:200px;overflow-y:auto;">
          ${Object.keys(mapping).length === 0 
            ? '<p style="margin:0;color:#9ca3af;">Aucun mapping défini</p>'
            : Object.entries(mapping).map(([k, v]) => 
              `<div style="display:flex;justify-content:space-between;padding:4px 0;border-bottom:1px solid #86efac;gap:10px;">
                 <code style="color:#059669;word-break:break-all;flex:1;">${esc(k)}</code>
                 <span style="flex-shrink:0;">→</span>
                 <code style="color:#0d9488;word-break:break-all;flex:1;">${esc(v)}</code>
               </div>`
            ).join('')
          }
        </div>
      </div>

      <!-- Save button -->
      <div style="display:flex;gap:8px;">
        <button id="xml-builder-save-btn" class="btn btn-primary">💾 Enregistrer le mapping</button>
        <button id="xml-builder-reset-btn" class="btn" style="color:#ef4444;border-color:#ef4444;">🔄 Réinitialiser</button>
      </div>
      <div id="xml-builder-save-msg" style="margin-top:8px;font-size:13px;"></div>
    </div>
  `;

  // ============================================================
  // Event Handlers
  // ============================================================

  // Load XML file
  document.getElementById('xml-builder-load-btn').onclick = async () => {
    const fileInput = document.getElementById('xml-builder-file');
    const msgEl = document.getElementById('xml-builder-load-msg');
    
    if (!fileInput.files || fileInput.files.length === 0) {
      msgEl.style.color = '#ef4444';
      msgEl.textContent = 'Sélectionnez un fichier XML';
      return;
    }

    msgEl.style.color = '#6b7280';
    msgEl.textContent = '⏳ Parsing XML…';

    try {
      const file = fileInput.files[0];
      
      // Use FileReader for better compatibility
      const text = await new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = (e) => resolve(e.target.result);
        reader.onerror = (e) => reject(new Error('Erreur de lecture du fichier'));
        reader.readAsText(file);
      });

      const parser = new DOMParser();
      const xmlDoc = parser.parseFromString(text, 'text/xml');

      // Check for XML parsing errors
      if (xmlDoc.getElementsByTagName('parsererror').length > 0) {
        const error = xmlDoc.getElementsByTagName('parsererror')[0];
        msgEl.style.color = '#ef4444';
        msgEl.textContent = '❌ XML invalide : ' + error.textContent;
        return;
      }

      // Store the parsed document for reference
      _currentXmlDoc = xmlDoc;

      // Render XML tree
      const treeContainer = document.getElementById('xml-tree');
      treeContainer.innerHTML = renderXmlTree(xmlDoc.documentElement, 0);

      // Wire up drag events on XML elements
      setupXmlElementDragEvents();

      msgEl.style.color = '#16a34a';
      msgEl.textContent = '✅ Structure XML chargée';
    } catch (e) {
      msgEl.style.color = '#ef4444';
      msgEl.textContent = '❌ Erreur : ' + e.message;
      console.error('XML loading error:', e);
    }
  };

  // Save mapping
  document.getElementById('xml-builder-save-btn').onclick = async () => {
    const msgEl = document.getElementById('xml-builder-save-msg');
    const newMapping = {};

    // Collect current mapping from all drop zones
    document.querySelectorAll('.xml-drop-zone').forEach(zone => {
      const fieldKey = zone.dataset.field;
      const valueEl = zone.querySelector('.xml-field-value');
      const value = valueEl?.textContent?.trim();
      if (value && value !== '—') {
        newMapping[fieldKey] = value;
      }
    });

    msgEl.style.color = '#6b7280';
    msgEl.textContent = '⏳ Enregistrement…';

    try {
      const response = await fetch('/api/settings/integrations-config', {
        method: 'PUT',
        headers: {
          'Content-Type': 'application/json',
          'Authorization': `Bearer ${authToken}`
        },
        body: JSON.stringify({
          section: 'xmlImport',
          data: {
            ...cfg.xmlImport,
            mapping: newMapping
          }
        })
      });

      const r = await response.json();

      if (r.ok) {
        msgEl.style.color = '#16a34a';
        msgEl.textContent = '✅ Mapping enregistré';
        cfg.xmlImport = { ...cfg.xmlImport, mapping: newMapping };
        updateMappingReview(newMapping);
        setTimeout(() => { msgEl.textContent = ''; }, 2000);
      } else {
        msgEl.style.color = '#ef4444';
        msgEl.textContent = '❌ ' + (r.error || 'Erreur inconnue');
      }
    } catch (e) {
      msgEl.style.color = '#ef4444';
      msgEl.textContent = '❌ Erreur réseau : ' + e.message;
      console.error('Save error:', e);
    }
  };

  // Reset mapping
  document.getElementById('xml-builder-reset-btn').onclick = () => {
    if (!confirm('Êtes-vous sûr de vouloir réinitialiser le mapping ?')) return;
    document.querySelectorAll('.xml-drop-zone').forEach(zone => {
      zone.querySelector('.xml-field-value').textContent = '—';
    });
    updateMappingReview({});
  };

  /**
   * Render XML tree as draggable elements
   */
  function renderXmlTree(node, depth = 0, maxDepth = 5) {
    if (depth > maxDepth) return '';
    if (node.nodeType === 3) return ''; // skip text nodes
    if (node.nodeType === 8) return ''; // skip comments

    const tagName = node.nodeName.toLowerCase();
    const textContent = node.textContent?.trim().substring(0, 30);
    const hasChildren = Array.from(node.childNodes).some(n => n.nodeType === 1);

    const elemId = `xml-elem-${Math.random().toString(36).substr(2, 9)}`;
    const fullPath = getXmlPath(node);

    let html = `
      <div style="margin-left:${depth * 16}px;padding:4px 0;">
        <div class="xml-tree-element" id="${elemId}" draggable="true"
          data-path="${esc(fullPath)}"
          style="background:#fff;border:1px solid #e5e7eb;border-radius:3px;padding:6px;margin-bottom:4px;cursor:grab;user-select:none;font-size:11px;transition:all 0.15s ease;">
          <span style="color:#1e40af;font-weight:600;">&lt;${esc(tagName)}&gt;</span>
          ${textContent && !hasChildren ? `<span style="color:#6b7280;margin-left:4px;">"${esc(textContent)}"</span>` : ''}
        </div>
    `;

    if (hasChildren) {
      html += Array.from(node.childNodes)
        .filter(n => n.nodeType === 1)
        .map(child => renderXmlTree(child, depth + 1, maxDepth))
        .join('');
    }

    html += '</div>';
    return html;
  }

  /**
   * Get XPath-like path to an element for later extraction
   */
  function getXmlPath(node) {
    const parts = [];
    let current = node;
    
    while (current && current.nodeType === 1) {
      let index = 1;
      let sibling = current.previousSibling;
      
      while (sibling) {
        if (sibling.nodeType === 1 && sibling.nodeName === current.nodeName) {
          index++;
        }
        sibling = sibling.previousSibling;
      }
      
      const nodeName = current.nodeName.toLowerCase();
      parts.unshift(`${nodeName}[${index}]`);
      current = current.parentElement;
    }
    
    return '/' + parts.join('/');
  }

  /**
   * Set up drag-and-drop events
   */
  function setupXmlElementDragEvents() {
    document.querySelectorAll('.xml-tree-element').forEach(elem => {
      elem.addEventListener('dragstart', (e) => {
        _draggedElement = {
          path: elem.dataset.path,
          text: elem.textContent
        };
        e.dataTransfer.effectAllowed = 'copy';
        e.dataTransfer.setData('text/plain', elem.dataset.path);
        elem.style.opacity = '0.6';
      });

      elem.addEventListener('dragend', (e) => {
        elem.style.opacity = '1';
      });
    });

    document.querySelectorAll('.xml-drop-zone').forEach(zone => {
      zone.addEventListener('dragover', (e) => {
        e.preventDefault();
        e.dataTransfer.dropEffect = 'copy';
        zone.style.background = '#dbeafe';
        zone.style.borderColor = '#0284c7';
      });

      zone.addEventListener('dragleave', (e) => {
        // Only reset if leaving the zone itself, not child elements
        if (e.target === zone) {
          zone.style.background = 'white';
          zone.style.borderColor = '#1e40af';
        }
      });

      zone.addEventListener('drop', (e) => {
        e.preventDefault();
        e.stopPropagation();
        
        zone.style.background = 'white';
        zone.style.borderColor = '#1e40af';

        if (_draggedElement) {
          const valueEl = zone.querySelector('.xml-field-value');
          valueEl.textContent = _draggedElement.path;
          valueEl.title = _draggedElement.path; // Show full path on hover
          
          // Collect all current mappings and update review
          const allMappings = {};
          document.querySelectorAll('.xml-drop-zone').forEach(z => {
            const fieldKey = z.dataset.field;
            const val = z.querySelector('.xml-field-value')?.textContent?.trim();
            if (val && val !== '—') {
              allMappings[fieldKey] = val;
            }
          });
          updateMappingReview(allMappings);
        }
        
        _draggedElement = null;
      });
    });
  }

  /**
   * Update the mapping review section
   */
  function updateMappingReview(mapping) {
    const reviewEl = document.getElementById('xml-mapping-review');
    if (Object.keys(mapping).length === 0) {
      reviewEl.innerHTML = '<p style="margin:0;color:#9ca3af;">Aucun mapping défini</p>';
    } else {
      reviewEl.innerHTML = Object.entries(mapping)
        .map(([k, v]) => `
          <div style="display:flex;justify-content:space-between;padding:4px 0;border-bottom:1px solid #86efac;gap:10px;">
            <code style="color:#059669;word-break:break-all;flex:1;">${esc(k)}</code>
            <span style="flex-shrink:0;">→</span>
            <code style="color:#0d9488;word-break:break-all;flex:1;">${esc(v)}</code>
          </div>
        `).join('');
    }
  }
}

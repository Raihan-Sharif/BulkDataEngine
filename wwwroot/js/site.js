/* ================================================================
   Bulk Data Engine — Client Logic
   ================================================================ */
(function () {
  'use strict';

  // ── Theme Toggling ───────────────────────────────────────────────
  const themeToggleBtn = document.getElementById('themeToggleBtn');
  if (themeToggleBtn) {
    const iconSun = themeToggleBtn.querySelector('.icon-sun');
    const iconMoon = themeToggleBtn.querySelector('.icon-moon');
    
    function applyThemeIcons(theme) {
      if (theme === 'dark') {
        iconSun.style.display = 'block';   // Show Sun in Dark Mode (to indicate "switch to light")
        iconMoon.style.display = 'none';
      } else {
        iconSun.style.display = 'none';
        iconMoon.style.display = 'block';  // Show Moon in Light Mode (to indicate "switch to dark")
      }
    }
    
    // Initial icon state
    const currentTheme = document.documentElement.getAttribute('data-theme') || 'light';
    applyThemeIcons(currentTheme);
    
    themeToggleBtn.addEventListener('click', () => {
      const isDark = document.documentElement.getAttribute('data-theme') === 'dark';
      const newTheme = isDark ? 'light' : 'dark';
      document.documentElement.setAttribute('data-theme', newTheme);
      localStorage.setItem('theme', newTheme);
      applyThemeIcons(newTheme);
    });
  }

  // ── Utilities ──────────────────────────────────────────────────
  function fmtBytes(bytes) {
    if (!bytes || bytes === 0) return '0 B';
    const k = 1024, s = ['B', 'KB', 'MB', 'GB'];
    const i = Math.floor(Math.log(bytes) / Math.log(k));
    return (bytes / Math.pow(k, i)).toFixed(1) + ' ' + s[i];
  }

  function esc(str) {
    return String(str == null ? '' : str)
      .replace(/&/g, '&amp;').replace(/</g, '&lt;')
      .replace(/>/g, '&gt;').replace(/"/g, '&quot;');
  }

  function getRadioVal(name) {
    const el = document.querySelector(`input[name="${name}"]:checked`);
    return el ? el.value : null;
  }

  function getDelimiterVal(sel, custom) {
    return sel.value === 'custom' ? (custom.value.trim() || null) : sel.value;
  }

  function getCustomConnStr() {
    const sel = document.getElementById('connEnvSelect');
    if (sel && sel.value === 'custom') {
      return (document.getElementById('customConnStr').value || '').trim();
    }
    return ''; // The backend will use default mapping if this is empty, but we can also pass the selected value if needed. Actually it's probably better to always pass the value from the input or select. Let's return the string directly. Wait, the backend uses default if customConnStr is empty. Since we have multiple defaults, the backend currently takes _defaultConnectionString which uses "DefaultConnection". 
    // We should just pass the selected connection string directly to the backend!
  }
  
  function getSelectedConnStr() {
    const sel = document.getElementById('connEnvSelect');
    if (!sel) return '';
    return sel.value === 'custom' ? document.getElementById('customConnStr').value.trim() : sel.value;
  }

  // ── Drop-zone wiring ───────────────────────────────────────────
  function wireDropZone(zone, input, onFile) {
    zone.addEventListener('click', () => input.click());
    zone.addEventListener('keydown', e => {
      if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); input.click(); }
    });
    zone.addEventListener('dragover', e => { e.preventDefault(); zone.classList.add('drag-over'); });
    zone.addEventListener('dragleave', e => {
      if (!zone.contains(e.relatedTarget)) zone.classList.remove('drag-over');
    });
    zone.addEventListener('drop', e => {
      e.preventDefault(); zone.classList.remove('drag-over');
      if (e.dataTransfer?.files?.length) onFile(e.dataTransfer.files[0]);
    });
    input.addEventListener('change', () => { if (input.files?.[0]) onFile(input.files[0]); });
  }

  // ── Card HTML builders ─────────────────────────────────────────
  function makeSuccessCard(title, detail) {
    return `<div class="result-success">
      <div class="result-icon success-icon">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <polyline points="20,6 9,17 4,12"/>
        </svg>
      </div>
      <div class="result-text">
        <strong>${esc(title)}</strong>
        <span>${esc(detail)}</span>
      </div>
    </div>`;
  }

  function makeErrorCard(msg) {
    return `<div class="result-error">
      <div class="result-icon error-icon">
        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <circle cx="12" cy="12" r="10"/>
          <line x1="15" y1="9" x2="9" y2="15"/>
          <line x1="9" y1="9" x2="15" y2="15"/>
        </svg>
      </div>
      <div class="result-text">
        <strong>Error</strong>
        <span>${esc(msg)}</span>
      </div>
    </div>`;
  }

  // ── Tab switching ───────────────────────────────────────────────
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.tab-btn').forEach(b => b.classList.remove('active'));
      document.querySelectorAll('.tab-pane').forEach(p => p.classList.add('hidden'));
      btn.classList.add('active');
      document.getElementById('tab-' + btn.dataset.tab).classList.remove('hidden');
    });
  });

  // ── Connection mode dropdown logic ──────────────────────────────
  const connEnvSelect = document.getElementById('connEnvSelect');
  const customConnStr = document.getElementById('customConnStr');
  const connHint = document.getElementById('connHint');

  if (connEnvSelect && customConnStr) {
    // Initial setup
    const initialVal = connEnvSelect.value;
    if (initialVal !== 'custom') {
      customConnStr.value = initialVal;
      customConnStr.readOnly = true;
      customConnStr.classList.add('readonly-input');
    }

    connEnvSelect.addEventListener('change', () => {
      const val = connEnvSelect.value;
      if (val === 'custom') {
        customConnStr.value = '';
        customConnStr.readOnly = false;
        customConnStr.classList.remove('readonly-input');
        customConnStr.focus();
        if (connHint) connHint.textContent = "Enter your full ADO.NET SQL Server connection string.";
      } else {
        customConnStr.value = val;
        customConnStr.readOnly = true;
        customConnStr.classList.add('readonly-input');
        if (connHint) connHint.textContent = "Value fetched from appsettings. Select 'Custom' to override manually.";
      }
    });
  }

  // ════════════════════════════════════════════════════════════════
  //  CONVERT TXT TAB
  // ════════════════════════════════════════════════════════════════
  let convFile = null;
  let convJobId = null;
  let convES = null;
  let selectedFormat = 'csv';

  const convDropZone   = document.getElementById('convDropZone');
  const convFileInput  = document.getElementById('convFileInput');
  const convFileInfo   = document.getElementById('convFileInfo');
  const convFileName   = document.getElementById('convFileName');
  const convFileSize   = document.getElementById('convFileSize');
  const convClearFile  = document.getElementById('convClearFile');
  const convertBtn     = document.getElementById('convertBtn');
  const convertBtnText = document.getElementById('convertBtnText');
  const convProgress   = document.getElementById('convProgress');
  const convProgLabel  = document.getElementById('convProgLabel');
  const convProgPct    = document.getElementById('convProgPct');
  const convProgBar    = document.getElementById('convProgBar');
  const convRowsStat   = document.getElementById('convRowsStat');
  const convBytesStat  = document.getElementById('convBytesStat');
  const convCancelBtn  = document.getElementById('convCancelBtn');
  const convResult     = document.getElementById('convResult');
  const convSuccessCard = document.getElementById('convSuccessCard');
  const convErrorCard  = document.getElementById('convErrorCard');
  const convResultStats = document.getElementById('convResultStats');
  const convErrMsg     = document.getElementById('convErrMsg');
  const convDownloadBtn = document.getElementById('convDownloadBtn');
  const convPreview    = document.getElementById('convPreview');
  const convDbPanel    = document.getElementById('convDbPanel');
  const delimSel       = document.getElementById('delimiterSelect');
  const custDelim      = document.getElementById('customDelimiter');
  const excelHint      = document.querySelector('.excel-hint');

  // Format toggle
  document.querySelectorAll('.format-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      document.querySelectorAll('.format-btn').forEach(b => b.classList.remove('active'));
      btn.classList.add('active');
      selectedFormat = btn.dataset.format;
      excelHint.classList.toggle('hidden', selectedFormat !== 'xlsx');
    });
  });

  // Delimiter select
  delimSel.addEventListener('change', () => {
    custDelim.classList.toggle('hidden', delimSel.value !== 'custom');
    if (delimSel.value === 'custom') custDelim.focus();
  });

  // Drop zone + file selection
  wireDropZone(convDropZone, convFileInput, setConvFile);
  convClearFile.addEventListener('click', clearConvFile);

  function setConvFile(file) {
    convFile = file;
    convFileName.textContent = file.name;
    convFileSize.textContent = fmtBytes(file.size);
    convFileInfo.classList.remove('hidden');
    convDropZone.classList.add('hidden');
    convertBtn.disabled = false;
    resetConvUI();
  }

  function clearConvFile() {
    convFile = null;
    convFileInput.value = '';
    convFileInfo.classList.add('hidden');
    convDropZone.classList.remove('hidden');
    convertBtn.disabled = true;
    resetConvUI();
  }

  function resetConvUI() {
    if (convES) { convES.close(); convES = null; }
    convProgress.classList.add('hidden');
    convResult.classList.add('hidden');
    convSuccessCard.classList.add('hidden');
    convErrorCard.classList.add('hidden');
    convPreview.innerHTML = '';
    convPreview.classList.add('hidden');
    convDbPanel.innerHTML = '';
    convDbPanel.classList.add('hidden');
  }

  // Convert button
  convertBtn.addEventListener('click', startConversion);

  async function startConversion() {
    if (!convFile) return;
    const delimiter = getDelimiterVal(delimSel, custDelim);
    if (!delimiter) { alert('Please enter a custom delimiter.'); return; }

    resetConvUI();
    convertBtn.disabled = true;
    convertBtnText.textContent = 'Converting...';
    showConvProgress(0, 'Uploading...');

    const fd = new FormData();
    fd.append('file', convFile);
    fd.append('delimiter', delimiter);
    fd.append('hasHeader', getRadioVal('hasHeader') === 'true');
    fd.append('outputFormat', selectedFormat);

    try {
      const res = await fetch('/Home/Upload', { method: 'POST', body: fd });
      const data = await res.json();
      if (!res.ok) {
        convProgress.classList.add('hidden');
        showConvError(data.error || 'Upload failed.');
        convertBtn.disabled = false;
        convertBtnText.textContent = 'Convert';
        return;
      }
      convJobId = data.jobId;
      startConvSSE(convJobId);
    } catch (err) {
      convProgress.classList.add('hidden');
      showConvError('Network error: ' + err.message);
      convertBtn.disabled = false;
      convertBtnText.textContent = 'Convert';
    }
  }

  function showConvProgress(pct, label, lines, bytesProc, totalBytes) {
    convProgress.classList.remove('hidden');
    convProgLabel.textContent = label || '';
    convProgPct.textContent = pct + '%';
    convProgBar.style.width = pct + '%';
    convRowsStat.textContent  = lines ? `${Number(lines).toLocaleString()} rows` : '';
    convBytesStat.textContent = totalBytes > 0
      ? `${fmtBytes(bytesProc)} / ${fmtBytes(totalBytes)}` : '';
  }

  function showConvSuccess(data) {
    convResult.classList.remove('hidden');
    convSuccessCard.classList.remove('hidden');
    convErrorCard.classList.add('hidden');
    const parts = [];
    if (data.processedLines)   parts.push(`${Number(data.processedLines).toLocaleString()} rows`);
    if (data.columnCount)      parts.push(`${data.columnCount} cols`);
    if (data.outputSizeBytes)  parts.push(fmtBytes(data.outputSizeBytes));
    convResultStats.textContent = parts.join(' • ');
  }

  function showConvError(msg) {
    convResult.classList.remove('hidden');
    convErrorCard.classList.remove('hidden');
    convSuccessCard.classList.add('hidden');
    convErrMsg.textContent = msg;
  }

  convCancelBtn.addEventListener('click', () => {
    if (convJobId) fetch(`/Home/Cancel?jobId=${encodeURIComponent(convJobId)}`, { method: 'POST' });
  });

  convDownloadBtn.addEventListener('click', () => {
    if (convJobId) window.location.href = `/Home/Download?jobId=${encodeURIComponent(convJobId)}`;
  });

  function startConvSSE(jobId) {
    if (convES) convES.close();
    convES = new EventSource(`/Home/Progress?jobId=${encodeURIComponent(jobId)}`);
    convES.onmessage = e => {
      const data = JSON.parse(e.data);
      switch (data.status) {
        case 'uploading':
          showConvProgress(5, 'Uploading...');
          break;
        case 'processing':
          showConvProgress(data.percent, data.message,
            data.processedLines, data.processedBytes, data.totalBytes);
          break;
        case 'completed':
          convES.close(); convES = null;
          showConvProgress(100, 'Complete!');
          setTimeout(() => {
            convertBtn.disabled = false;
            convertBtnText.textContent = 'Convert';
            convProgress.classList.add('hidden');
            showConvSuccess(data);
            renderPreviewPanel(convPreview, data);
            renderDbPanel(convDbPanel, jobId, data.processedLines, data.suggestedTable);
          }, 600);
          break;
        case 'failed':
          convES.close(); convES = null;
          convertBtn.disabled = false;
          convertBtnText.textContent = 'Convert';
          convProgress.classList.add('hidden');
          showConvError(data.error || data.message || 'Conversion failed.');
          break;
        case 'cancelled':
          convES.close(); convES = null;
          convertBtn.disabled = false;
          convertBtnText.textContent = 'Convert';
          convProgress.classList.add('hidden');
          showConvError('Conversion was cancelled.');
          break;
      }
    };
    // onerror: EventSource auto-reconnects by spec — don't show a false error.
    // The server sends keepalive pings every 15 s so the connection stays alive.
    convES.onerror = () => { /* silent — let browser reconnect */ };
  }

  // ════════════════════════════════════════════════════════════════
  //  IMPORT TAB
  // ════════════════════════════════════════════════════════════════
  let impFile = null;
  let impJobId = null;

  const impDropZone      = document.getElementById('impDropZone');
  const impFileInput     = document.getElementById('impFileInput');
  const impFileInfo      = document.getElementById('impFileInfo');
  const impFileName      = document.getElementById('impFileName');
  const impFileSize      = document.getElementById('impFileSize');
  const impClearFile     = document.getElementById('impClearFile');
  const impUploadBtn     = document.getElementById('impUploadBtn');
  const impUploadBtnText = document.getElementById('impUploadBtnText');
  const impProgress      = document.getElementById('impProgress');
  const impResult        = document.getElementById('impResult');
  const impPreview       = document.getElementById('impPreview');
  const impDbPanel       = document.getElementById('impDbPanel');
  const impDelimSel      = document.getElementById('impDelimiterSelect');
  const impCustDelim     = document.getElementById('impCustomDelimiter');

  wireDropZone(impDropZone, impFileInput, setImpFile);
  impClearFile.addEventListener('click', clearImpFile);

  impDelimSel.addEventListener('change', () => {
    impCustDelim.classList.toggle('hidden', impDelimSel.value !== 'custom');
    if (impDelimSel.value === 'custom') impCustDelim.focus();
  });

  function setImpFile(file) {
    impFile = file;
    impFileName.textContent = file.name;
    impFileSize.textContent = fmtBytes(file.size);
    impFileInfo.classList.remove('hidden');
    impDropZone.classList.add('hidden');
    impUploadBtn.disabled = false;
    resetImpUI();
  }

  function clearImpFile() {
    impFile = null;
    impFileInput.value = '';
    impFileInfo.classList.add('hidden');
    impDropZone.classList.remove('hidden');
    impUploadBtn.disabled = true;
    resetImpUI();
  }

  function resetImpUI() {
    impProgress.classList.add('hidden');
    impResult.innerHTML = '';
    impResult.classList.add('hidden');
    impPreview.innerHTML = '';
    impPreview.classList.add('hidden');
    impDbPanel.innerHTML = '';
    impDbPanel.classList.add('hidden');
  }

  impUploadBtn.addEventListener('click', startImportUpload);

  async function startImportUpload() {
    if (!impFile) return;
    const delimiter = getDelimiterVal(impDelimSel, impCustDelim);
    if (!delimiter) { alert('Please enter a custom delimiter.'); return; }

    resetImpUI();
    impUploadBtn.disabled = true;
    impUploadBtnText.textContent = 'Loading...';
    impProgress.classList.remove('hidden');

    const fd = new FormData();
    fd.append('file', impFile);
    fd.append('delimiter', delimiter);
    fd.append('hasHeader', getRadioVal('impHasHeader') === 'true');

    try {
      const res = await fetch('/Home/ImportUpload', { method: 'POST', body: fd });
      const data = await res.json();
      impProgress.classList.add('hidden');
      impUploadBtn.disabled = false;
      impUploadBtnText.textContent = 'Load & Preview';

      if (!res.ok) {
        impResult.innerHTML = makeErrorCard(data.error || 'Upload failed.');
        impResult.classList.remove('hidden');
        return;
      }

      impJobId = data.jobId;
      const rows = data.estimatedRows ? `${Number(data.estimatedRows).toLocaleString()} rows` : '';
      const cols = data.columnCount   ? `${data.columnCount} columns` : '';
      impResult.innerHTML = makeSuccessCard('File loaded', [rows, cols].filter(Boolean).join(' • '));
      impResult.classList.remove('hidden');

      renderPreviewPanel(impPreview, data);
      renderDbPanel(impDbPanel, impJobId, data.estimatedRows, data.suggestedTable);
    } catch (err) {
      impProgress.classList.add('hidden');
      impResult.innerHTML = makeErrorCard('Network error: ' + err.message);
      impResult.classList.remove('hidden');
      impUploadBtn.disabled = false;
      impUploadBtnText.textContent = 'Load & Preview';
    }
  }

  // ════════════════════════════════════════════════════════════════
  //  SHARED — Preview panel
  // ════════════════════════════════════════════════════════════════
  function renderPreviewPanel(container, data) {
    const cols = data.columnNames || [];
    const rows = data.previewRows  || [];
    if (cols.length === 0) return;

    const colCount  = data.columnCount || cols.length;
    const totalRows = data.processedLines || data.estimatedRows || 0;

    const thead = `<tr><th>#</th>${cols.map(c => `<th title="${esc(c)}">${esc(c)}</th>`).join('')}</tr>`;
    const tbody = rows.map((row, i) => {
      const cells = cols.map((_, c) => {
        const v = row[c] != null ? String(row[c]) : '';
        return `<td title="${esc(v)}">${esc(v)}</td>`;
      }).join('');
      return `<tr><td>${i + 1}</td>${cells}</tr>`;
    }).join('');

    container.innerHTML = `
      <div class="panel-header">
        <svg class="panel-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <rect x="3" y="3" width="18" height="18" rx="2"/>
          <line x1="3" y1="9" x2="21" y2="9"/>
          <line x1="3" y1="15" x2="21" y2="15"/>
          <line x1="9" y1="9" x2="9" y2="21"/>
        </svg>
        <h2>Data Preview</h2>
        <div class="preview-meta">
          <span class="meta-badge">${colCount} col${colCount !== 1 ? 's' : ''}</span>
          <span class="meta-badge">${Number(totalRows).toLocaleString()} rows</span>
        </div>
      </div>
      <div class="preview-table-wrap">
        <table class="preview-table">
          <thead>${thead}</thead>
          <tbody>${tbody}</tbody>
        </table>
      </div>
      <p class="preview-note">Showing first ${rows.length} row${rows.length !== 1 ? 's' : ''}</p>`;
    container.classList.remove('hidden');
  }

  // ════════════════════════════════════════════════════════════════
  //  SHARED — DB Insert panel
  // ════════════════════════════════════════════════════════════════
  function renderDbPanel(container, jobId, totalRows, suggestedName) {
    const rowsLabel = totalRows ? `${Number(totalRows).toLocaleString()} rows` : '';

    container.innerHTML = `
      <div class="panel-header">
        <svg class="panel-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
          <ellipse cx="12" cy="5" rx="9" ry="3"/>
          <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/>
          <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/>
        </svg>
        <h2>Insert into SQL Server</h2>
        <span class="panel-sub">${esc(rowsLabel)}</span>
      </div>
      <div class="db-config-row">
        <div class="field-group" style="flex:1">
          <label class="field-label">Target Table Name</label>
          <input type="text" class="styled-input db-tbl-input"
                 value="${esc(suggestedName || '')}"
                 placeholder="e.g. MyData_temp_20260409" />
        </div>
        <button class="btn-db-insert db-do-insert">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <ellipse cx="12" cy="5" rx="9" ry="3"/>
            <path d="M21 12c0 1.66-4 3-9 3s-9-1.34-9-3"/>
            <path d="M3 5v14c0 1.66 4 3 9 3s9-1.34 9-3V5"/>
          </svg>
          Insert
        </button>
      </div>
      <div class="db-info-row">
        <div class="db-info-chip">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <circle cx="12" cy="12" r="10"/><polyline points="12,6 12,12 16,14"/>
          </svg>
          Drops &amp; re-creates table, then bulk inserts
        </div>
        <div class="db-info-chip">
          <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
          </svg>
          Batched transactions &bull; All columns <code>NVARCHAR(MAX)</code>
        </div>
      </div>
      <div class="db-progress-wrap hidden">
        <div class="progress-section">
          <div class="progress-header">
            <span class="progress-label db-prog-lbl">Connecting...</span>
            <div class="progress-actions">
              <span class="progress-percent db-prog-pct">0%</span>
              <button class="btn-cancel db-cancel-btn">Cancel</button>
            </div>
          </div>
          <div class="progress-bar-wrap">
            <div class="progress-bar-fill db-bar db-prog-bar" style="width:0%">
              <div class="progress-bar-shimmer"></div>
            </div>
          </div>
          <div class="progress-stats">
            <span class="db-rows-done"></span>
            <span class="db-rows-total"></span>
          </div>
        </div>
      </div>
      <div class="db-result-area"></div>`;

    // Wire up elements
    const tblInput   = container.querySelector('.db-tbl-input');
    const insertBtn  = container.querySelector('.db-do-insert');
    const progWrap   = container.querySelector('.db-progress-wrap');
    const progLbl    = container.querySelector('.db-prog-lbl');
    const progPct    = container.querySelector('.db-prog-pct');
    const progBar    = container.querySelector('.db-prog-bar');
    const rowsDone   = container.querySelector('.db-rows-done');
    const rowsTot    = container.querySelector('.db-rows-total');
    const cancelBtn  = container.querySelector('.db-cancel-btn');
    const resultArea = container.querySelector('.db-result-area');

    cancelBtn.addEventListener('click', () => {
      fetch(`/Home/CancelDb?jobId=${encodeURIComponent(jobId)}`, { method: 'POST' });
    });

    insertBtn.addEventListener('click', async () => {
      const tableName = tblInput.value.trim();
      if (!tableName) { alert('Please enter a table name.'); tblInput.focus(); return; }

      insertBtn.disabled = true;
      resultArea.innerHTML = '';
      progLbl.textContent  = 'Connecting...';
      progPct.textContent  = '0%';
      progBar.style.width  = '0%';
      rowsDone.textContent = '';
      rowsTot.textContent  = '';
      progWrap.classList.remove('hidden');

      const fd = new FormData();
      fd.append('jobId',     jobId);
      fd.append('tableName', tableName);
      const cc = getSelectedConnStr();
      if (cc) fd.append('customConnStr', cc);

      try {
        const res = await fetch('/Home/InsertDatabase', { method: 'POST', body: fd });
        const d   = await res.json();
        if (!res.ok) {
          progWrap.classList.add('hidden');
          resultArea.innerHTML = makeErrorCard(d.error || 'Failed to start insert.');
          insertBtn.disabled = false;
          return;
        }
        startDbSSE(jobId, insertBtn, progWrap, progLbl, progPct, progBar, rowsDone, rowsTot, resultArea);
      } catch (err) {
        progWrap.classList.add('hidden');
        resultArea.innerHTML = makeErrorCard('Network error: ' + err.message);
        insertBtn.disabled = false;
      }
    });

    container.classList.remove('hidden');
  }

  // ── DB SSE stream with reconnect + poll fallback ────────────────
  function startDbSSE(jobId, insertBtn, progWrap, progLbl, progPct, progBar, rowsDone, rowsTot, resultArea) {
    let finished = false;
    let errTimer  = null;

    function updateProg(data) {
      const pct = data.percent || 0;
      progLbl.textContent  = data.message || '';
      progPct.textContent  = pct + '%';
      progBar.style.width  = pct + '%';
      rowsDone.textContent = data.insertedRows
        ? `${Number(data.insertedRows).toLocaleString()} inserted` : '';
      rowsTot.textContent  = data.totalRows
        ? `of ${Number(data.totalRows).toLocaleString()} rows` : '';
    }

    function handleEvent(data) {
      switch (data.status) {
        case 'queued':
        case 'processing':
          updateProg(data);
          break;
        case 'completed':
          finished = true;
          updateProg({ ...data, percent: 100, message: 'Complete!' });
          setTimeout(() => {
            insertBtn.disabled = false;
            progWrap.classList.add('hidden');
            const r = data.insertedRows
              ? `${Number(data.insertedRows).toLocaleString()} rows inserted` : '';
            const t = data.tableName ? ` into [${data.tableName}]` : '';
            resultArea.innerHTML = makeSuccessCard('Inserted successfully', (r + t).trim());
          }, 600);
          break;
        case 'failed':
          finished = true;
          insertBtn.disabled = false;
          progWrap.classList.add('hidden');
          resultArea.innerHTML = makeErrorCard(data.error || data.message || 'Insert failed.');
          break;
        case 'cancelled':
          finished = true;
          insertBtn.disabled = false;
          progWrap.classList.add('hidden');
          resultArea.innerHTML = makeErrorCard('Insert was cancelled.');
          break;
        case 'notfound':
          finished = true;
          insertBtn.disabled = false;
          progWrap.classList.add('hidden');
          resultArea.innerHTML = makeErrorCard('Job lost or expired on the server. Please check the DB manually.');
          break;
      }
    }

    // Poll HTTP fallback — checks job state once and handles terminal states
    function poll() {
      fetch(`/Home/DbJobStatus?jobId=${encodeURIComponent(jobId)}`)
        .then(async r => {
            if (!r.ok) {
               if (r.status === 404) {
                  finished = true;
                  es.close();
                  handleEvent({ status: 'notfound' });
               }
               throw new Error('Not OK');
            }
            return r.json();
        })
        .then(data => {
          if (['completed', 'failed', 'cancelled', 'notfound'].includes(data.status)) {
            finished = true;
            es.close();
            handleEvent(data);
          } else if (!finished) {
            // Still processing! Schedule another poll just in case SSE is completely dead
            errTimer = setTimeout(() => { errTimer = null; poll(); }, 5000);
          }
        })
        .catch(() => {
          // If network error, try polling again
          if (!finished) {
              errTimer = setTimeout(() => { errTimer = null; poll(); }, 8000);
          }
        });
    }

    const es = new EventSource(`/Home/DbProgress?jobId=${encodeURIComponent(jobId)}`);

    es.onmessage = e => {
      // Clear any pending error timer — we have a live connection
      if (errTimer) { clearTimeout(errTimer); errTimer = null; }
      const data = JSON.parse(e.data);
      if (['completed', 'failed', 'cancelled'].includes(data.status)) {
        finished = true;
        es.close();
      }
      handleEvent(data);
    };

    es.onerror = () => {
      if (finished) { es.close(); return; }
      // SSE connection dropped (proxy timeout / network blip).
      // EventSource will auto-reconnect. Don't show an error immediately.
      // After 8 s without a message, poll HTTP to check if the job already finished.
      if (!errTimer) {
        errTimer = setTimeout(() => {
          errTimer = null;
          poll();
        }, 8000);
      }
    };
  }

})();

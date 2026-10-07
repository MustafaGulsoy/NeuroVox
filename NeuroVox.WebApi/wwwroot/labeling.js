const CATEGORIES = [
  "Anomia", "Circumlocution", "VagueExpression", "EmptyExpression", "Repetition",
  "PronounAmbiguity", "SemanticError", "MorphosyntacticIssue", "InformationOmission",
  "EventOmission", "CoherenceIssue", "CohesionIssue", "TopicDeviation"
];

function apiBase() {
  return document.getElementById('apiBase').value.replace(/\/$/, '');
}
function customerId() { return document.getElementById('customerId').value.trim(); }
function recordingId() { return document.getElementById('recordingId').value.trim(); }

window.addEventListener('DOMContentLoaded', () => {
  const sel = document.getElementById('category');
  CATEGORIES.forEach((c, i) => {
    const o = document.createElement('option');
    o.value = i; o.textContent = c;
    sel.appendChild(o);
  });
});

async function loadRecording() {
  const res = await fetch(`${apiBase()}/api/SpeechRecordings/${recordingId()}`, {
    headers: { 'customerid': customerId() }
  });
  if (!res.ok) { alert('Kayıt bulunamadı'); return; }
  const rec = await res.json();
  document.getElementById('visitId').value = rec.visitId;
  document.getElementById('player').src = rec.audioFilePath || '';
  document.getElementById('transcriptBox').textContent = rec.transcriptText || '(transkript henüz yok)';
  document.getElementById('workspace').hidden = false;
  await refreshAnnotations();
}

async function refreshAnnotations() {
  const annId = document.getElementById('annotatorId').value.trim();
  const url = annId ? `${apiBase()}/api/TherapistAnnotations/by-recording/${recordingId()}?annotatorId=${encodeURIComponent(annId)}` : `${apiBase()}/api/TherapistAnnotations/by-recording/${recordingId()}`;
  const res = await fetch(url, {
    headers: { 'customerid': customerId() }
  });
  const rows = await res.json();
  const tbody = document.querySelector('#annotationsTable tbody');
  tbody.innerHTML = '';
  rows.forEach(r => {
    const tr = document.createElement('tr');
    tr.innerHTML = `<td>${CATEGORIES[r.category] ?? r.category}</td><td>${r.startSeconds}</td><td>${r.endSeconds}</td><td>${r.severity ?? ''}</td><td>${r.note ?? ''}</td>`;
    tbody.appendChild(tr);
  });
}

function useCurrentTime() {
  document.getElementById('startSeconds').value = document.getElementById('player').currentTime.toFixed(1);
}

async function submitAnnotation() {
  const body = {
    recordingId: recordingId(),
    visitId: document.getElementById('visitId').value,
    annotatorId: document.getElementById('annotatorId').value,
    raterIndex: parseInt(document.getElementById('raterIndex').value, 10),
    category: parseInt(document.getElementById('category').value, 10),
    severity: document.getElementById('severity').value,
    confidence: parseFloat(document.getElementById('confidence').value) || null,
    startSeconds: parseFloat(document.getElementById('startSeconds').value),
    endSeconds: parseFloat(document.getElementById('endSeconds').value),
    segmentText: document.getElementById('segmentText').value,
    note: document.getElementById('note').value,
    annotationVersion: '1.0'
  };
  const res = await fetch(`${apiBase()}/api/TherapistAnnotations`, {
    method: 'POST',
    headers: { 'customerid': customerId(), 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  if (res.ok) { await refreshAnnotations(); } else { alert('Kayıt hatası'); }
}

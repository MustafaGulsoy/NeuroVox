import { Component, ElementRef, OnDestroy, OnInit, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { ANNOTATION_CATEGORIES, Annotation, Recording } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, StatusBadge, fmtDuration } from '../shared/ui';

// One hotkey per category: 1-9, 0, then q w e.
const KEYS = ['1', '2', '3', '4', '5', '6', '7', '8', '9', '0', 'q', 'w', 'e'];
const color = (i: number) => `hsl(${(i * 47) % 360} 65% 50%)`;
const PEAKS = 6000;

@Component({
  selector: 'app-annotate',
  imports: [FormsModule, RouterLink, IconComponent, Empty, StatusBadge],
  template: `
    <div class="page">
      <a routerLink="/recordings" class="muted">← Kayıtlar</a>
      <div class="page-head" style="margin-top:10px">
        <div><h1>Kör anotasyon</h1><p>Dalga formunda aralığı sürükleyerek seçin, sonra etiket tuşuna basın. Yalnızca kendi etiketlerinizi görürsünüz.</p></div>
        @if (rec()) { <app-status [status]="rec()!.analysisStatus" /> }
      </div>

      @if (!rec()) { <div class="skeleton" style="height:160px"></div> } @else {
        <section class="card">
          @if (audioUrl()) {
            <audio #player [src]="audioUrl()!" preload="auto" (loadedmetadata)="onMeta()" (play)="playing.set(true); tick()" (pause)="playing.set(false)" (seeked)="queueDraw()"></audio>
            <div class="wave-bar">
              <button class="btn sm primary" type="button" (click)="toggle()" [title]="playing() ? 'Durdur (boşluk)' : 'Oynat (boşluk)'">{{ playing() ? '❚❚ Durdur' : '▶ Oynat' }}</button>
              <button class="btn sm" type="button" (click)="skip(-5)" title="Geri 5 sn (Shift+←)">−5 sn</button>
              <button class="btn sm" type="button" (click)="skip(5)" title="İleri 5 sn (Shift+→)">+5 sn</button>
              <span class="mono">{{ dur(time()) }} / {{ dur(duration()) }}</span>
              <span class="spacer"></span>
              <label class="check"><input type="checkbox" [checked]="loop()" (change)="loop.set(!loop())" /> Seçimi döngüle (L)</label>
              <select class="input" style="width:auto" [ngModel]="rate()" (ngModelChange)="setRate($event)" aria-label="Hız">
                @for (r of rates; track r) { <option [ngValue]="r">{{ r }}×</option> }
              </select>
              <button class="btn sm" type="button" (click)="setZoom(zoom() / 2)" [disabled]="zoom() <= 1" aria-label="Uzaklaştır">−</button>
              <span class="muted">{{ zoom() }}×</span>
              <button class="btn sm" type="button" (click)="setZoom(zoom() * 2)" [disabled]="zoom() >= 32" aria-label="Yakınlaştır">+</button>
            </div>
            <div class="wave-scroll" #scroller>
              <canvas #cv class="wave" (pointerdown)="down($event)" (pointermove)="move($event)" (pointerup)="up($event)" (pointercancel)="drag = null"></canvas>
            </div>
            <div class="row" style="margin-top:8px;gap:8px;flex-wrap:wrap">
              @if (sel(); as s) {
                <span class="badge info">Seçim: {{ s.a.toFixed(1) }}–{{ s.b.toFixed(1) }} sn ({{ (s.b - s.a).toFixed(1) }} sn)</span>
                <button class="btn sm" type="button" (click)="clearSel()">Temizle (Esc)</button>
              } @else { <span class="muted">Aralık seçmek için dalga formunda sürükleyin, ya da oynatırken <kbd>I</kbd> ile başlangıç, <kbd>O</kbd> ile bitiş işaretleyin.</span> }
            </div>
          } @else { <div class="notice warn">{{ rec()!.hasAudio ? 'Ses yükleniyor…' : 'Bu kayıtta ses dosyası yok.' }}</div> }
        </section>

        <div class="grid" style="grid-template-columns:minmax(0,1.4fr) minmax(300px,1fr);gap:16px;align-items:start;margin-top:16px" id="ws">
          <div class="stack">
            <section class="card">
              <header><h2>Etiket</h2><span class="muted">tuşa basınca seçili aralık kaydedilir</span></header>
              <div class="chips">
                @for (c of cats; track $index) {
                  <button type="button" class="chip" [style.--c]="col($index)" (click)="label($index)" [disabled]="busy()">
                    <kbd>{{ keys[$index].toUpperCase() }}</kbd> {{ c }}
                  </button>
                }
              </div>
              <div class="row" style="margin-top:14px;gap:12px;flex-wrap:wrap;align-items:end">
                <div class="field">Şiddet
                  <div class="seg">
                    @for (s of sevs; track s) { <button type="button" [class.on]="severity() === s" (click)="severity.set(s)">{{ s || '—' }}</button> }
                  </div>
                </div>
                <label class="field" style="width:110px">Değerlendirici #
                  <input class="input" name="r" type="number" min="1" [(ngModel)]="raterIndex" />
                </label>
              </div>
            </section>

            <section class="card">
              <header>
                <h2>Transkript @if (rec()!.transcriptSource === 'manual') { <span class="badge">elle düzeltildi</span> }</h2>
                @if (!editing()) { <button class="btn sm" type="button" (click)="editText = rec()!.transcriptText || ''; editing.set(true)">Düzenle</button> }
              </header>
              @if (editing()) {
                <textarea class="input" rows="8" [(ngModel)]="editText" name="tx" style="width:100%;line-height:1.6"></textarea>
                <div class="row" style="margin-top:10px;gap:8px">
                  <button class="btn primary" type="button" (click)="saveText()" [disabled]="busy()">Kaydet</button>
                  <button class="btn" type="button" (click)="editing.set(false)">Vazgeç</button>
                </div>
              } @else if (rec()!.transcriptText) { <p style="white-space:pre-wrap;line-height:1.7">{{ rec()!.transcriptText }}</p> }
              @else { <p class="muted">Transkript henüz yok. Kayıtlar sayfasından analizi başlatın ya da elle yazın.</p> }
            </section>
          </div>

          <aside class="card flush" style="position:sticky;top:16px">
            <header style="padding:16px 20px 0"><h2>Etiketlerim ({{ anns().length }})</h2></header>
            @if (!anns().length) { <app-empty icon="tag" text="Henüz etiket eklemediniz" /> } @else {
              <ul class="alist">
                @for (a of anns(); track a.id) {
                  <li (click)="select(a)" [class.on]="activeId() === a.id">
                    <i class="dot" [style.background]="col(a.category)"></i>
                    <div><b>{{ cats[a.category] }}</b>@if (a.severity) { <span class="muted"> · {{ a.severity }}</span> }
                      <div class="muted mono">{{ a.startSeconds.toFixed(1) }}–{{ a.endSeconds.toFixed(1) }} sn@if (a.note) { · {{ a.note }} }</div></div>
                    <button class="btn sm danger" type="button" (click)="remove(a); $event.stopPropagation()" title="Sil" aria-label="Sil"><app-icon name="trash" [size]="14" /></button>
                  </li>
                }
              </ul>
            }
          </aside>
        </div>
      }
    </div>`,
  styles: [`
    @media (max-width: 900px) { #ws { grid-template-columns: 1fr !important; } aside { position: static !important; } }
    .wave-bar { display: flex; flex-wrap: wrap; gap: 8px; align-items: center; margin-bottom: 10px; }
    .spacer { flex: 1; }
    .wave-scroll { overflow-x: auto; border: 1px solid var(--border); border-radius: 10px; background: var(--surface-2); }
    canvas.wave { display: block; height: 170px; cursor: crosshair; touch-action: none; }
    kbd { font: 600 .72rem var(--font); padding: 1px 6px; border: 1px solid var(--border); border-bottom-width: 2px; border-radius: 5px; background: var(--surface); color: var(--muted); }
    .chips { display: flex; flex-wrap: wrap; gap: 8px; }
    .chip { display: inline-flex; align-items: center; gap: 6px; padding: 7px 12px; border-radius: 99px; border: 1.5px solid var(--c); background: transparent; color: var(--text); font: inherit; font-size: .88rem; cursor: pointer; }
    .chip:hover:not(:disabled) { background: color-mix(in srgb, var(--c) 18%, transparent); }
    .chip:disabled { opacity: .5; cursor: default; }
    .seg { display: inline-flex; border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
    .seg button { border: 0; background: var(--surface); color: var(--text); padding: 8px 12px; font: inherit; font-size: .85rem; cursor: pointer; }
    .seg button.on { background: var(--primary); color: #fff; }
    .alist { list-style: none; margin: 0; padding: 8px 0; max-height: 60vh; overflow: auto; }
    .alist li { display: flex; gap: 10px; align-items: center; padding: 10px 20px; cursor: pointer; border-top: 1px solid var(--border); }
    .alist li > div { flex: 1; min-width: 0; }
    .alist li.on, .alist li:hover { background: var(--surface-2); }
    .dot { width: 10px; height: 10px; border-radius: 50%; flex: none; }
  `]
})
export class AnnotatePage implements OnInit, OnDestroy {
  id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(ToastService);
  player = viewChild<ElementRef<HTMLAudioElement>>('player');
  cv = viewChild<ElementRef<HTMLCanvasElement>>('cv');
  scroller = viewChild<ElementRef<HTMLDivElement>>('scroller');

  rec = signal<Recording | null>(null);
  anns = signal<Annotation[]>([]);
  audioUrl = signal<string | null>(null);
  busy = signal(false);
  playing = signal(false);
  time = signal(0);
  duration = signal(0);
  zoom = signal(1);
  rate = signal(1);
  loop = signal(false);
  sel = signal<{ a: number; b: number } | null>(null);
  activeId = signal<string | null>(null);
  severity = signal('');
  editing = signal(false);
  editText = '';
  raterIndex = 1;
  cats = ANNOTATION_CATEGORIES; keys = KEYS; dur = fmtDuration; col = color;
  rates = [0.5, 0.75, 1, 1.25, 1.5, 2];
  sevs = ['', 'Hafif', 'Orta', 'Ağır'];
  drag: { x0: number; t0: number; moved: boolean } | null = null;

  private peaks: Float32Array | null = null;
  private raf = 0;
  private drawQueued = false;

  private onKey = (e: KeyboardEvent) => {
    const el = e.target as HTMLElement;
    if (['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName) || e.ctrlKey || e.metaKey || e.altKey) return;
    const k = e.key.toLowerCase();
    const ci = KEYS.indexOf(k);
    if (e.code === 'Space' && el.tagName !== 'BUTTON') { e.preventDefault(); this.toggle(); }
    else if (ci >= 0) this.label(ci);
    else if (k === 'i') this.mark('a');
    else if (k === 'o') this.mark('b');
    else if (k === 'l') this.loop.set(!this.loop());
    else if (k === 'escape') this.clearSel();
    else if (k === 'arrowleft') { e.preventDefault(); this.skip(e.shiftKey ? -5 : -2); }
    else if (k === 'arrowright') { e.preventDefault(); this.skip(e.shiftKey ? 5 : 2); }
  };
  private onResize = () => this.layout();

  ngOnInit() {
    window.addEventListener('keydown', this.onKey);
    window.addEventListener('resize', this.onResize);
    this.api.recording(this.id()).subscribe({
      next: r => {
        this.rec.set(r);
        this.duration.set(r.recordingDurationSeconds || 0);
        // <audio> cannot send the bearer token, so the audio is fetched as a blob (also decoded for the waveform).
        if (r.hasAudio) this.api.audio(r.id).subscribe({
          next: b => { this.audioUrl.set(URL.createObjectURL(b)); this.decode(b); },
          error: e => this.toast.err(e)
        });
      },
      error: e => this.toast.err(e)
    });
    this.loadAnns();
  }
  ngOnDestroy() {
    window.removeEventListener('keydown', this.onKey);
    window.removeEventListener('resize', this.onResize);
    cancelAnimationFrame(this.raf);
    const u = this.audioUrl(); if (u) URL.revokeObjectURL(u);
  }

  // ponytail: whole file decoded in memory (fine for interview-length audio); stream peaks server-side if files grow past ~100 MB.
  private async decode(blob: Blob) {
    try {
      const ctx = new AudioContext();
      const buf = await ctx.decodeAudioData(await blob.arrayBuffer());
      void ctx.close();
      const ch = buf.getChannelData(0), size = Math.max(1, Math.floor(ch.length / PEAKS));
      const p = new Float32Array(PEAKS);
      for (let i = 0; i < PEAKS; i++) {
        let m = 0;
        for (let j = i * size, end = Math.min(ch.length, j + size); j < end; j++) { const v = Math.abs(ch[j]); if (v > m) m = v; }
        p[i] = m;
      }
      this.peaks = p;
      if (!this.duration()) this.duration.set(buf.duration);
    } catch { /* waveform is optional: timeline and labelling still work without it */ }
    this.layout();
  }

  onMeta() {
    const d = this.player()?.nativeElement.duration;
    if (d && isFinite(d)) this.duration.set(d);
    this.layout();
  }

  // --- canvas ---
  private layout() {
    const cv = this.cv()?.nativeElement, sc = this.scroller()?.nativeElement;
    if (!cv || !sc) return;
    const w = Math.max(300, Math.floor(sc.clientWidth * this.zoom())), h = 170, dpr = window.devicePixelRatio || 1;
    cv.style.width = w + 'px';
    cv.width = w * dpr; cv.height = h * dpr;
    cv.getContext('2d')!.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.draw();
  }

  queueDraw() {
    if (this.drawQueued) return;
    this.drawQueued = true;
    requestAnimationFrame(() => { this.drawQueued = false; this.draw(); });
  }

  private draw() {
    const cv = this.cv()?.nativeElement;
    if (!cv) return;
    const g = cv.getContext('2d')!, w = parseFloat(cv.style.width) || cv.clientWidth, h = 170, d = this.duration() || 1;
    const css = getComputedStyle(cv);
    const text = css.getPropertyValue('--text').trim() || '#0f172a', muted = css.getPropertyValue('--muted').trim() || '#64748b', primary = css.getPropertyValue('--primary').trim() || '#0d9488';
    const x = (t: number) => (t / d) * w;
    g.clearRect(0, 0, w, h);

    for (const a of this.anns()) {            // saved labels as coloured bands
      g.globalAlpha = a.id === this.activeId() ? .38 : .22;
      g.fillStyle = color(a.category);
      g.fillRect(x(a.startSeconds), 0, Math.max(2, x(a.endSeconds) - x(a.startSeconds)), h);
    }
    g.globalAlpha = 1;
    const s = this.sel();
    if (s) { g.fillStyle = primary; g.globalAlpha = .28; g.fillRect(x(s.a), 0, Math.max(1, x(s.b) - x(s.a)), h); g.globalAlpha = 1; g.fillStyle = primary; g.fillRect(x(s.a), 0, 2, h); g.fillRect(x(s.b) - 2, 0, 2, h); }

    const mid = (h - 18) / 2;
    g.fillStyle = muted;
    if (this.peaks) {
      for (let px = 0; px < w; px++) {
        const i = Math.min(PEAKS - 1, Math.floor((px / w) * PEAKS));
        const a = Math.max(1, this.peaks[i] * mid);
        g.fillRect(px, mid - a, 1, a * 2);
      }
    } else g.fillRect(0, mid, w, 1);

    // time ruler
    const step = [1, 2, 5, 10, 15, 30, 60, 120, 300].find(v => (v / d) * w >= 60) ?? 300;
    g.font = '11px system-ui'; g.fillStyle = muted;
    for (let t = 0; t <= d; t += step) { g.fillRect(x(t), h - 18, 1, 5); g.fillText(fmtDuration(t), x(t) + 3, h - 4); }

    g.fillStyle = text; g.fillRect(x(this.time()), 0, 2, h);   // playhead
  }

  tick() {
    cancelAnimationFrame(this.raf);
    const step = () => {
      const p = this.player()?.nativeElement;
      if (!p) return;
      const s = this.sel();
      if (this.loop() && s && !p.paused && p.currentTime >= s.b) p.currentTime = s.a;
      this.time.set(p.currentTime);
      this.follow();
      this.draw();
      if (!p.paused) this.raf = requestAnimationFrame(step);
    };
    this.raf = requestAnimationFrame(step);
  }

  private follow() {   // keep the playhead visible when zoomed in
    const sc = this.scroller()?.nativeElement, cv = this.cv()?.nativeElement;
    if (!sc || !cv) return;
    const px = (this.time() / (this.duration() || 1)) * (parseFloat(cv.style.width) || 1);
    if (px < sc.scrollLeft || px > sc.scrollLeft + sc.clientWidth - 40) sc.scrollLeft = Math.max(0, px - 40);
  }

  private tAt(ev: PointerEvent) {
    const cv = this.cv()!.nativeElement, r = cv.getBoundingClientRect();
    return { x: ev.clientX - r.left, t: Math.min(this.duration(), Math.max(0, ((ev.clientX - r.left) / r.width) * this.duration())) };
  }
  down(ev: PointerEvent) {
    (ev.target as HTMLElement).setPointerCapture(ev.pointerId);
    const p = this.tAt(ev);
    this.drag = { x0: p.x, t0: p.t, moved: false };
  }
  move(ev: PointerEvent) {
    if (!this.drag) return;
    const p = this.tAt(ev);
    if (Math.abs(p.x - this.drag.x0) > 4) this.drag.moved = true;
    if (this.drag.moved) { this.sel.set({ a: Math.min(this.drag.t0, p.t), b: Math.max(this.drag.t0, p.t) }); this.activeId.set(null); this.queueDraw(); }
  }
  up(ev: PointerEvent) {
    const d = this.drag; this.drag = null;
    if (!d || d.moved) return;
    this.seekTo(this.tAt(ev).t);   // a plain click only moves the playhead
  }

  // --- player ---
  toggle() { const p = this.player()?.nativeElement; if (p) p.paused ? void p.play() : p.pause(); }
  skip(sec: number) { const p = this.player()?.nativeElement; if (p) this.seekTo(p.currentTime + sec); }
  seekTo(t: number) {
    const p = this.player()?.nativeElement;
    if (!p) return;
    p.currentTime = Math.min(Math.max(0, t), this.duration() || t);
    this.time.set(p.currentTime);
    this.queueDraw();
  }
  setRate(r: number) { this.rate.set(r); const p = this.player()?.nativeElement; if (p) p.playbackRate = r; }
  setZoom(z: number) { this.zoom.set(Math.min(32, Math.max(1, z))); this.layout(); }

  // --- selection & labels ---
  mark(which: 'a' | 'b') {
    const t = +this.time().toFixed(1), s = this.sel() ?? { a: t, b: t };
    this.sel.set(which === 'a' ? { a: t, b: Math.max(t, s.b) } : { a: Math.min(s.a, t), b: t });
    this.activeId.set(null); this.queueDraw();
  }
  clearSel() { this.sel.set(null); this.activeId.set(null); this.queueDraw(); }
  select(a: Annotation) {
    this.sel.set({ a: a.startSeconds, b: a.endSeconds }); this.activeId.set(a.id);
    this.seekTo(a.startSeconds); this.queueDraw();
  }

  loadAnns() { this.api.annotations(this.id()).subscribe({ next: v => { this.anns.set(v); this.queueDraw(); }, error: e => this.toast.err(e) }); }

  label(category: number) {
    const s = this.sel(), r = this.rec();
    if (!r || !this.audioUrl()) return;
    if (!s || s.b <= s.a) { this.toast.show('Önce dalga formunda bir aralık seçin'); return; }
    this.busy.set(true);
    this.api.createAnnotation({
      recordingId: r.id, visitId: r.visitId, raterIndex: +this.raterIndex || 1, category,
      severity: this.severity() || null, confidence: null,
      startSeconds: +s.a.toFixed(2), endSeconds: +s.b.toFixed(2), segmentText: null, note: null, annotationVersion: '1.0'
    }).subscribe({
      next: () => { this.busy.set(false); this.toast.ok(`${this.cats[category]} kaydedildi`); this.clearSel(); this.loadAnns(); },
      error: e => { this.busy.set(false); this.toast.err(e); }
    });
  }

  saveText() {
    this.busy.set(true);
    this.api.saveTranscript(this.id(), this.editText).subscribe({
      next: r => { this.busy.set(false); this.rec.set(r); this.editing.set(false); this.toast.ok('Transkript kaydedildi'); },
      error: e => { this.busy.set(false); this.toast.err(e); }
    });
  }

  remove(a: Annotation) {
    this.api.deleteAnnotation(a.id).subscribe({
      next: () => { this.toast.ok('Etiket silindi'); if (this.activeId() === a.id) this.clearSel(); this.loadAnns(); },
      error: e => this.toast.err(e)
    });
  }
}

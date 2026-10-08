import { Component, ElementRef, OnDestroy, OnInit, inject, input, signal, viewChild } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { ANNOTATION_CATEGORIES, Annotation, Recording } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, StatusBadge, fmtDuration } from '../shared/ui';

@Component({
  selector: 'app-annotate',
  imports: [FormsModule, RouterLink, IconComponent, Empty, StatusBadge],
  template: `
    <div class="page">
      <a routerLink="/recordings" class="muted">← Kayıtlar</a>
      <div class="page-head" style="margin-top:10px">
        <div><h1>Kör anotasyon</h1><p>Yalnızca kendi etiketlerinizi görürsünüz; AI çıktıları ve diğer değerlendiricilerin etiketleri gizlidir.</p></div>
        @if (rec()) { <app-status [status]="rec()!.analysisStatus" /> }
      </div>

      @if (!rec()) { <div class="skeleton" style="height:160px"></div> } @else {
        <div class="grid" style="grid-template-columns:minmax(0,1.5fr) minmax(300px,1fr);gap:16px;align-items:start" id="ws">
          <div class="stack">
            <section class="card">
              <header><h2>Ses</h2><span class="muted">{{ dur(rec()!.recordingDurationSeconds) }}</span></header>
              @if (audioUrl()) { <audio #player [src]="audioUrl()!" controls preload="metadata" style="width:100%"></audio> }
              @else { <div class="notice warn">{{ rec()!.hasAudio ? 'Ses yükleniyor…' : 'Bu kayıtta ses dosyası yok.' }}</div> }
              <div class="row" style="margin-top:12px">
                <button class="btn sm" (click)="mark('start')"><app-icon name="play" [size]="14" /> Başlangıç = şimdi</button>
                <button class="btn sm" (click)="mark('end')">Bitiş = şimdi</button>
                <span class="muted">Klavye: boşluk oynat/durdur</span>
              </div>
            </section>

            <section class="card">
              <header><h2>Transkript</h2></header>
              @if (rec()!.transcriptText) { <p style="white-space:pre-wrap;line-height:1.7">{{ rec()!.transcriptText }}</p> }
              @else { <p class="muted">Transkript henüz yok. Kayıtlar sayfasından analizi başlatın.</p> }
            </section>

            <section class="card flush">
              <header style="padding:16px 20px 0"><h2>Etiketlerim ({{ anns().length }})</h2></header>
              @if (!anns().length) { <app-empty icon="tag" text="Henüz etiket eklemediniz" /> } @else {
                <div class="table-wrap"><table class="table">
                  <thead><tr><th>Kategori</th><th>Aralık</th><th>Şiddet</th><th>Not</th></tr></thead>
                  <tbody>@for (a of anns(); track a.id) {
                    <tr class="clickable" (click)="seek(a.startSeconds)"><td><b>{{ cats[a.category] }}</b></td><td class="mono">{{ a.startSeconds }}–{{ a.endSeconds }} sn</td><td>{{ a.severity || '-' }}</td><td>{{ a.note || a.segmentText || '-' }}</td></tr>
                  }</tbody>
                </table></div>
              }
            </section>
          </div>

          <aside class="card" style="position:sticky;top:16px">
            <header><h2>Yeni etiket</h2></header>
            <form (ngSubmit)="save()" class="form-grid" style="grid-template-columns:1fr 1fr">
              <label class="field wide">Kategori
                <select class="input" name="c" [(ngModel)]="f.category">@for (c of cats; track $index) { <option [ngValue]="$index">{{ c }}</option> }</select>
              </label>
              <label class="field">Başlangıç (sn)<input class="input" name="s" type="number" step="0.1" min="0" [(ngModel)]="f.startSeconds" required /></label>
              <label class="field">Bitiş (sn)<input class="input" name="e" type="number" step="0.1" min="0" [(ngModel)]="f.endSeconds" required /></label>
              <label class="field">Şiddet
                <select class="input" name="sv" [(ngModel)]="f.severity"><option value="">—</option><option>Hafif</option><option>Orta</option><option>Ağır</option></select>
              </label>
              <label class="field">Güven (0-1)<input class="input" name="cf" type="number" step="0.1" min="0" max="1" [(ngModel)]="f.confidence" /></label>
              <label class="field wide">Değerlendirici sırası<input class="input" name="r" type="number" min="1" [(ngModel)]="f.raterIndex" /></label>
              <label class="field wide">Segment metni<textarea class="input" name="t" [(ngModel)]="f.segmentText"></textarea></label>
              <label class="field wide">Not<textarea class="input" name="n" [(ngModel)]="f.note"></textarea></label>
              <button class="btn primary wide" type="submit" [disabled]="busy() || f.endSeconds < f.startSeconds || f.endSeconds === null"><app-icon name="check" /> Kaydet</button>
            </form>
          </aside>
        </div>
      }
    </div>`,
  styles: ['@media (max-width: 900px) { #ws { grid-template-columns: 1fr !important; } aside { position: static !important; } }']
})
export class AnnotatePage implements OnInit, OnDestroy {
  id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(ToastService);
  player = viewChild<ElementRef<HTMLAudioElement>>('player');

  rec = signal<Recording | null>(null);
  anns = signal<Annotation[]>([]);
  audioUrl = signal<string | null>(null);
  busy = signal(false);
  cats = ANNOTATION_CATEGORIES; dur = fmtDuration;
  f: any = { category: 0, startSeconds: 0, endSeconds: 0, severity: '', confidence: null, raterIndex: 1, segmentText: '', note: '' };
  private onKey = (e: KeyboardEvent) => {
    const el = e.target as HTMLElement;
    if (e.code === 'Space' && !['INPUT', 'TEXTAREA', 'SELECT', 'BUTTON'].includes(el.tagName)) {
      e.preventDefault();
      const p = this.player()?.nativeElement;
      if (p) p.paused ? p.play() : p.pause();
    }
  };

  ngOnInit() {
    window.addEventListener('keydown', this.onKey);
    this.api.recording(this.id()).subscribe({
      next: r => {
        this.rec.set(r);
        // <audio> cannot send the bearer token, so the audio is fetched as a blob.
        if (r.hasAudio) this.api.audio(r.id).subscribe({ next: b => this.audioUrl.set(URL.createObjectURL(b)), error: e => this.toast.err(e) });
      },
      error: e => this.toast.err(e)
    });
    this.loadAnns();
  }
  ngOnDestroy() {
    window.removeEventListener('keydown', this.onKey);
    const u = this.audioUrl(); if (u) URL.revokeObjectURL(u);
  }

  loadAnns() { this.api.annotations(this.id()).subscribe({ next: v => this.anns.set(v), error: e => this.toast.err(e) }); }
  mark(which: 'start' | 'end') {
    const t = +(this.player()?.nativeElement.currentTime ?? 0).toFixed(1);
    if (which === 'start') { this.f.startSeconds = t; if (this.f.endSeconds < t) this.f.endSeconds = t; } else this.f.endSeconds = t;
  }
  seek(t: number) { const p = this.player()?.nativeElement; if (p) { p.currentTime = t; p.play(); } }

  save() {
    const r = this.rec()!;
    this.busy.set(true);
    this.api.createAnnotation({
      recordingId: r.id, visitId: r.visitId, raterIndex: +this.f.raterIndex || 1, category: this.f.category,
      severity: this.f.severity || null, confidence: this.f.confidence === '' ? null : this.f.confidence,
      startSeconds: +this.f.startSeconds, endSeconds: +this.f.endSeconds, segmentText: this.f.segmentText || null, note: this.f.note || null, annotationVersion: '1.0'
    }).subscribe({
      next: () => { this.busy.set(false); this.toast.ok('Etiket kaydedildi'); this.f.segmentText = ''; this.f.note = ''; this.loadAnns(); },
      error: e => { this.busy.set(false); this.toast.err(e); }
    });
  }
}

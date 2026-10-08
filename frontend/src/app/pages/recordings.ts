import { Component, OnDestroy, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AnalysisInfo, AUDIO_QUALITY, Participant, Recording, Stimulus, VISIT_TYPES, Visit } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal, StatusBadge, fmtDateTime, fmtDuration } from '../shared/ui';

@Component({
  selector: 'app-recordings',
  imports: [FormsModule, RouterLink, IconComponent, Modal, Empty, StatusBadge],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>Ses kayıtları</h1><p>Yükleyin, analiz ettirin ve anotasyona açın. AI çıktıları yalnızca aday ölçümdür.</p></div>
        <button class="btn primary" (click)="openUpload()"><app-icon name="upload" /> Kayıt yükle</button>
      </div>

      <div class="card" style="margin-bottom:16px">
        <div class="form-grid">
          <label class="field">Katılımcı
            <select class="input" [ngModel]="pid()" (ngModelChange)="pickParticipant($event)">
              <option value="">Tümü</option>
              @for (p of participants(); track p.id) { <option [value]="p.id">{{ p.participantCode }}</option> }
            </select>
          </label>
          <label class="field">Ziyaret
            <select class="input" [ngModel]="vid()" (ngModelChange)="pickVisit($event)" [disabled]="!pid() && !vid()">
              <option value="">Tümü</option>
              @for (v of visits(); track v.id) { <option [value]="v.id">{{ vt[v.visitType] }} · {{ v.id.slice(0, 6) }}</option> }
              @if (vid() && !hasVisit()) { <option [value]="vid()">Seçili ziyaret</option> }
            </select>
          </label>
        </div>
      </div>

      <div class="card flush">
        @if (loading()) { <div style="padding:20px"><div class="skeleton" style="height:120px"></div></div> }
        @else if (!recs().length) { <app-empty icon="mic" text="Kayıt bulunamadı" /> }
        @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Kayıt</th><th>Süre</th><th>Kalite</th><th>Analiz</th><th>Eklenme</th><th class="right">İşlem</th></tr></thead>
            <tbody>
              @for (r of recs(); track r.id) {
                <tr>
                  <td><span class="mono">{{ r.id.slice(0, 8) }}</span><br><span class="muted">Uyarıcı v{{ r.stimulusVersion }}</span></td>
                  <td>{{ dur(r.recordingDurationSeconds) }}</td>
                  <td>{{ q[r.audioQuality] }}</td>
                  <td><app-status [status]="r.analysisStatus" />
                    @if (r.analysisError) { <br><span class="muted" style="font-size:.78rem" [title]="r.analysisError">{{ r.analysisError.slice(0, 40) }}</span> }</td>
                  <td class="muted">{{ r.analyzedAt ? dt(r.analyzedAt) : '-' }}</td>
                  <td class="right"><div class="row" style="justify-content:flex-end;gap:6px">
                    <a class="btn sm" [routerLink]="['/annotate', r.id]" title="Anotasyon"><app-icon name="tag" /></a>
                    <button class="btn sm" (click)="showAnalysis(r)" [disabled]="r.analysisStatus !== 3" title="Ölçümleri gör"><app-icon name="eye" /></button>
                    <button class="btn sm" (click)="analyze(r)" [disabled]="!r.hasAudio || r.analysisStatus === 1 || r.analysisStatus === 2" title="Analiz et">
                      <app-icon [name]="r.analysisStatus === 3 ? 'refresh' : 'play'" /></button>
                    <button class="btn sm danger" (click)="del.set(r)" title="Sil"><app-icon name="trash" /></button>
                  </div></td>
                </tr>
              }
            </tbody>
          </table></div>
        }
      </div>
    </div>

    @if (upOpen()) {
      <app-modal title="Ses kaydı yükle" (close)="upOpen.set(false)">
        <form id="uf" (ngSubmit)="upload()" class="form-grid">
          <label class="field">Katılımcı *
            <select class="input" name="p" [ngModel]="uf.pid" (ngModelChange)="uploadParticipant($event)" required>
              <option value="" disabled>Seçin…</option>
              @for (p of participants(); track p.id) { <option [value]="p.id">{{ p.participantCode }}{{ p.consentGivenAt && !p.consentWithdrawnAt ? '' : ' (onam yok)' }}</option> }
            </select>
          </label>
          <label class="field">Ziyaret *
            <select class="input" name="v" [(ngModel)]="uf.visitId" required [disabled]="!uf.pid">
              <option value="" disabled>Seçin…</option>
              @for (v of uploadVisits(); track v.id) { <option [value]="v.id">{{ vt[v.visitType] }} · {{ v.id.slice(0, 6) }}</option> }
            </select>
          </label>
          <label class="field">Uyarıcı *
            <select class="input" name="s" [ngModel]="uf.stimulusId" (ngModelChange)="pickStimulus($event)" required>
              <option value="" disabled>Seçin…</option>
              @for (s of stimuli(); track s.id) { <option [value]="s.id">{{ s.stimulusId }} v{{ s.version }}</option> }
            </select>
          </label>
          <label class="field">Yönerge sürümü *<input class="input" name="i" [(ngModel)]="uf.instructionVersion" required /></label>
          <label class="field wide">Ses dosyası * (wav, mp3, m4a, ogg, flac, webm · en fazla 200 MB)
            <input class="input" name="f" type="file" accept=".wav,.mp3,.m4a,.ogg,.flac,.webm,audio/*" (change)="pickFile($event)" required />
          </label>
          @if (uf.duration) { <div class="muted wide">Algılanan süre: {{ dur(uf.duration) }}</div> }
          @if (!stimuli().length) { <div class="notice warn wide">Önce <a routerLink="/research">Araştırma</a> sayfasında protokol ve uyarıcı ekleyin.</div> }
          @if (progress() !== null) { <div class="progress wide"><i [style.width.%]="progress()"></i></div> }
        </form>
        <ng-container footer>
          <button class="btn" (click)="upOpen.set(false)" [disabled]="busy()">Vazgeç</button>
          <button class="btn primary" type="submit" form="uf" [disabled]="busy() || !file || !uf.visitId || !uf.stimulusId || !uf.instructionVersion">
            @if (busy()) { <app-icon name="refresh" class="spin" /> } Yükle
          </button>
        </ng-container>
      </app-modal>
    }

    @if (ana()) {
      <app-modal title="Analiz sonucu (aday ölçümler)" [wide]="true" (close)="ana.set(null)">
        <div class="notice warn" style="margin-bottom:14px">Bu değerler otomatik üretilmiş adaylardır; klinik olarak doğrulanmış etiket değildir.</div>
        @if (anaRec()?.transcriptText) { <h3 style="margin-bottom:6px">Transkript</h3><p class="card" style="background:var(--surface-2);margin-bottom:16px;white-space:pre-wrap">{{ anaRec()!.transcriptText }}</p> }
        <div class="table-wrap"><table class="table">
          <thead><tr><th>Özellik</th><th class="right">Değer</th><th>Aday</th></tr></thead>
          <tbody>@for (m of ana()!.measurements; track m.featureName) {
            <tr><td class="mono">{{ m.featureName }}</td><td class="right"><b>{{ m.numericValue ?? m.textValue ?? '-' }}</b></td><td>@if (m.isCandidateAnnotation) { <span class="badge warn">aday</span> }</td></tr>
          }</tbody>
        </table></div>
      </app-modal>
    }

    @if (del()) {
      <app-modal title="Kaydı sil" (close)="del.set(null)">
        <p>Ses dosyası ve transkript kalıcı olarak silinecek. Devam edilsin mi?</p>
        <ng-container footer><button class="btn" (click)="del.set(null)">Vazgeç</button><button class="btn danger" (click)="remove()"><app-icon name="trash" /> Sil</button></ng-container>
      </app-modal>
    }`
})
export class RecordingsPage implements OnInit, OnDestroy {
  visit = input<string>(); // ?visit=<id> via withComponentInputBinding
  private api = inject(ApiService);
  private toast = inject(ToastService);

  participants = signal<Participant[]>([]);
  visits = signal<Visit[]>([]);
  uploadVisits = signal<Visit[]>([]);
  stimuli = signal<Stimulus[]>([]);
  recs = signal<Recording[]>([]);
  pid = signal(''); vid = signal('');
  loading = signal(true);
  upOpen = signal(false); busy = signal(false); progress = signal<number | null>(null);
  ana = signal<AnalysisInfo | null>(null); anaRec = signal<Recording | null>(null);
  del = signal<Recording | null>(null);
  vt = VISIT_TYPES; q = AUDIO_QUALITY; dur = fmtDuration; dt = fmtDateTime;
  uf: any = {}; file: File | null = null;
  private timer: any;

  hasVisit = () => this.visits().some(v => v.id === this.vid());

  ngOnInit() {
    this.api.participants().subscribe({ next: v => this.participants.set(v), error: e => this.toast.err(e) });
    if (this.visit()) this.vid.set(this.visit()!);
    this.load();
    this.timer = setInterval(() => { if (this.recs().some(r => r.analysisStatus === 1 || r.analysisStatus === 2)) this.load(true); }, 4000);
  }
  ngOnDestroy() { clearInterval(this.timer); }

  load(quiet = false) {
    if (!quiet) this.loading.set(true);
    this.api.recordings(this.vid() || undefined).subscribe({
      next: v => { this.recs.set(v); this.loading.set(false); },
      error: e => { this.loading.set(false); if (!quiet) this.toast.err(e); }
    });
  }
  pickParticipant(id: string) {
    this.pid.set(id); this.vid.set(''); this.visits.set([]);
    if (id) this.api.visits(id).subscribe({ next: v => this.visits.set(v), error: e => this.toast.err(e) });
    this.load();
  }
  pickVisit(id: string) { this.vid.set(id); this.load(); }

  analyze(r: Recording) {
    this.api.analyze(r.id).subscribe({ next: () => { this.toast.ok('Analiz kuyruğa alındı'); this.load(true); }, error: e => this.toast.err(e) });
  }
  showAnalysis(r: Recording) {
    this.api.analysis(r.id).subscribe({ next: a => { this.anaRec.set(r); this.ana.set(a); }, error: e => this.toast.err(e) });
  }
  remove() {
    const r = this.del()!;
    this.api.deleteRecording(r.id).subscribe({ next: () => { this.del.set(null); this.toast.ok('Kayıt silindi'); this.load(); }, error: e => this.toast.err(e) });
  }

  openUpload() {
    this.uf = { pid: this.pid(), visitId: this.vid(), stimulusId: '', instructionVersion: '1', stimulusVersion: '', duration: 0 };
    this.file = null; this.progress.set(null);
    this.uploadVisits.set(this.visits());
    this.upOpen.set(true);
    this.api.stimuli().subscribe({ next: v => this.stimuli.set(v), error: e => this.toast.err(e) });
  }
  uploadParticipant(id: string) {
    this.uf.pid = id; this.uf.visitId = ''; this.uploadVisits.set([]);
    this.api.visits(id).subscribe({ next: v => this.uploadVisits.set(v), error: e => this.toast.err(e) });
  }
  pickStimulus(id: string) { this.uf.stimulusId = id; this.uf.stimulusVersion = this.stimuli().find(s => s.id === id)?.version ?? ''; }
  pickFile(ev: Event) {
    this.file = (ev.target as HTMLInputElement).files?.[0] ?? null;
    this.uf.duration = 0;
    if (!this.file) return;
    // Read duration from the file itself so the researcher does not type it.
    const url = URL.createObjectURL(this.file);
    const a = new Audio(url);
    a.onloadedmetadata = () => { this.uf.duration = isFinite(a.duration) ? a.duration : 0; URL.revokeObjectURL(url); };
    a.onerror = () => URL.revokeObjectURL(url);
  }

  upload() {
    if (!this.file) return;
    const f = new FormData();
    f.append('file', this.file);
    f.append('visitId', this.uf.visitId);
    f.append('stimulusId', this.uf.stimulusId);
    f.append('stimulusVersion', this.uf.stimulusVersion || '1');
    f.append('instructionVersion', this.uf.instructionVersion);
    f.append('recordingDurationSeconds', String(this.uf.duration || 0));
    this.busy.set(true); this.progress.set(40);
    this.api.upload(f).subscribe({
      next: r => {
        this.busy.set(false); this.upOpen.set(false);
        this.toast.ok('Kayıt yüklendi');
        this.api.analyze(r.id).subscribe({ next: () => { this.toast.show('Analiz otomatik başlatıldı'); this.load(true); }, error: () => {} });
        this.load();
      },
      error: e => { this.busy.set(false); this.progress.set(null); this.toast.err(e?.status === 409 ? 'Bu katılımcının aktif onamı yok; önce onam kaydedin.' : e); }
    });
  }
}

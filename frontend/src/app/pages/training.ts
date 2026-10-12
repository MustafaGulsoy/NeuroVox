import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { ApiService } from '../core/api.service';
import { TrainingRun } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal } from '../shared/ui';

const RUN: Record<number, [string, string]> = { 1: ['Sırada', 'warn'], 2: ['Çalışıyor', 'info'], 3: ['Tamamlandı', 'ok'], 4: ['Başarısız', 'err'] };

@Component({
  selector: 'app-training',
  imports: [DatePipe, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>Model eğitimi</h1><p>Bu kurumdaki klinik sonucu kayıtlı ziyaretlerden (en az 10 satır ve 2 sınıf) beş model eğitilir; katılımcılar eğitim/test arasında bölünmez.</p></div>
        <button class="btn primary" (click)="train()" [disabled]="running()"><app-icon name="play" /> Eğitimi başlat</button>
      </div>
      <div class="card flush">
        @if (!runs().length) { <app-empty icon="chart" text="Henüz eğitim yapılmadı" /> } @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Başlangıç</th><th>Durum</th><th>Örnek</th><th>Sonuç</th><th>Not</th></tr></thead>
            <tbody>@for (r of runs(); track r.id) {
              <tr><td>{{ r.rowCreatedDate | date: 'dd.MM HH:mm' }}</td>
                <td><span class="badge" [class]="cls(r)">{{ label(r) }}</span></td>
                <td>{{ r.sampleCount || '—' }}</td>
                <td>@if (r.report) { <button class="btn sm" (click)="detail.set(r)"><app-icon name="eye" /> Ayrıntı</button> } @else { — }</td>
                <td class="muted">{{ r.error || '' }}</td></tr>
            }</tbody>
          </table></div>
        }
      </div>
      <div class="notice" style="margin-top:16px">Yapay zekâ sunucusu gerektiğinde kendiliğinden açılır; iş sırada birkaç dakika bekleyebilir. Sonuçlar araştırma amaçlıdır, klinik tanı yerine geçmez.</div>
    </div>

    @if (detail(); as r) {
      <app-modal title="Eğitim sonucu" (close)="detail.set(null)" [wide]="true">
        <p class="muted">{{ r.report!.n_participants }} katılımcı · {{ r.report!.n_rows }} ziyaret · {{ r.report!.folds }} katlı, katılımcı gruplu çapraz doğrulama (dışarıda bırakılan tahminler). Artefakt seti: <b>{{ setLabel(r.report!.primary_set) }}</b>.</p>
        @for (s of sets(r); track s[0]) {
          <h3 style="margin:14px 0 6px">{{ setLabel(s[0]) }} <span class="muted" style="font-weight:400">({{ s[1].features.length }} özellik)</span></h3>
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Model</th><th>AUC (%95 GA)</th><th>Duyarlılık</th><th>Özgüllük</th><th>Doğruluk</th></tr></thead>
            <tbody>@for (m of entries(s[1].models); track m[0]) {
              <tr><td>{{ m[0] }}</td><td><b>{{ m[1].roc_auc }}</b> @if (m[1].roc_auc_ci95) { <span class="muted">({{ m[1].roc_auc_ci95![0] }}–{{ m[1].roc_auc_ci95![1] }})</span> }</td>
                <td>{{ m[1].sensitivity ?? '—' }}</td><td>{{ m[1].specificity ?? '—' }}</td><td>{{ m[1].accuracy }}</td></tr>
            }</tbody>
          </table></div>
        }
        <div class="notice" style="margin-top:12px">{{ r.report!.note }}</div>
        <ng-container footer><button class="btn primary" (click)="detail.set(null)">Kapat</button></ng-container>
      </app-modal>
    }`
})
export class TrainingPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  runs = signal<TrainingRun[]>([]);
  detail = signal<TrainingRun | null>(null);

  constructor() {
    const t = setInterval(() => this.load(), 10000);   // queued/running jobs finish without user action
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }
  ngOnInit() { this.load(); }
  load() { this.api.trainingRuns().subscribe({ next: r => this.runs.set(r), error: e => this.toast.err(e) }); }

  running = () => this.runs().some(r => r.status === 1 || r.status === 2);
  label = (r: TrainingRun) => RUN[r.status]?.[0] ?? '—';
  cls = (r: TrainingRun) => RUN[r.status]?.[1] ?? '';
  sets = (r: TrainingRun) => Object.entries(r.report?.feature_sets ?? {});
  entries = <T,>(o: Record<string, T>) => Object.entries(o);
  setLabel = (n: string) => ({ speech: 'Yalnız konuşma', cognitive: 'Yalnız ACE-III', combined: 'Konuşma + ACE-III' } as Record<string, string>)[n] ?? n;
  train() { this.api.startTraining().subscribe({ next: () => { this.toast.ok('Eğitim sıraya alındı'); this.load(); }, error: e => this.toast.err(e) }); }
}

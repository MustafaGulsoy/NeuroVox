import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { GroupComparison, Participant, RaterAgreement } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { LineChart, Point } from '../shared/line-chart.component';
import { download, fmtDate } from '../shared/ui';

const DOMAINS: [string, string][] = [['total', 'ACE-III toplam'], ['attention', 'Dikkat'], ['memory', 'Bellek'], ['fluency', 'Akıcılık'], ['language', 'Dil'], ['visuospatial', 'Görsel-uzamsal']];

@Component({
  selector: 'app-analysis',
  imports: [FormsModule, IconComponent, LineChart],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>Analiz</h1><p>Konuşma özellikleri ile bilişsel puanlar arasındaki ilişki ve zaman içindeki değişim.</p></div>
        <button class="btn" (click)="exportCsv()" [disabled]="exporting()"><app-icon name="download" /> Eğitim verisi (CSV)</button>
      </div>

      <div class="grid cols-2">
        <section class="card">
          <header><h2>Özellik – ACE-III ilişkisi</h2></header>
          <div class="form-grid" style="grid-template-columns:1fr 1fr">
            <label class="field">Konuşma özelliği
              <select class="input" [(ngModel)]="feat"><option value="" disabled>Seçin…</option>@for (n of featureNames(); track n) { <option>{{ n }}</option> }</select>
            </label>
            <label class="field">Bilişsel puan
              <select class="input" [(ngModel)]="domain">@for (d of domains; track d[0]) { <option [value]="d[0]">{{ d[1] }}</option> }</select>
            </label>
          </div>
          <div class="row" style="margin-top:14px"><button class="btn primary" (click)="assoc()" [disabled]="!feat">Hesapla</button></div>
          @if (a(); as a) {
            <div class="grid" style="grid-template-columns:repeat(3,1fr);margin-top:16px">
              <div class="stat"><span class="lbl">Örneklem (n)</span><span class="num">{{ a.n }}</span></div>
              <div class="stat"><span class="lbl">Pearson r</span><span class="num">{{ fmt(a.pearson) }}</span></div>
              <div class="stat"><span class="lbl">Spearman ρ</span><span class="num">{{ fmt(a.spearman) }}</span></div>
            </div>
            <p class="muted" style="margin-top:12px">İlişkilendirme nedensellik göstermez. {{ a.n < 2 ? 'Hesap için en az 2 eşleşen ziyaret gerekir.' : '' }}</p>
          }
        </section>

        <section class="card">
          <header><h2>Boylamsal değişim</h2></header>
          <div class="form-grid" style="grid-template-columns:1fr 1fr">
            <label class="field">Katılımcı
              <select class="input" [(ngModel)]="pid"><option value="" disabled>Seçin…</option>@for (p of participants(); track p.id) { <option [value]="p.id">{{ p.participantCode }}</option> }</select>
            </label>
            <label class="field">Özellik
              <select class="input" [(ngModel)]="lfeat"><option value="" disabled>Seçin…</option>@for (n of featureNames(); track n) { <option>{{ n }}</option> }</select>
            </label>
          </div>
          <div class="row" style="margin-top:14px"><button class="btn primary" (click)="longi()" [disabled]="!pid || !lfeat">Göster</button></div>
          @if (l(); as l) {
            @if (l.n < 2) { <div class="notice" style="margin-top:14px">Grafik için tarihli en az 2 ölçüm gerekir (şu an {{ l.n }}).</div> }
            @else {
              <app-line-chart [points]="pts()" title="Boylamsal değişim" />
              <div class="grid" style="grid-template-columns:repeat(3,1fr);margin-top:8px">
                <div class="stat"><span class="lbl">Mutlak değişim</span><span class="num" style="font-size:1.4rem">{{ fmt(l.absoluteChange) }}</span></div>
                <div class="stat"><span class="lbl">Yıllık değişim</span><span class="num" style="font-size:1.4rem">{{ fmt(l.annualizedChange) }}</span></div>
                <div class="stat"><span class="lbl">Aylık eğim</span><span class="num" style="font-size:1.4rem">{{ fmt(l.slopePerMonth) }}</span></div>
              </div>
            }
          }
        </section>
      </div>

      <section class="card" style="margin-top:16px">
        <header><h2>Grup karşılaştırması</h2>
          <div class="row">
            <select class="input" style="width:auto" [(ngModel)]="by" aria-label="Gruplama"><option value="outcome">AD'ye dönüşüm ↔ stabil MCI</option><option value="diagnosis">Hafif Alzheimer ↔ MCI (başlangıç)</option></select>
            <button class="btn primary" (click)="compare()" [disabled]="comparing()">{{ comparing() ? 'Hesaplanıyor…' : 'Hesapla' }}</button>
          </div></header>
        <p class="muted">Katılımcı düzeyinde (ziyaretler ortalanır). Her özellik için Shapiro-Wilk; iki grup da normalse Welch t-testi, değilse Mann-Whitney U. Çoklu karşılaştırma için Benjamini-Hochberg düzeltmeli p (q) gösterilir.</p>
        @if (cmp(); as c) {
          @if (c.note) { <div class="notice warn">{{ c.note }} ({{ c.groups.a.name }}: {{ c.groups.a.n }}, {{ c.groups.b.name }}: {{ c.groups.b.n }})</div> }
          @else {
            <p><b>{{ c.groups.a.name }}</b>: {{ c.groups.a.n }} katılımcı · <b>{{ c.groups.b.name }}</b>: {{ c.groups.b.n }} katılımcı</p>
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Özellik</th><th>{{ c.groups.a.name }} (medyan)</th><th>{{ c.groups.b.name }} (medyan)</th><th>Test</th><th>p</th><th>q (BH)</th><th>Etki büyüklüğü</th></tr></thead>
              <tbody>@for (r of c.results; track r.feature) {
                <tr><td class="mono">{{ r.feature }}</td>
                  <td>{{ fmt(r.a.median) }} <span class="muted">(n={{ r.a.n }})</span></td><td>{{ fmt(r.b.median) }} <span class="muted">(n={{ r.b.n }})</span></td>
                  <td>{{ r.test === 'welch_t' ? 'Welch t' : r.test === 'mann_whitney_u' ? 'Mann-Whitney U' : '—' }}</td>
                  <td [class.sig]="r.p != null && r.p < 0.05"><b>{{ r.p == null ? '—' : r.p.toFixed(4) }}</b></td>
                  <td>{{ r.p_adj == null ? '—' : r.p_adj.toFixed(4) }}</td>
                  <td>{{ r.effect == null ? '—' : (r.effect_name === 'cohen_d' ? 'd = ' : 'r = ') + r.effect.toFixed(2) }}</td></tr>
              }</tbody>
            </table></div>
          }
        }
      </section>

      <section class="card" style="margin-top:16px">
        <header><h2>Değerlendirici uyumu</h2><button class="btn primary" (click)="agreement()">Hesapla</button></header>
        @if (rater(); as r) {
          <p class="muted">{{ r.annotatedRecordings }} anotasyonlu kayıt, {{ r.recordingsWithTwoRaters }} tanesi en az iki değerlendiricili. {{ r.note }}</p>
          @if (!r.categories.length) { <div class="notice">Kappa için aynı kayıtları anote etmiş en az iki değerlendirici gerekir.</div> } @else {
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Kategori</th><th>Kayıt (n)</th><th>Yüzde uyum</th><th>Cohen kappa</th></tr></thead>
              <tbody>@for (c of r.categories; track c.category) {
                <tr><td>{{ c.category }}</td><td>{{ c.n }}</td><td>%{{ (c.percentAgreement * 100).toFixed(0) }}</td>
                  <td><b>{{ c.kappa == null ? '—' : c.kappa.toFixed(2) }}</b> <span class="muted">{{ kappaLabel(c.kappa) }}</span></td></tr>
              }</tbody>
            </table></div>
          }
          <h3 style="margin-top:18px">Yapay zekâ adayı ↔ insan anotasyonu</h3>
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Kategori</th><th>YZ özelliği</th><th>Kayıt (n)</th><th>Spearman ρ</th></tr></thead>
            <tbody>@for (a of r.aiVsHuman; track a.category) { <tr><td>{{ a.category }}</td><td class="mono">{{ a.feature }}</td><td>{{ a.n }}</td><td><b>{{ a.spearman == null ? '—' : a.spearman.toFixed(2) }}</b></td></tr> }</tbody>
          </table></div>
        }
      </section>
    </div>`
})
export class AnalysisPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  domains = DOMAINS;
  featureNames = signal<string[]>([]);
  participants = signal<Participant[]>([]);
  feat = ''; domain = 'total'; pid = ''; lfeat = '';
  a = signal<{ n: number; pearson: number | null; spearman: number | null } | null>(null);
  l = signal<any>(null);
  pts = signal<Point[]>([]);
  exporting = signal(false);
  by = 'outcome'; comparing = signal(false);
  cmp = signal<GroupComparison | null>(null);
  rater = signal<RaterAgreement | null>(null);

  compare() {
    this.comparing.set(true);
    this.api.groupComparison(this.by).subscribe({ next: r => { this.comparing.set(false); this.cmp.set(r); }, error: e => { this.comparing.set(false); this.toast.err(e); } });
  }
  agreement() { this.api.raterAgreement().subscribe({ next: r => this.rater.set(r), error: e => this.toast.err(e) }); }
  kappaLabel(k: number | null) { return k == null ? '' : k >= .81 ? 'çok iyi' : k >= .61 ? 'iyi' : k >= .41 ? 'orta' : k >= .21 ? 'zayıf' : 'yetersiz'; }

  fmt = (v: number | null | undefined) => (v == null ? '—' : (+v).toFixed(3));

  ngOnInit() {
    this.api.features().subscribe({ next: v => this.featureNames.set([...new Set(v.map(x => x.featureName))].sort()), error: e => this.toast.err(e) });
    this.api.participants().subscribe({ next: v => this.participants.set(v), error: e => this.toast.err(e) });
  }
  assoc() { this.api.association(this.feat, this.domain).subscribe({ next: r => this.a.set(r), error: e => this.toast.err(e) }); }
  longi() {
    this.api.longitudinal(this.pid, this.lfeat).subscribe({
      next: r => {
        this.l.set(r);
        this.pts.set((r.points ?? []).map((p: any) => ({ label: fmtDate(p.date), value: p.value })));
      },
      error: e => this.toast.err(e)
    });
  }
  exportCsv() {
    this.exporting.set(true);
    this.api.trainingCsv().subscribe({
      next: b => { this.exporting.set(false); download(b, 'training-set.csv'); this.toast.ok('CSV indirildi'); },
      error: e => { this.exporting.set(false); this.toast.err(e); }
    });
  }
}

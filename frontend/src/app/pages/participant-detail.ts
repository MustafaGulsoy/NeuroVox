import { Component, OnInit, inject, input, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router, RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Ace, DIAGNOSES, Outcome, OUTCOME_TYPES, Participant, Protocol, VISIT_TYPES, Visit, VisitPrediction } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal, fmtDate, fmtDateTime } from '../shared/ui';

type Tab = 'visits' | 'ace' | 'outcomes';

@Component({
  selector: 'app-participant-detail',
  imports: [FormsModule, RouterLink, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <a routerLink="/participants" class="muted" style="display:inline-block;margin-bottom:10px">← Katılımcılar</a>
      @if (p(); as p) {
        <div class="page-head">
          <div>
            <h1>{{ p.participantCode }}</h1>
            <p>
              @if (p.consentWithdrawnAt) { <span class="badge err">Onam geri çekildi · {{ d(p.consentWithdrawnAt) }}</span> }
              @else if (p.consentGivenAt) { <span class="badge ok">Onam {{ p.consentVersion }} · {{ d(p.consentGivenAt) }}</span> }
              @else { <span class="badge warn">Onam yok</span> }
              @if (p.isEligible === true) { <span class="badge ok" style="margin-left:6px">Çalışmaya uygun</span> }
              @else if (p.isEligible === false) { <span class="badge err" style="margin-left:6px">Dahil etme/dışlama ölçütlerini karşılamıyor</span> }
              @else { <span class="badge warn" style="margin-left:6px">Uygunluk değerlendirilmedi</span> }
            </p>
          </div>
          <div class="row">
            <button class="btn" (click)="openElig()"><app-icon name="shield" /> Uygunluk</button>
            @if (!p.consentGivenAt || p.consentWithdrawnAt) { <button class="btn" (click)="consent.set('give')"><app-icon name="check" /> Onam kaydet</button> }
            @else { <button class="btn" (click)="withdraw()"><app-icon name="x" /> Onamı geri çek</button> }
            <button class="btn danger" (click)="eraseOpen.set(true)"><app-icon name="trash" /> Verileri sil</button>
          </div>
        </div>

        <div class="grid cols-2" style="margin-bottom:16px">
          <div class="card"><dl class="kv" style="margin:0">
            <dt>Başlangıç tanısı</dt><dd>{{ p.diagnosis == null ? '-' : dx[p.diagnosis] }}</dd>
            <dt>Cinsiyet</dt><dd>{{ p.sex || '-' }}</dd>
            <dt>Doğum tarihi</dt><dd>{{ d(p.dateOfBirth) }}</dd>
            <dt>Eklenme</dt><dd>{{ dt(p.rowCreatedDate) }}</dd>
            <dt>Notlar</dt><dd>{{ p.notes || '-' }}</dd>
          </dl></div>
        </div>

        <div class="tabs" role="tablist">
          <button class="tab" role="tab" [class.active]="tab() === 'visits'" (click)="tab.set('visits')">Ziyaretler ({{ visits().length }})</button>
          <button class="tab" role="tab" [class.active]="tab() === 'ace'" (click)="tab.set('ace')">ACE-III ({{ aces().length }})</button>
          <button class="tab" role="tab" [class.active]="tab() === 'outcomes'" (click)="tab.set('outcomes')">Klinik sonuç ({{ outcomes().length }})</button>
        </div>

        @if (tab() === 'visits') {
          <div class="card flush">
            <header style="padding:16px 20px 0"><h2>Ziyaretler</h2><button class="btn sm primary" (click)="openVisit()"><app-icon name="plus" /> Ziyaret ekle</button></header>
            @if (!visits().length) { <app-empty icon="folder" text="Henüz ziyaret yok" /> } @else {
              <div class="table-wrap"><table class="table">
                <thead><tr><th>Tür</th><th>Planlanan</th><th>Gerçekleşen</th><th>Not</th><th></th></tr></thead>
                <tbody>@for (v of visits(); track v.id) {
                  <tr><td><b>{{ vt[v.visitType] }}</b></td><td>{{ d(v.scheduledDate) }}</td><td>{{ d(v.actualDate) }}</td><td>{{ v.notes || '-' }}</td>
                    <td class="right"><button class="btn sm" (click)="predict(v)" title="Eğitilmiş modelin araştırma amaçlı tahmini"><app-icon name="chart" /> Model tahmini</button>
                      <a class="btn sm" [routerLink]="['/recordings']" [queryParams]="{ visit: v.id }"><app-icon name="mic" /> Kayıtlar</a></td></tr>
                }</tbody>
              </table></div>
            }
          </div>
        }

        @if (tab() === 'ace') {
          <div class="card flush">
            <header style="padding:16px 20px 0"><h2>ACE-III değerlendirmeleri</h2><button class="btn sm primary" (click)="openAce()" [disabled]="!visits().length"><app-icon name="plus" /> Ekle</button></header>
            @if (!aces().length) { <app-empty icon="folder" [text]="visits().length ? 'Henüz değerlendirme yok' : 'Önce bir ziyaret ekleyin'" /> } @else {
              <div class="table-wrap"><table class="table">
                <thead><tr><th>Tarih</th><th>Sürüm</th><th>Toplam</th><th>Dikkat</th><th>Bellek</th><th>Akıcılık</th><th>Dil</th><th>Görsel-uzamsal</th></tr></thead>
                <tbody>@for (a of aces(); track a.id) {
                  <tr><td>{{ d(a.assessmentDate) }}</td><td>{{ a.assessmentVersion }}</td><td><b>{{ a.totalScore ?? '-' }}</b></td>
                    <td>{{ a.attentionScore ?? '-' }}</td><td>{{ a.memoryScore ?? '-' }}</td><td>{{ a.fluencyScore ?? '-' }}</td><td>{{ a.languageScore ?? '-' }}</td><td>{{ a.visuospatialScore ?? '-' }}</td></tr>
                }</tbody>
              </table></div>
            }
          </div>
        }

        @if (tab() === 'outcomes') {
          <div class="card flush">
            <header style="padding:16px 20px 0"><h2>Bağımsız klinik sonuç</h2><button class="btn sm primary" (click)="outOpen.set(true)"><app-icon name="plus" /> Ekle</button></header>
            <div style="padding:0 20px"><div class="notice">Tanı konuşmadan çıkarılmaz; bu kayıt bağımsız olarak belgelenmiş klinik sonuçtur.</div></div>
            @if (!outcomes().length) { <app-empty icon="folder" text="Henüz sonuç yok" /> } @else {
              <div class="table-wrap"><table class="table">
                <thead><tr><th>Sonuç</th><th>Tarih</th><th>Değerlendirici</th><th>Not</th></tr></thead>
                <tbody>@for (o of outcomes(); track o.id) { <tr><td><b>{{ ot[o.outcomeType] }}</b></td><td>{{ d(o.diagnosisDate) }}</td><td>{{ o.evaluator || '-' }}</td><td>{{ o.notes || '-' }}</td></tr> }</tbody>
              </table></div>
            }
          </div>
        }
      } @else { <div class="skeleton" style="height:140px"></div> }
    </div>

    @if (eligOpen()) {
      <app-modal title="Dahil etme / dışlama ölçütleri" (close)="eligOpen.set(false)" [wide]="true">
        <form id="ef" (ngSubmit)="saveElig()" class="form-grid">
          <label class="field wide">Başlangıç tanısı *<select class="input" name="dx" [(ngModel)]="ef.diagnosis" required>@for (t of dx; track $index) { <option [ngValue]="$index">{{ t }}</option> }</select></label>
          <div class="field wide"><b>Dahil etme</b></div>
          <label class="check wide"><input type="checkbox" name="w" [(ngModel)]="ef.willingFollowUp6Months" /> En az 6 aylık klinik izleme katılmaya istekli</label>
          <label class="check wide"><input type="checkbox" name="v" [(ngModel)]="ef.adequateVisionHearing" /> Yeterli görme ve işitme kapasitesi var</label>
          <div class="field wide"><b>Dışlama (varsa işaretleyin)</b></div>
          <label class="check wide"><input type="checkbox" name="m" [(ngModel)]="ef.severeMentalIllness" /> Ciddi psikiyatrik hastalık öyküsü</label>
          <label class="check wide"><input type="checkbox" name="n" [(ngModel)]="ef.severeNeurologicalDeficit" /> İleri nörolojik defisit veya başka ciddi nörolojik hastalık</label>
          <label class="check wide"><input type="checkbox" name="l" [(ngModel)]="ef.languageBarrier" /> Dil engeli nedeniyle konuşma analizi yapılamıyor</label>
          <div class="notice wide">Uygun değerlendirilmeyen katılımcıdan ses kaydı alınamaz ve eğitim/analiz verisine girmez.</div>
        </form>
        <ng-container footer><button class="btn" (click)="eligOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="ef" [disabled]="ef.diagnosis == null">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (pred()) {
      <app-modal title="Model tahmini (araştırma amaçlı)" (close)="pred.set(null)">
        @if (pred()!.modelEstimatedRisk != null) {
          <div class="stat"><span class="lbl">Modelin tahmini AD'ye dönüşüm olasılığı</span><span class="num">%{{ (pred()!.modelEstimatedRisk! * 100).toFixed(0) }}</span></div>
        }
        <p class="muted" style="margin-top:12px">{{ pred()!.featuresUsed }} / {{ pred()!.featuresExpected }} özellik kullanıldı.
          @if (pred()!.missing.length) { Eksik: {{ pred()!.missing.join(', ') }} (eğitim verisinin medyanıyla dolduruldu). }</p>
        <div class="notice warn">{{ pred()!.disclaimer }}</div>
        <ng-container footer><button class="btn primary" (click)="pred.set(null)">Kapat</button></ng-container>
      </app-modal>
    }

    @if (consent()) {
      <app-modal title="Onam kaydet" (close)="consent.set(null)">
        <label class="field">Onam formu sürümü *<input class="input" [(ngModel)]="consentVersion" placeholder="örn. v1" /></label>
        <ng-container footer><button class="btn" (click)="consent.set(null)">Vazgeç</button><button class="btn primary" [disabled]="!consentVersion.trim()" (click)="give()">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (eraseOpen()) {
      <app-modal title="Katılımcı verilerini sil" (close)="eraseOpen.set(false)">
        <div class="notice warn">Ses dosyaları diskten silinir; transkript, notlar ve kişisel alanlar temizlenir; ilişkili tüm kayıtlar kaldırılır. Bu işlem geri alınamaz (KVKK silme hakkı).</div>
        <label class="field" style="margin-top:14px">Onaylamak için kodu yazın: <b>{{ p()?.participantCode }}</b><input class="input" [(ngModel)]="eraseConfirm" /></label>
        <ng-container footer><button class="btn" (click)="eraseOpen.set(false)">Vazgeç</button><button class="btn danger" [disabled]="eraseConfirm !== p()?.participantCode" (click)="erase()"><app-icon name="trash" /> Kalıcı olarak sil</button></ng-container>
      </app-modal>
    }

    @if (visitOpen()) {
      <app-modal title="Ziyaret ekle" (close)="visitOpen.set(false)">
        <form id="vf" (ngSubmit)="addVisit()" class="form-grid">
          <label class="field">Ziyaret türü<select class="input" name="t" [(ngModel)]="vf.visitType">@for (t of vt; track $index) { <option [ngValue]="$index">{{ t }}</option> }</select></label>
          <label class="field">Protokol *<select class="input" name="p" [(ngModel)]="vf.protocolId" required><option value="" disabled>Seçin…</option>@for (pr of protocols(); track pr.id) { <option [value]="pr.id">{{ pr.name }} v{{ pr.version }}</option> }</select></label>
          <label class="field">Planlanan tarih<input class="input" name="s" type="date" [(ngModel)]="vf.scheduledDate" /></label>
          <label class="field">Gerçekleşen tarih<input class="input" name="a" type="date" [(ngModel)]="vf.actualDate" /></label>
          <label class="field wide">Not<input class="input" name="n" [(ngModel)]="vf.notes" /></label>
          @if (!protocols().length) { <div class="notice warn wide">Önce <a routerLink="/research">Araştırma</a> sayfasında bir protokol oluşturun.</div> }
        </form>
        <ng-container footer><button class="btn" (click)="visitOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="vf" [disabled]="!vf.protocolId">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (aceOpen()) {
      <app-modal title="ACE-III ekle" (close)="aceOpen.set(false)" [wide]="true">
        <form id="af" (ngSubmit)="addAce()" class="form-grid">
          <label class="field">Ziyaret *<select class="input" name="v" [(ngModel)]="af.visitId" required>@for (v of visits(); track v.id) { <option [value]="v.id">{{ vt[v.visitType] }} · {{ d(v.actualDate || v.scheduledDate) }}</option> }</select></label>
          <label class="field">Tarih *<input class="input" name="d" type="date" [(ngModel)]="af.assessmentDate" required /></label>
          <label class="field">Form sürümü *<input class="input" name="ver" [(ngModel)]="af.assessmentVersion" required placeholder="örn. TR-A" /></label>
          <label class="field">Değerlendirici<input class="input" name="e" [(ngModel)]="af.evaluator" /></label>
          <label class="field">Toplam (0-100)<input class="input" name="t" type="number" min="0" max="100" [(ngModel)]="af.totalScore" /></label>
          <label class="field">Dikkat (0-18)<input class="input" name="at" type="number" min="0" max="18" [(ngModel)]="af.attentionScore" /></label>
          <label class="field">Bellek (0-26)<input class="input" name="m" type="number" min="0" max="26" [(ngModel)]="af.memoryScore" /></label>
          <label class="field">Akıcılık (0-14)<input class="input" name="f" type="number" min="0" max="14" [(ngModel)]="af.fluencyScore" /></label>
          <label class="field">Dil (0-26)<input class="input" name="l" type="number" min="0" max="26" [(ngModel)]="af.languageScore" /></label>
          <label class="field">Görsel-uzamsal (0-16)<input class="input" name="vs" type="number" min="0" max="16" [(ngModel)]="af.visuospatialScore" /></label>
        </form>
        <ng-container footer><button class="btn" (click)="aceOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="af">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (outOpen()) {
      <app-modal title="Klinik sonuç ekle" (close)="outOpen.set(false)">
        <form id="of" (ngSubmit)="addOutcome()" class="form-grid">
          <label class="field">Sonuç<select class="input" name="t" [(ngModel)]="of.outcomeType">@for (t of ot; track $index) { <option [ngValue]="$index">{{ t }}</option> }</select></label>
          <label class="field">Tanı / karar tarihi<input class="input" name="d" type="date" [(ngModel)]="of.diagnosisDate" /></label>
          <label class="field wide">Değerlendirici<input class="input" name="e" [(ngModel)]="of.evaluator" /></label>
          <label class="field wide">Not<textarea class="input" name="n" [(ngModel)]="of.notes"></textarea></label>
        </form>
        <ng-container footer><button class="btn" (click)="outOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="of">Kaydet</button></ng-container>
      </app-modal>
    }`
})
export class ParticipantDetailPage implements OnInit {
  id = input.required<string>();
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private router = inject(Router);

  p = signal<Participant | null>(null);
  visits = signal<Visit[]>([]);
  aces = signal<Ace[]>([]);
  outcomes = signal<Outcome[]>([]);
  protocols = signal<Protocol[]>([]);
  tab = signal<Tab>('visits');
  consent = signal<'give' | null>(null);
  eraseOpen = signal(false);
  visitOpen = signal(false);
  aceOpen = signal(false);
  outOpen = signal(false);
  eligOpen = signal(false);
  pred = signal<VisitPrediction | null>(null);
  ef: any = {};
  dx = DIAGNOSES;
  consentVersion = ''; eraseConfirm = '';
  vt = VISIT_TYPES; ot = OUTCOME_TYPES; d = fmtDate; dt = fmtDateTime;
  vf: any = {}; af: any = {}; of: any = {};

  ngOnInit() { this.reload(); }

  reload() {
    const id = this.id();
    this.api.participant(id).subscribe({ next: v => this.p.set(v), error: e => this.toast.err(e) });
    this.api.visits(id).subscribe({ next: v => this.visits.set(v), error: e => this.toast.err(e) });
    this.api.aces(id).subscribe({ next: v => this.aces.set(v), error: () => {} });
    this.api.outcomes(id).subscribe({ next: v => this.outcomes.set(v), error: () => {} });
  }

  openElig() {
    const p = this.p()!;
    this.ef = { diagnosis: p.diagnosis ?? null, willingFollowUp6Months: !!p.willingFollowUp6Months, adequateVisionHearing: !!p.adequateVisionHearing,
      severeMentalIllness: !!p.severeMentalIllness, severeNeurologicalDeficit: !!p.severeNeurologicalDeficit, languageBarrier: !!p.languageBarrier };
    this.eligOpen.set(true);
  }
  saveElig() {
    this.api.setEligibility(this.id(), this.ef).subscribe({
      next: r => { this.eligOpen.set(false); this.toast.ok(r.eligible ? 'Katılımcı çalışmaya uygun' : 'Katılımcı ölçütleri karşılamıyor'); this.reload(); }, error: e => this.toast.err(e)
    });
  }
  predict(v: Visit) { this.api.predictVisit(v.id).subscribe({ next: r => this.pred.set(r), error: e => this.toast.err(e) }); }

  give() {
    this.api.setConsent(this.id(), true, this.consentVersion.trim()).subscribe({
      next: () => { this.consent.set(null); this.toast.ok('Onam kaydedildi'); this.reload(); }, error: e => this.toast.err(e)
    });
  }
  withdraw() {
    this.api.setConsent(this.id(), false).subscribe({ next: () => { this.toast.ok('Onam geri çekildi'); this.reload(); }, error: e => this.toast.err(e) });
  }
  erase() {
    this.api.eraseParticipant(this.id()).subscribe({
      next: () => { this.toast.ok('Katılımcı verileri silindi'); this.router.navigate(['/participants']); }, error: e => this.toast.err(e)
    });
  }

  openVisit() {
    this.vf = { visitType: 0, protocolId: '', scheduledDate: '', actualDate: '', notes: '' };
    this.visitOpen.set(true);
    this.api.protocols().subscribe({ next: v => { this.protocols.set(v); if (v.length === 1) this.vf.protocolId = v[0].id; }, error: e => this.toast.err(e) });
  }
  addVisit() {
    const b = { ...this.vf, participantId: this.id(), scheduledDate: this.vf.scheduledDate || null, actualDate: this.vf.actualDate || null, notes: this.vf.notes || null };
    this.api.createVisit(b).subscribe({ next: () => { this.visitOpen.set(false); this.toast.ok('Ziyaret eklendi'); this.reload(); }, error: e => this.toast.err(e) });
  }

  openAce() { this.af = { visitId: this.visits()[0]?.id, assessmentDate: new Date().toISOString().slice(0, 10), assessmentVersion: '', evaluator: '' }; this.aceOpen.set(true); }
  addAce() {
    const n = (x: any) => (x === '' || x == null ? null : +x);
    const a = this.af;
    this.api.createAce({ participantId: this.id(), visitId: a.visitId, assessmentVersion: a.assessmentVersion, assessmentDate: a.assessmentDate, evaluator: a.evaluator || null,
      totalScore: n(a.totalScore), attentionScore: n(a.attentionScore), memoryScore: n(a.memoryScore), fluencyScore: n(a.fluencyScore), languageScore: n(a.languageScore), visuospatialScore: n(a.visuospatialScore) })
      .subscribe({ next: () => { this.aceOpen.set(false); this.toast.ok('Değerlendirme eklendi'); this.reload(); }, error: e => this.toast.err(e) });
  }

  addOutcome() {
    this.api.createOutcome({ participantId: this.id(), outcomeType: this.of.outcomeType ?? 0, diagnosisDate: this.of.diagnosisDate || null, evaluator: this.of.evaluator || null, notes: this.of.notes || null })
      .subscribe({ next: () => { this.outOpen.set(false); this.of = {}; this.toast.ok('Sonuç eklendi'); this.reload(); }, error: e => this.toast.err(e) });
  }
}

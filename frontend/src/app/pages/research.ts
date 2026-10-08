import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { FeatureDefinition, Protocol, Stimulus, VALIDATION_LABEL } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal } from '../shared/ui';

type Tab = 'protocols' | 'stimuli' | 'features';

@Component({
  selector: 'app-research',
  imports: [FormsModule, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head"><div><h1>Araştırma tanımları</h1><p>Protokoller ve uyarıcılar sürümlüdür; değiştirmek yerine yeni sürüm oluşturun.</p></div></div>
      <div class="tabs" role="tablist">
        <button class="tab" [class.active]="tab() === 'protocols'" (click)="tab.set('protocols')">Protokoller ({{ protocols().length }})</button>
        <button class="tab" [class.active]="tab() === 'stimuli'" (click)="tab.set('stimuli')">Uyarıcılar ({{ stimuli().length }})</button>
        <button class="tab" [class.active]="tab() === 'features'" (click)="tab.set('features')">Özellik kaydı ({{ features().length }})</button>
      </div>

      @if (tab() === 'protocols') {
        <div class="card flush">
          <header style="padding:16px 20px 0"><h2>Araştırma protokolleri</h2><button class="btn sm primary" (click)="pOpen.set(true)"><app-icon name="plus" /> Yeni protokol</button></header>
          @if (!protocols().length) { <app-empty icon="flask" text="Henüz protokol yok" /> } @else {
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Ad</th><th>Sürüm</th><th>Kayıt süresi</th><th>Kodlama kılavuzu</th><th>Kör anotasyon</th><th>Etik kurul onayı</th></tr></thead>
              <tbody>@for (p of protocols(); track p.id) {
                <tr><td><b>{{ p.name }}</b><br><span class="muted">{{ p.description }}</span></td><td>v{{ p.version }}</td><td>{{ p.targetRecordingSecondsMin }}–{{ p.targetRecordingSecondsMax }} sn</td><td>{{ p.codingManualVersion }}</td>
                  <td>@if (p.isBlindedAnnotationEnabled) { <span class="badge ok">Açık</span> } @else { <span class="badge">Kapalı</span> }</td>
                  <td>@if (p.ethicsApprovalNumber) { <span class="badge ok">{{ p.ethicsApprovalNumber }}</span> } @else { <span class="badge warn">Yok · kayıt alınamaz</span> }
                    <button class="btn sm" style="margin-left:6px" (click)="openEthics(p)"><app-icon name="edit" /></button></td></tr>
              }</tbody>
            </table></div>
          }
        </div>
      }

      @if (tab() === 'stimuli') {
        <div class="card flush">
          <header style="padding:16px 20px 0"><h2>Uyarıcılar</h2><button class="btn sm primary" (click)="sOpen.set(true)" [disabled]="!protocols().length"><app-icon name="plus" /> Yeni uyarıcı</button></header>
          @if (!stimuli().length) { <app-empty icon="flask" [text]="protocols().length ? 'Henüz uyarıcı yok' : 'Önce bir protokol oluşturun'" /> } @else {
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Uyarıcı</th><th>Sürüm</th><th>Protokol</th><th>Açıklama</th></tr></thead>
              <tbody>@for (s of stimuli(); track s.id) { <tr><td><b>{{ s.stimulusId }}</b></td><td>v{{ s.version }}</td><td>{{ protoName(s.protocolId) }}</td><td>{{ s.description || '-' }}</td></tr> }</tbody>
            </table></div>
          }
        </div>
      }

      @if (tab() === 'features') {
        <div class="card flush">
          <header style="padding:16px 20px 0"><h2>Özellik kaydı</h2></header>
          <div style="padding:0 20px"><div class="notice">Otomatik üretilen özellikler araştırma ekibi onaylayana kadar <b>deneyseldir</b>.</div></div>
          @if (!features().length) { <app-empty icon="flask" text="Analiz yapıldıkça özellikler burada listelenir" /> } @else {
            <div class="table-wrap"><table class="table">
              <thead><tr><th>Özellik</th><th>Sürüm</th><th>Durum</th><th>Tanım</th></tr></thead>
              <tbody>@for (f of features(); track f.id) {
                <tr><td class="mono">{{ f.featureName }}</td><td>v{{ f.algorithmVersion }}</td>
                  <td><span class="badge" [class.ok]="f.validationStatus === 2" [class.warn]="f.validationStatus < 2">{{ vl[f.validationStatus] }}</span></td>
                  <td class="muted">{{ f.definition }}</td></tr>
              }</tbody>
            </table></div>
          }
        </div>
      }
    </div>

    @if (pOpen()) {
      <app-modal title="Yeni protokol" (close)="pOpen.set(false)">
        <form id="pf" (ngSubmit)="addProtocol()" class="form-grid">
          <label class="field">Ad *<input class="input" name="n" [(ngModel)]="pf.name" required /></label>
          <label class="field">Sürüm *<input class="input" name="v" [(ngModel)]="pf.version" required /></label>
          <label class="field">En kısa kayıt (sn)<input class="input" name="a" type="number" min="1" [(ngModel)]="pf.targetRecordingSecondsMin" /></label>
          <label class="field">En uzun kayıt (sn)<input class="input" name="b" type="number" min="1" [(ngModel)]="pf.targetRecordingSecondsMax" /></label>
          <label class="field">Kodlama kılavuzu sürümü *<input class="input" name="c" [(ngModel)]="pf.codingManualVersion" required /></label>
          <label class="field">Bilgi birimi şeması sürümü<input class="input" name="i" [(ngModel)]="pf.informationUnitSchemaVersion" /></label>
          <label class="field wide">Açıklama<textarea class="input" name="d" [(ngModel)]="pf.description"></textarea></label>
          <label class="check wide"><input type="checkbox" name="bl" [(ngModel)]="pf.isBlindedAnnotationEnabled" /> Kör anotasyon açık</label>
          <label class="field">Etik kurul<input class="input" name="ec" [(ngModel)]="pf.ethicsCommittee" /></label>
          <label class="field">Onay numarası<input class="input" name="en" [(ngModel)]="pf.ethicsApprovalNumber" /></label>
          <label class="field">Onay tarihi<input class="input" name="ed" type="date" [(ngModel)]="pf.ethicsApprovalDate" /></label>
        </form>
        <ng-container footer><button class="btn" (click)="pOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="pf">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (eOpen()) {
      <app-modal title="Etik kurul onayı" (close)="eOpen.set(false)">
        <form id="ethf" (ngSubmit)="saveEthics()" class="form-grid">
          <label class="field wide">Etik kurul<input class="input" name="ec" [(ngModel)]="ef.ethicsCommittee" /></label>
          <label class="field">Onay numarası *<input class="input" name="en" [(ngModel)]="ef.ethicsApprovalNumber" required /></label>
          <label class="field">Onay tarihi<input class="input" name="ed" type="date" [(ngModel)]="ef.ethicsApprovalDate" /></label>
          <div class="notice wide">Onay numarası olmayan protokolde ses kaydı alınamaz. Protokolün bilimsel içeriği değişmez; yalnızca onay bilgisi eklenir.</div>
        </form>
        <ng-container footer><button class="btn" (click)="eOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="ethf" [disabled]="!ef.ethicsApprovalNumber">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (sOpen()) {
      <app-modal title="Yeni uyarıcı" (close)="sOpen.set(false)">
        <form id="sf" (ngSubmit)="addStimulus()" class="form-grid">
          <label class="field">Protokol *<select class="input" name="p" [(ngModel)]="sf.protocolId" required>@for (p of protocols(); track p.id) { <option [value]="p.id">{{ p.name }} v{{ p.version }}</option> }</select></label>
          <label class="field">Uyarıcı kodu *<input class="input" name="s" [(ngModel)]="sf.stimulusId" required placeholder="örn. cookie-theft" /></label>
          <label class="field">Sürüm *<input class="input" name="v" [(ngModel)]="sf.version" required /></label>
          <label class="field wide">Açıklama<textarea class="input" name="d" [(ngModel)]="sf.description"></textarea></label>
        </form>
        <ng-container footer><button class="btn" (click)="sOpen.set(false)">Vazgeç</button><button class="btn primary" type="submit" form="sf">Kaydet</button></ng-container>
      </app-modal>
    }`
})
export class ResearchPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  tab = signal<Tab>('protocols');
  protocols = signal<Protocol[]>([]);
  stimuli = signal<Stimulus[]>([]);
  features = signal<FeatureDefinition[]>([]);
  pOpen = signal(false); sOpen = signal(false); eOpen = signal(false);
  ef: any = {}; ethicsFor = '';
  vl = VALIDATION_LABEL;
  pf: any = this.pBlank(); sf: any = { protocolId: '', stimulusId: '', version: '1', description: '' };

  private pBlank() { return { name: '', version: '1', description: '', targetRecordingSecondsMin: 180, targetRecordingSecondsMax: 300, codingManualVersion: '1', informationUnitSchemaVersion: '', isBlindedAnnotationEnabled: true, ethicsCommittee: '', ethicsApprovalNumber: '', ethicsApprovalDate: '' }; }
  protoName = (id: string) => { const p = this.protocols().find(x => x.id === id); return p ? `${p.name} v${p.version}` : '-'; };

  ngOnInit() { this.load(); }
  load() {
    this.api.protocols().subscribe({ next: v => { this.protocols.set(v); if (!this.sf.protocolId && v[0]) this.sf.protocolId = v[0].id; }, error: e => this.toast.err(e) });
    this.api.stimuli().subscribe({ next: v => this.stimuli.set(v), error: e => this.toast.err(e) });
    this.api.features().subscribe({ next: v => this.features.set(v), error: () => {} });
  }
  addProtocol() {
    this.api.createProtocol({ ...this.pf, informationUnitSchemaVersion: this.pf.informationUnitSchemaVersion || null,
      ethicsCommittee: this.pf.ethicsCommittee || null, ethicsApprovalNumber: this.pf.ethicsApprovalNumber || null, ethicsApprovalDate: this.pf.ethicsApprovalDate || null }).subscribe({
      next: () => { this.pOpen.set(false); this.pf = this.pBlank(); this.toast.ok('Protokol oluşturuldu'); this.load(); },
      error: e => this.toast.err(e?.status === 409 ? 'Bu ad ve sürümde protokol zaten var; yeni sürüm numarası verin.' : e)
    });
  }
  openEthics(p: Protocol) {
    this.ethicsFor = p.id;
    this.ef = { ethicsCommittee: p.ethicsCommittee ?? '', ethicsApprovalNumber: p.ethicsApprovalNumber ?? '', ethicsApprovalDate: p.ethicsApprovalDate ?? '' };
    this.eOpen.set(true);
  }
  saveEthics() {
    this.api.setEthics(this.ethicsFor, { ...this.ef, ethicsCommittee: this.ef.ethicsCommittee || null, ethicsApprovalDate: this.ef.ethicsApprovalDate || null }).subscribe({
      next: () => { this.eOpen.set(false); this.toast.ok('Etik onay kaydedildi'); this.load(); }, error: e => this.toast.err(e)
    });
  }
  addStimulus() {
    this.api.createStimulus(this.sf).subscribe({
      next: () => { this.sOpen.set(false); this.sf = { ...this.sf, stimulusId: '', description: '' }; this.toast.ok('Uyarıcı eklendi'); this.load(); },
      error: e => this.toast.err(e?.status === 409 ? 'Bu kod ve sürümde uyarıcı zaten var.' : e)
    });
  }
}

import { DatePipe } from '@angular/common';
import { Component, DestroyRef, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { KaggleAccount, KaggleOverview } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal } from '../shared/ui';

const LABEL: Record<string, string> = { online: 'Çevrimiçi', starting: 'Başlatılıyor', offline: 'Kapalı', error: 'Hata', disabled: 'Devre dışı', scheduled: 'Zamanlandı' };
const CLS: Record<string, string> = { online: 'ok', starting: 'warn', offline: '', error: 'err', disabled: '', scheduled: 'info' };

@Component({
  selector: 'app-kaggle',
  imports: [DatePipe, FormsModule, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head"><div><h1>Kaggle hesapları</h1>
        <p>Konuşma analizi ve model eğitimi Kaggle’da çalışır. Çekirdekler yalnızca iş geldiğinde açılır ve iş bittikten 20 dakika sonra kendiliğinden kapanır; iş çoğalırsa ikinci hesap da açılır. Hesaplar sırayla kullanılır (GPU hakkı olanlar önce), GPU kotası bitenler CPU ile devam eder.</p></div>
        <button class="btn primary" (click)="open()" [disabled]="!ov().secretsEnabled"><app-icon name="plus" /> Hesap ekle</button></div>

      @if (!ov().secretsEnabled) { <div class="notice warn" style="margin-bottom:16px">Sunucuda <code>NeuroVox__SecretKey</code> tanımlı değil; token saklanamaz.</div> }
      <div class="notice" style="margin-bottom:16px">Ses kayıtları sağlık verisidir ve Kaggle yurt dışında üçüncü taraftır: bu yolla yalnızca sentetik ya da kendi kaydettiğiniz sesleri analiz edin. Token’lar AES-256-GCM ile şifrelenip saklanır, geri gösterilmez.</div>

      <div class="grid" style="grid-template-columns:repeat(auto-fit,minmax(170px,1fr));margin-bottom:16px">
        <div class="stat"><span class="lbl">Çevrimiçi kernel</span><span class="num">{{ online() }} / {{ ov().accounts.length }}</span></div>
        <div class="stat"><span class="lbl">Sıradaki analiz</span><span class="num">{{ ov().queue.recordings }}</span></div>
        <div class="stat"><span class="lbl">Sıradaki eğitim</span><span class="num">{{ ov().queue.training }}</span></div>
        <div class="stat"><span class="lbl">Toplam GPU kalan</span><span class="num">{{ gpuLeft() }} sa</span></div>
      </div>

      <div class="card flush" style="margin-bottom:16px">
        @if (!ov().accounts.length) { <app-empty icon="flask" text="Henüz Kaggle hesabı eklenmedi; analizler yerel AI servisine gider." /> } @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Kullanıcı</th><th>Durum</th><th>Mod</th><th>Bu hafta GPU</th><th>Son sinyal</th><th>Not</th><th class="right">İşlem</th></tr></thead>
            <tbody>@for (a of ov().accounts; track a.id) {
              <tr><td><b>{{ a.username }}</b></td>
                <td><span class="badge" [class]="cls[a.status]">{{ label[a.status] }}</span></td>
                <td>{{ a.status === 'online' || a.status === 'starting' ? (a.mode === 'gpu' ? 'GPU' : 'CPU') : '—' }}</td>
                <td style="min-width:150px">
                  <div class="muted" style="font-size:.8rem">{{ a.gpuHoursUsed }} / {{ a.gpuHoursLimit }} sa{{ a.gpuExhausted ? ' · kota doldu' : '' }}</div>
                  <progress [value]="a.gpuHoursUsed" [max]="a.gpuHoursLimit" style="width:100%"></progress></td>
                <td>{{ a.lastHeartbeatUtc ? (a.lastHeartbeatUtc | date: 'dd.MM HH:mm:ss') : '—' }}</td>
                <td class="muted">@if (a.status === 'scheduled') { {{ a.resumeAtUtc | date: 'dd.MM HH:mm' }} 'de başlar } @else { {{ a.lastError || '' }} }
                  @if (a.status === 'online' && !a.upToDate) { <div>Eski sürüm; boşalınca otomatik yenilenir</div> }</td>
                <td class="right"><div class="row" style="justify-content:flex-end;gap:6px">
                  <button class="btn sm" (click)="connect(a)" [disabled]="busy() === a.id || a.status === 'starting' || !a.enabled"><app-icon name="play" /> Şimdi bağlan</button>
                  @if (a.status === 'online' || a.status === 'starting') { <button class="btn sm" (click)="stop(a)">Durdur</button> }
                  <button class="btn sm" (click)="schedule.set(a); at = ''">Zamanla</button>
                  <button class="btn sm" (click)="setEnabled(a, !a.enabled)">{{ a.enabled ? 'Devre dışı bırak' : 'Etkinleştir' }}</button>
                  <button class="btn sm danger" (click)="del.set(a)" aria-label="Sil"><app-icon name="trash" /></button>
                </div></td></tr>
            }</tbody>
          </table></div>
        }
      </div>
    </div>

    @if (adding()) {
      <app-modal title="Kaggle hesabı ekle" (close)="adding.set(false)">
        <form id="kf" (ngSubmit)="add()" class="form-grid">
          <label class="field wide">Kaggle kullanıcı adı *<input class="input" name="u" [(ngModel)]="f.username" required autocomplete="off" /></label>
          <label class="field wide">API token *<input class="input" name="k" type="password" [(ngModel)]="f.apiKey" required minlength="10" autocomplete="new-password" /></label>
          <div class="notice wide">Kaggle → Settings → API → “Create New Token”. Hesapta telefon doğrulaması olmalı (internet erişimi için). Eklerken Kaggle’a doğrulatılır.</div>
        </form>
        <ng-container footer><button class="btn" (click)="adding.set(false)">Vazgeç</button>
          <button class="btn primary" type="submit" form="kf" [disabled]="!f.username || f.apiKey.length < 10 || saving()">{{ saving() ? 'Doğrulanıyor…' : 'Ekle' }}</button></ng-container>
      </app-modal>
    }

    @if (schedule(); as a) {
      <app-modal [title]="'Zamanlı başlatma · ' + a.username" (close)="schedule.set(null)">
        <label class="field">Bu saatten önce kernel açılmaz (yerel saatiniz)<input class="input" type="datetime-local" name="at" [(ngModel)]="at" /></label>
        <div class="notice" style="margin-top:12px">Zamanlanınca çalışan kernel kapatılır; saat gelince sistem hesabı kendiliğinden başlatır. Zamanlamayı kaldırmak için “Zamanlamayı kaldır”a basın.</div>
        <ng-container footer><button class="btn" (click)="saveSchedule(null)">Zamanlamayı kaldır</button>
          <button class="btn primary" (click)="saveSchedule(at)" [disabled]="!at">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (del()) {
      <app-modal title="Hesabı sil" (close)="del.set(null)">
        <p><b>{{ del()!.username }}</b> ve şifreli token’ı kalıcı olarak silinecek; çalışan kernel bir sonraki sinyalde kendini kapatır.</p>
        <ng-container footer><button class="btn" (click)="del.set(null)">Vazgeç</button><button class="btn danger" (click)="remove()"><app-icon name="trash" /> Sil</button></ng-container>
      </app-modal>
    }`
})
export class KaggleAccountsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  ov = signal<KaggleOverview>({ secretsEnabled: true, alwaysOn: false, queue: { recordings: 0, training: 0 }, accounts: [] });
  adding = signal(false); saving = signal(false); busy = signal(''); del = signal<KaggleAccount | null>(null);
  f = { username: '', apiKey: '' };
  schedule = signal<KaggleAccount | null>(null); at = '';
  label = LABEL; cls = CLS;

  constructor() {
    const t = setInterval(() => this.load(), 15000);   // kernel and job status change without user action
    inject(DestroyRef).onDestroy(() => clearInterval(t));
  }
  ngOnInit() { this.load(); }
  load() {
    this.api.kaggleAccounts().subscribe({ next: r => this.ov.set(r), error: e => this.toast.err(e) });
  }

  online = () => this.ov().accounts.filter(a => a.status === 'online').length;
  gpuLeft = () => Math.max(0, this.ov().accounts.reduce((s, a) => s + a.gpuHoursLimit - a.gpuHoursUsed, 0)).toFixed(1);

  open() { this.f = { username: '', apiKey: '' }; this.adding.set(true); }
  add() {
    this.saving.set(true);
    this.api.addKaggleAccount(this.f.username.trim(), this.f.apiKey.trim()).subscribe({
      next: () => { this.saving.set(false); this.f.apiKey = ''; this.adding.set(false); this.toast.ok('Hesap eklendi; iş geldiğinde kernel kendiliğinden açılır'); this.load(); },
      error: e => { this.saving.set(false); this.toast.err(e); }
    });
  }
  connect(a: KaggleAccount) {
    this.busy.set(a.id);
    this.api.connectKaggle(a.id).subscribe({
      next: () => { this.busy.set(''); this.toast.ok('Kernel başlatıldı; çevrimiçi olması birkaç dakika sürer'); this.load(); },
      error: e => { this.busy.set(''); this.toast.err(e); this.load(); }
    });
  }
  setEnabled(a: KaggleAccount, enabled: boolean) {
    this.api.setKaggleEnabled(a.id, enabled).subscribe({ next: () => { this.toast.ok(enabled ? 'Hesap etkinleştirildi' : 'Hesap devre dışı; kernel kapatılıyor'); this.load(); }, error: e => this.toast.err(e) });
  }
  stop(a: KaggleAccount) {
    this.api.stopKaggle(a.id).subscribe({ next: () => { this.toast.ok('Kernel kapatılıyor; gerektiğinde yeniden açılır'); this.load(); }, error: e => this.toast.err(e) });
  }
  saveSchedule(local: string | null) {
    const a = this.schedule()!;
    this.api.scheduleKaggle(a.id, local ? new Date(local).toISOString() : null).subscribe({
      next: () => { this.schedule.set(null); this.toast.ok(local ? 'Zamanlandı' : 'Zamanlama kaldırıldı'); this.load(); }, error: e => this.toast.err(e) });
  }
  remove() {
    this.api.deleteKaggle(this.del()!.id).subscribe({ next: () => { this.del.set(null); this.toast.ok('Hesap silindi'); this.load(); }, error: e => this.toast.err(e) });
  }
}

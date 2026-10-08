import { DatePipe } from '@angular/common';
import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { Institution } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal } from '../shared/ui';

@Component({
  selector: 'app-institutions',
  imports: [DatePipe, FormsModule, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head"><div><h1>Kurumlar</h1>
        <p>Her kurumun verisi, kullanıcıları ve rolleri birbirinden ayrıdır. Kurum oluşturulunca kendi yöneticisi (KurumAdmin) ve doktor rolü (Doktor) hazır gelir; kurum yöneticisi kendi doktorlarının yetkilerini yönetir. Kaggle hesapları tüm kurumlar için ortaktır.</p></div>
        <button class="btn primary" (click)="openNew()"><app-icon name="plus" /> Yeni kurum</button></div>

      <div class="card flush">
        @if (!items().length) { <app-empty icon="users" text="Kurum bulunamadı" /> } @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Kurum</th><th>Durum</th><th>Kullanıcı</th><th>Katılımcı</th><th>Kayıt</th><th>Abonelik sonu</th><th class="right">İşlem</th></tr></thead>
            <tbody>@for (c of items(); track c.id) {
              <tr><td><b>{{ c.name }}</b>@if (c.isSystem) { <span class="badge info" style="margin-left:8px">Sistem</span> }
                  <div class="muted" style="font-size:.8rem">{{ c.city || '' }} {{ c.email || '' }}</div></td>
                <td><span class="badge" [class]="c.isActive ? 'ok' : 'err'">{{ c.isActive ? 'Aktif' : 'Pasif' }}</span></td>
                <td>{{ c.userCount }}@if (c.maxUsers) { <span class="muted"> / {{ c.maxUsers }}</span> }</td>
                <td>{{ c.participantCount }}</td><td>{{ c.recordingCount }}</td>
                <td>{{ c.subscriptionEndDate ? (c.subscriptionEndDate | date: 'dd.MM.yyyy') : '—' }}</td>
                <td class="right"><div class="row" style="justify-content:flex-end;gap:6px">
                  <button class="btn sm" (click)="openEdit(c)"><app-icon name="edit" /> Düzenle</button>
                  @if (!c.isSystem) { <button class="btn sm" (click)="toggle(c)">{{ c.isActive ? 'Pasifleştir' : 'Aktifleştir' }}</button> }
                </div></td></tr>
            }</tbody>
          </table></div>
        }
      </div>
      <div class="notice" style="margin-top:16px">Pasif kurumun kullanıcıları giriş yapamaz ve açık oturumları bir sonraki istekte düşer. Veriler silinmez.</div>
    </div>

    @if (modal(); as m) {
      <app-modal [title]="m === 'new' ? 'Yeni kurum' : 'Kurumu düzenle'" (close)="modal.set(null)">
        <form id="inf" (ngSubmit)="save()" class="form-grid">
          <label class="field wide">Kurum adı *<input class="input" name="n" [(ngModel)]="f.name" required minlength="2" /></label>
          <label class="field">Şehir<input class="input" name="c" [(ngModel)]="f.city" /></label>
          <label class="field">Telefon<input class="input" name="p" [(ngModel)]="f.phoneNumber" /></label>
          <label class="field">E-posta<input class="input" name="e" type="email" [(ngModel)]="f.email" /></label>
          <label class="field">En fazla kullanıcı<input class="input" name="mu" type="number" min="1" [(ngModel)]="f.maxUsers" /></label>
          <label class="field">Abonelik bitişi<input class="input" name="sd" type="date" [(ngModel)]="f.subscriptionEndDate" /></label>
          @if (m === 'new') {
            <div class="notice wide">Kurumun ilk yöneticisi (KurumAdmin). Şifre en az 8 karakter olmalı; kullanıcı adı sistemde benzersiz olmalı.</div>
            <label class="field">Yönetici ad soyad *<input class="input" name="an" [(ngModel)]="f.adminNameSurname" required /></label>
            <label class="field">Yönetici kullanıcı adı *<input class="input" name="au" [(ngModel)]="f.adminUsername" required autocomplete="off" /></label>
            <label class="field">Yönetici e-posta<input class="input" name="ae" type="email" [(ngModel)]="f.adminEmail" /></label>
            <label class="field">Geçici şifre *<input class="input" name="ap" type="password" [(ngModel)]="f.adminPassword" required minlength="8" autocomplete="new-password" /></label>
          }
        </form>
        <ng-container footer><button class="btn" (click)="modal.set(null)">Vazgeç</button>
          <button class="btn primary" type="submit" form="inf" [disabled]="saving() || !f.name || (m === 'new' && (!f.adminNameSurname || !f.adminUsername || f.adminPassword.length < 8))">
            {{ saving() ? 'Kaydediliyor…' : 'Kaydet' }}</button></ng-container>
      </app-modal>
    }`
})
export class InstitutionsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  items = signal<Institution[]>([]);
  modal = signal<'new' | Institution | null>(null);
  saving = signal(false);
  f = this.blank();

  private blank() {
    return { name: '', city: '', phoneNumber: '', email: '', maxUsers: null as number | null, subscriptionEndDate: '',
      adminNameSurname: '', adminUsername: '', adminEmail: '', adminPassword: '' };
  }
  ngOnInit() { this.load(); }
  load() { this.api.institutions().subscribe({ next: r => this.items.set(r), error: e => this.toast.err(e) }); }

  openNew() { this.f = this.blank(); this.modal.set('new'); }
  openEdit(c: Institution) {
    this.f = { ...this.blank(), name: c.name, city: c.city ?? '', phoneNumber: c.phoneNumber ?? '', email: c.email ?? '', maxUsers: c.maxUsers ?? null,
      subscriptionEndDate: c.subscriptionEndDate ? c.subscriptionEndDate.slice(0, 10) : '' };
    this.modal.set(c);
  }
  private body() {
    const f = this.f;
    return { name: f.name.trim(), city: f.city || null, phoneNumber: f.phoneNumber || null, email: f.email || null, maxUsers: f.maxUsers || null,
      subscriptionEndDate: f.subscriptionEndDate ? new Date(f.subscriptionEndDate + 'T23:59:59Z').toISOString() : null };
  }
  save() {
    const m = this.modal(); if (!m) return;
    this.saving.set(true);
    const done = (msg: string) => { this.saving.set(false); this.modal.set(null); this.toast.ok(msg); this.load(); };
    const fail = (e: unknown) => { this.saving.set(false); this.toast.err(e); };
    if (m === 'new') {
      const f = this.f;
      this.api.createInstitution({ ...this.body(), adminNameSurname: f.adminNameSurname.trim(), adminUsername: f.adminUsername.trim(),
        adminEmail: f.adminEmail || null, adminPassword: f.adminPassword }).subscribe({ next: () => done('Kurum oluşturuldu'), error: fail });
    } else {
      this.api.updateInstitution(m.id, { ...this.body(), isActive: m.isActive }).subscribe({ next: () => done('Kurum güncellendi'), error: fail });
    }
  }
  toggle(c: Institution) {
    this.api.updateInstitution(c.id, { name: c.name, city: c.city ?? null, phoneNumber: c.phoneNumber ?? null, email: c.email ?? null,
      maxUsers: c.maxUsers ?? null, subscriptionEndDate: c.subscriptionEndDate ?? null, isActive: !c.isActive })
      .subscribe({ next: () => { this.toast.ok(c.isActive ? 'Kurum pasifleştirildi' : 'Kurum aktifleştirildi'); this.load(); }, error: e => this.toast.err(e) });
  }
}

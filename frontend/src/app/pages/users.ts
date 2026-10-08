import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { ApiService } from '../core/api.service';
import { AppUser, Role } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal } from '../shared/ui';

@Component({
  selector: 'app-users',
  imports: [FormsModule, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head"><div><h1>Kullanıcılar ve roller</h1><p>Terapist ve araştırmacı hesapları. Yetkiler rollere bağlı endpoint izinleriyle verilir.</p></div></div>

      <div class="card flush" style="margin-bottom:16px">
        <header style="padding:16px 20px 0"><h2>Kullanıcılar</h2><button class="btn sm primary" (click)="openUser()"><app-icon name="plus" /> Yeni kullanıcı</button></header>
        @if (!users().length) { <app-empty icon="users" text="Kullanıcı bulunamadı" /> } @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Ad soyad</th><th>Kullanıcı adı</th><th>E-posta</th><th>Rol</th><th class="right">İşlem</th></tr></thead>
            <tbody>@for (u of users(); track u.id) {
              <tr><td><b>{{ u.nameSurname }}</b></td><td>{{ u.userName }}</td><td>{{ u.email || '-' }}</td>
                <td>@if (u.userRole) { <span class="badge info">{{ u.userRole }}</span> } @else { <span class="muted">—</span> }</td>
                <td class="right"><div class="row" style="justify-content:flex-end;gap:6px">
                  <button class="btn sm" (click)="openAssign(u)"><app-icon name="shield" /> Rol</button>
                  <button class="btn sm danger" (click)="delUser.set(u)" aria-label="Sil"><app-icon name="trash" /></button>
                </div></td></tr>
            }</tbody>
          </table></div>
        }
      </div>

      <div class="card flush">
        <header style="padding:16px 20px 0"><h2>Roller</h2></header>
        <div style="padding:0 20px 16px" class="row">
          <input class="input" style="max-width:260px" placeholder="Yeni rol adı (örn. Terapist)" [(ngModel)]="newRole" (keyup.enter)="addRole()" aria-label="Yeni rol adı" />
          <button class="btn primary" (click)="addRole()" [disabled]="!newRole.trim()"><app-icon name="plus" /> Rol ekle</button>
        </div>
        <div class="table-wrap"><table class="table"><tbody>
          @for (r of roles(); track r.id) { <tr><td><b>{{ r.name }}</b></td><td class="right"><button class="btn sm danger" (click)="removeRole(r)" aria-label="Rolü sil"><app-icon name="trash" /></button></td></tr> }
        </tbody></table></div>
        <div style="padding:14px 20px"><div class="notice">Yeni rolün hangi işlemlere izin verdiği BaseAuth endpoint izinleriyle belirlenir; yeni roller başlangıçta hiçbir işleme izin vermez.</div></div>
      </div>
    </div>

    @if (uOpen()) {
      <app-modal title="Yeni kullanıcı" (close)="uOpen.set(false)">
        <form id="uf" (ngSubmit)="addUser()" class="form-grid">
          <label class="field">Ad soyad *<input class="input" name="n" [(ngModel)]="uf.nameSurname" required /></label>
          <label class="field">Kullanıcı adı *<input class="input" name="u" [(ngModel)]="uf.username" required autocomplete="off" /></label>
          <label class="field wide">E-posta<input class="input" name="e" type="email" [(ngModel)]="uf.email" /></label>
          <label class="field">Şifre *<input class="input" name="p" type="password" [(ngModel)]="uf.password" required minlength="8" autocomplete="new-password" /></label>
          <label class="field">Şifre tekrar *<input class="input" name="pc" type="password" [(ngModel)]="uf.passwordConfirm" required autocomplete="new-password" /></label>
          <label class="field wide">Rol<select class="input" name="r" [(ngModel)]="uf.role"><option value="">Rol atama</option>@for (r of roles(); track r.id) { <option>{{ r.name }}</option> }</select></label>
          @if (uf.password && uf.passwordConfirm && uf.password !== uf.passwordConfirm) { <div class="notice warn wide">Şifreler eşleşmiyor.</div> }
        </form>
        <ng-container footer><button class="btn" (click)="uOpen.set(false)">Vazgeç</button>
          <button class="btn primary" type="submit" form="uf" [disabled]="!uf.nameSurname || !uf.username || !uf.password || uf.password !== uf.passwordConfirm">Oluştur</button></ng-container>
      </app-modal>
    }

    @if (assign()) {
      <app-modal [title]="'Rol ata · ' + assign()!.nameSurname" (close)="assign.set(null)">
        <div class="stack">@for (r of roles(); track r.id) { <label class="check"><input type="checkbox" [checked]="picked().has(r.name)" (change)="toggle(r.name)" /> {{ r.name }}</label> }</div>
        <ng-container footer><button class="btn" (click)="assign.set(null)">Vazgeç</button><button class="btn primary" (click)="saveRoles()">Kaydet</button></ng-container>
      </app-modal>
    }

    @if (delUser()) {
      <app-modal title="Kullanıcıyı sil" (close)="delUser.set(null)">
        <p><b>{{ delUser()!.nameSurname }}</b> hesabı silinecek. Devam edilsin mi?</p>
        <ng-container footer><button class="btn" (click)="delUser.set(null)">Vazgeç</button><button class="btn danger" (click)="removeUser()"><app-icon name="trash" /> Sil</button></ng-container>
      </app-modal>
    }`
})
export class UsersPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  users = signal<AppUser[]>([]); roles = signal<Role[]>([]);
  uOpen = signal(false); assign = signal<AppUser | null>(null); delUser = signal<AppUser | null>(null);
  picked = signal(new Set<string>());
  uf: any = {}; newRole = '';

  ngOnInit() { this.load(); }
  load() {
    this.api.users().subscribe({ next: r => this.users.set(r.users as AppUser[] ?? []), error: e => this.toast.err(e) });
    this.api.roles().subscribe({ next: r => this.roles.set((r.datas as Role[]) ?? []), error: e => this.toast.err(e) });
  }

  openUser() { this.uf = { nameSurname: '', username: '', email: '', password: '', passwordConfirm: '', role: '' }; this.uOpen.set(true); }
  addUser() {
    const { role, ...body } = this.uf;
    this.api.createUser({ ...body, email: body.email || null }).subscribe({
      next: (res: any) => {
        const finish = () => { this.uOpen.set(false); this.toast.ok('Kullanıcı oluşturuldu'); this.load(); };
        const id = res?.userId ?? res?.id;
        if (role && id) this.api.assignRoles(id, [role]).subscribe({ next: finish, error: e => { this.toast.err(e); this.load(); } });
        else { finish(); if (role) this.toast.show('Rol atamak için listeden “Rol” düğmesini kullanın.'); }
      },
      error: e => this.toast.err(e)
    });
  }

  openAssign(u: AppUser) { this.picked.set(new Set(u.userRole ? u.userRole.split(',').map(s => s.trim()) : [])); this.assign.set(u); }
  toggle(name: string) { this.picked.update(s => { const n = new Set(s); n.has(name) ? n.delete(name) : n.add(name); return n; }); }
  saveRoles() {
    this.api.assignRoles(this.assign()!.id, [...this.picked()]).subscribe({
      next: () => { this.assign.set(null); this.toast.ok('Roller güncellendi'); this.load(); }, error: e => this.toast.err(e)
    });
  }
  removeUser() {
    this.api.deleteUser(this.delUser()!.id).subscribe({ next: () => { this.delUser.set(null); this.toast.ok('Kullanıcı silindi'); this.load(); }, error: e => this.toast.err(e) });
  }
  addRole() {
    const n = this.newRole.trim(); if (!n) return;
    this.api.createRole(n).subscribe({ next: () => { this.newRole = ''; this.toast.ok('Rol eklendi'); this.load(); }, error: e => this.toast.err(e) });
  }
  removeRole(r: Role) {
    this.api.deleteRole(r.id).subscribe({ next: () => { this.toast.ok('Rol silindi'); this.load(); }, error: e => this.toast.err(e) });
  }
}

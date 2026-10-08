import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from '../core/auth.service';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-shell',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, IconComponent],
  template: `
    <div class="app" [class.open]="open()">
      <aside class="nav" aria-label="Ana menü">
        <div class="brand"><app-icon name="wave" [size]="26" /><span>NeuroVox</span></div>
        <nav (click)="open.set(false)">
          <small>Genel</small>
          <a routerLink="/" routerLinkActive="on" [routerLinkActiveOptions]="{ exact: true }"><app-icon name="dashboard" />Genel Bakış</a>
          <small>Çalışma</small>
          <a routerLink="/participants" routerLinkActive="on"><app-icon name="users" />Katılımcılar</a>
          <a routerLink="/recordings" routerLinkActive="on"><app-icon name="mic" />Kayıtlar</a>
          <a routerLink="/analysis" routerLinkActive="on"><app-icon name="chart" />Analiz</a>
          <small>Yönetim</small>
          <a routerLink="/research" routerLinkActive="on"><app-icon name="flask" />Araştırma</a>
          <a routerLink="/users" routerLinkActive="on"><app-icon name="shield" />Kullanıcılar</a>
          @if (auth.roles().includes('NeuroVoxAdmin')) { <a routerLink="/kaggle" routerLinkActive="on"><app-icon name="flask" />Kaggle</a> }
        </nav>
        <div class="me">
          <div class="avatar">{{ initials() }}</div>
          <div class="who"><b>{{ auth.displayName() || 'Kullanıcı' }}</b><span>{{ auth.roles().join(', ') || '—' }}</span></div>
          <button class="btn ghost sm" (click)="auth.logout()" aria-label="Çıkış yap" title="Çıkış"><app-icon name="logout" /></button>
        </div>
      </aside>
      <div class="scrim" (click)="open.set(false)"></div>
      <div class="main">
        <header class="top">
          <button class="btn ghost" (click)="open.set(true)" aria-label="Menüyü aç"><app-icon name="menu" /></button>
          <span class="brand-sm">NeuroVox</span>
        </header>
        <router-outlet />
      </div>
    </div>`,
  styles: [`
    .app { display: grid; grid-template-columns: 252px 1fr; min-height: 100vh; }
    .nav { background: var(--nav); color: var(--nav-text); display: flex; flex-direction: column; padding: 18px 12px; position: sticky; top: 0; height: 100vh; }
    .brand { display: flex; align-items: center; gap: 10px; color: #5eead4; font-weight: 700; font-size: 1.15rem; padding: 6px 12px 18px; }
    nav { display: flex; flex-direction: column; gap: 2px; flex: 1; overflow-y: auto; }
    nav small { text-transform: uppercase; font-size: .68rem; letter-spacing: .08em; color: #64748b; padding: 14px 12px 6px; }
    nav a { display: flex; align-items: center; gap: 12px; padding: 9px 12px; border-radius: 10px; color: var(--nav-text); font-weight: 500; }
    nav a:hover { background: rgb(255 255 255 / .06); text-decoration: none; }
    nav a.on { background: rgb(45 212 191 / .14); color: #5eead4; }
    .me { display: flex; align-items: center; gap: 10px; padding: 12px; border-top: 1px solid rgb(255 255 255 / .08); margin-top: 8px; }
    .me .btn { color: var(--nav-text); }
    .avatar { width: 36px; height: 36px; border-radius: 50%; background: #134e4a; color: #5eead4; display: grid; place-items: center; font-weight: 700; flex: none; }
    .who { display: flex; flex-direction: column; min-width: 0; flex: 1; }
    .who b { color: #f1f5f9; font-size: .9rem; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .who span { font-size: .75rem; color: #94a3b8; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; }
    .main { min-width: 0; }
    .top, .scrim { display: none; }
    @media (max-width: 900px) {
      .app { grid-template-columns: 1fr; }
      .nav { position: fixed; z-index: 40; left: 0; top: 0; width: 272px; transform: translateX(-100%); transition: transform .2s; }
      .open .nav { transform: none; }
      .open .scrim { display: block; position: fixed; inset: 0; background: rgb(2 6 23 / .5); z-index: 30; }
      .top { display: flex; align-items: center; gap: 8px; padding: 10px 12px; background: var(--surface); border-bottom: 1px solid var(--border); position: sticky; top: 0; z-index: 20; }
      .brand-sm { font-weight: 700; }
    }
  `]
})
export class Shell {
  auth = inject(AuthService);
  private router = inject(Router);
  open = signal(false);
  initials = () => (this.auth.displayName() || 'NV').split(/\s+/).map(s => s[0]).slice(0, 2).join('').toUpperCase();
}

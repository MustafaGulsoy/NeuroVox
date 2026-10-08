import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { AuthService } from '../core/auth.service';
import { PublicInstitution } from '../core/models';
import { messageOf } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';

@Component({
  selector: 'app-login',
  imports: [FormsModule, IconComponent],
  template: `
    <main class="wrap">
      <section class="hero" aria-hidden="true">
        <div class="brand"><app-icon name="wave" [size]="30" /> NeuroVox</div>
        <h1>Konuşmadan bilişe,<br>ölçülebilir bir köprü.</h1>
        <p>Ses kayıtlarını toplayın, kör anotasyonla etiketleyin ve konuşma özelliklerini klinik sonuçlarla ilişkilendirin.</p>
        <ul>
          <li><app-icon name="shield" /> KVKK uyumlu onam ve silme akışı</li>
          <li><app-icon name="tag" /> Bağımsız (kör) terapist anotasyonu</li>
          <li><app-icon name="chart" /> Boylamsal analiz ve dışa aktarma</li>
        </ul>
      </section>

      <section class="panel">
        <form (ngSubmit)="submit()" class="stack" autocomplete="on">
          <div>
            <h2>Giriş yap</h2>
            <p class="muted">Hesabınızla devam edin.</p>
          </div>
          @if (institutions().length > 1) {
            <label class="field">Kurum
              <select class="input" name="t" [ngModel]="auth.tenantId()" (ngModelChange)="auth.chooseTenant($event)">
                @for (i of institutions(); track i.id) { <option [value]="i.id">{{ i.name }}</option> }
              </select>
            </label>
          }
          <label class="field">Kullanıcı adı veya e-posta
            <input class="input" name="u" [(ngModel)]="user" autocomplete="username" required autofocus />
          </label>
          <label class="field">Şifre
            <input class="input" name="p" type="password" [(ngModel)]="pass" autocomplete="current-password" required />
          </label>
          @if (error()) { <div class="notice warn" role="alert">{{ error() }}</div> }
          @if (!auth.tenantId()) { <div class="notice warn">Bu kurulum için kurum kimliği (customerId) yapılandırılmamış.</div> }
          <button class="btn primary" type="submit" [disabled]="busy() || !user || !pass">
            @if (busy()) { <app-icon name="refresh" class="spin" /> } Giriş
          </button>
        </form>
      </section>
    </main>`,
  styles: [`
    .wrap { min-height: 100vh; display: grid; grid-template-columns: 1.1fr 1fr; }
    .hero { background: linear-gradient(160deg, #0f172a 0%, #134e4a 100%); color: #e2e8f0; padding: clamp(32px, 6vw, 72px); display: flex; flex-direction: column; justify-content: center; gap: 22px; }
    .brand { display: flex; align-items: center; gap: 10px; font-weight: 700; font-size: 1.2rem; color: #5eead4; }
    .hero h1 { font-size: clamp(1.8rem, 3.4vw, 2.8rem); color: #fff; }
    .hero p { max-width: 46ch; color: #cbd5e1; }
    .hero ul { list-style: none; padding: 0; margin: 8px 0 0; display: grid; gap: 12px; }
    .hero li { display: flex; align-items: center; gap: 10px; color: #99f6e4; }
    .panel { display: grid; place-items: center; padding: 32px 20px; background: var(--bg); }
    form { width: min(380px, 100%); }
    @media (max-width: 860px) { .wrap { grid-template-columns: 1fr; } .hero { padding: 28px 22px; } .hero ul, .hero p { display: none; } }
  `]
})
export class LoginPage implements OnInit {
  auth = inject(AuthService);
  private api = inject(ApiService);
  private router = inject(Router);
  institutions = signal<PublicInstitution[]>([]);

  ngOnInit() {
    this.api.publicInstitutions().subscribe({
      next: list => {
        this.institutions.set(list);
        // Remembered/default institution must be one that exists; otherwise start with the first.
        if (list.length && !list.some(i => i.id === this.auth.tenantId())) this.auth.chooseTenant(list[0].id);
      },
      error: () => { /* single-institution deployments keep working from config.json */ }
    });
  }
  user = ''; pass = '';
  busy = signal(false);
  error = signal('');

  async submit() {
    this.busy.set(true); this.error.set('');
    try {
      await this.auth.login(this.user.trim(), this.pass);
      await this.router.navigate(['/']);
    } catch (e: any) {
      this.error.set(e?.status === 400 || e?.status === 401 || e?.status === 404 ? 'Kullanıcı adı veya şifre hatalı.' : messageOf(e));
    } finally { this.busy.set(false); this.pass = ''; }
  }
}

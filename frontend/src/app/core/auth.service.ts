import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ConfigService } from './config.service';

interface Session { accessToken: string; refreshToken: string; expiration: string; }
const KEY = 'nv.session';
const CLAIM = {
  name: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name',
  given: 'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/givenname',
  role: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/role',
  tenant: 'http://schemas.microsoft.com/ws/2008/06/identity/claims/userdata'
};
const TENANT_KEY = 'nv.tenant';

function decode(token: string): Record<string, unknown> {
  try {
    const b64 = token.split('.')[1].replace(/-/g, '+').replace(/_/g, '/');
    const bin = atob(b64);
    return JSON.parse(new TextDecoder().decode(Uint8Array.from(bin, c => c.charCodeAt(0))));
  } catch { return {}; }
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private http = inject(HttpClient);
  private router = inject(Router);
  private cfg = inject(ConfigService);

  // sessionStorage: survives reload, dies with the tab. Tokens are never put in localStorage.
  private session = signal<Session | null>(this.restore());
  private refreshing: Promise<boolean> | null = null;

  readonly isLoggedIn = computed(() => !!this.session());
  readonly claims = computed(() => decode(this.session()?.accessToken ?? ''));
  readonly displayName = computed(() => (this.claims()[CLAIM.given] ?? this.claims()[CLAIM.name] ?? '') as string);
  readonly roles = computed(() => {
    const r = this.claims()[CLAIM.role];
    return Array.isArray(r) ? (r as string[]) : r ? [r as string] : [];
  });
  // Institution: the token's claim once signed in; before that the one picked on the login page (remembered), else the deployment default.
  readonly chosenTenant = signal<string>(this.rememberedTenant());
  readonly tenantId = computed(() => (this.claims()[CLAIM.tenant] as string) || this.chosenTenant() || this.cfg.config.customerId);
  readonly isSystemAdmin = computed(() => this.roles().includes('NeuroVoxAdmin'));
  readonly isInstitutionAdmin = computed(() => this.roles().includes('KurumAdmin') || this.isSystemAdmin());
  get accessToken() { return this.session()?.accessToken ?? null; }

  private rememberedTenant(): string {
    try { return localStorage.getItem(TENANT_KEY) ?? ''; } catch { return ''; }
  }
  chooseTenant(id: string) {
    this.chosenTenant.set(id);
    try { localStorage.setItem(TENANT_KEY, id); } catch { /* private mode */ }
  }

  private restore(): Session | null {
    try { return JSON.parse(sessionStorage.getItem(KEY) ?? 'null'); } catch { return null; }
  }
  private store(s: Session | null) {
    this.session.set(s);
    try { if (s) sessionStorage.setItem(KEY, JSON.stringify(s)); else sessionStorage.removeItem(KEY); } catch { /* private mode */ }
  }

  async login(usernameOrEmail: string, password: string): Promise<void> {
    const res: any = await firstValueFrom(this.http.post('/api/Auth/Login', { usernameOrEmail, password }));
    if (!res?.token?.accessToken) throw new Error('Giriş başarısız');
    this.store({ accessToken: res.token.accessToken, refreshToken: res.token.refreshToken, expiration: res.token.expiration });
  }

  // One refresh at a time; concurrent 401s share it.
  refresh(): Promise<boolean> {
    const rt = this.session()?.refreshToken;
    if (!rt) return Promise.resolve(false);
    this.refreshing ??= firstValueFrom(this.http.post<any>('/api/Auth/RefreshTokenLogin', { refreshToken: rt }))
      .then(res => {
        const t = res?.token ?? res;
        if (!t?.accessToken) return false;
        this.store({ accessToken: t.accessToken, refreshToken: t.refreshToken ?? rt, expiration: t.expiration });
        return true;
      })
      .catch(() => false)
      .finally(() => (this.refreshing = null));
    return this.refreshing;
  }

  logout() {
    this.store(null);
    this.router.navigate(['/login']);
  }
}

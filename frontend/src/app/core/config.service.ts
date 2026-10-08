import { Injectable } from '@angular/core';

export interface AppConfig { customerId: string; apiBase: string; }

// Runtime config (public/config.json) so one build serves any deployment; written by the container entrypoint.
@Injectable({ providedIn: 'root' })
export class ConfigService {
  config: AppConfig = { customerId: '', apiBase: '' };

  async load(): Promise<void> {
    try {
      const res = await fetch('config.json', { cache: 'no-store' });
      if (res.ok) this.config = { ...this.config, ...(await res.json()) };
    } catch { /* keep defaults */ }
  }
}

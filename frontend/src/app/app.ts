import { Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { ToastService } from './core/toast.service';
import { IconComponent } from './shared/icon.component';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet, IconComponent],
  template: `
    <router-outlet />
    <div class="toasts" aria-live="polite">
      @for (t of toast.toasts(); track t.id) {
        <div class="toast" [class]="t.kind" role="status">
          <app-icon [name]="t.kind === 'ok' ? 'check' : t.kind === 'err' ? 'x' : 'eye'" />
          <span>{{ t.text }}</span>
          <button type="button" (click)="toast.dismiss(t.id)" aria-label="Kapat"><app-icon name="x" [size]="14" /></button>
        </div>
      }
    </div>`,
  styles: [`
    .toasts { position: fixed; right: 16px; bottom: 16px; display: flex; flex-direction: column; gap: 8px; z-index: 100; max-width: min(420px, calc(100vw - 32px)); }
    .toast { display: flex; gap: 10px; align-items: flex-start; padding: 12px 14px; border-radius: 12px; background: var(--surface); border: 1px solid var(--border);
      box-shadow: 0 8px 30px rgb(0 0 0 / .18); animation: in .2s ease; }
    .toast span { flex: 1; font-size: .92rem; }
    .toast button { background: none; border: 0; color: var(--muted); cursor: pointer; padding: 2px; }
    .toast.ok { border-left: 4px solid var(--ok); } .toast.err { border-left: 4px solid var(--err); } .toast.info { border-left: 4px solid var(--info); }
    @keyframes in { from { transform: translateY(8px); opacity: 0; } }
  `]
})
export class App {
  toast = inject(ToastService);
}

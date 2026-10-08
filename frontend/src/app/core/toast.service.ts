import { Injectable, signal } from '@angular/core';

export interface Toast { id: number; kind: 'ok' | 'err' | 'info'; text: string; }

@Injectable({ providedIn: 'root' })
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private n = 0;

  show(text: string, kind: Toast['kind'] = 'info', ms = 4500) {
    const id = ++this.n;
    this.toasts.update(t => [...t, { id, kind, text }]);
    setTimeout(() => this.dismiss(id), ms);
  }
  ok(text: string) { this.show(text, 'ok'); }
  err(e: unknown) { this.show(messageOf(e), 'err', 7000); }
  dismiss(id: number) { this.toasts.update(t => t.filter(x => x.id !== id)); }
}

export function messageOf(e: any): string {
  if (e?.forbidden) return 'Bu işlem için yetkiniz yok.';
  const b = e?.error;
  if (typeof b === 'string' && b) return b;
  if (b?.title || b?.message) return b.title ?? b.message;
  if (e?.status === 0) return 'Sunucuya ulaşılamadı.';
  if (e?.status === 429) return 'Çok fazla istek, biraz bekleyin.';
  return e?.message ?? 'Beklenmeyen bir hata oluştu.';
}

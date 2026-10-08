import { Component, input, output } from '@angular/core';
import { IconComponent } from './icon.component';
import { STATUS_LABEL } from '../core/models';

@Component({
  selector: 'app-status',
  standalone: true,
  template: `<span class="badge" [class]="cls()"><span class="dot" [class.pulse]="status() === 1 || status() === 2"></span>{{ label() }}</span>`
})
export class StatusBadge {
  status = input.required<number>();
  label = () => STATUS_LABEL[this.status()] ?? '-';
  cls = () => ({ 0: '', 1: 'info', 2: 'info', 3: 'ok', 4: 'err' } as Record<number, string>)[this.status()] ?? '';
}

@Component({
  selector: 'app-modal',
  standalone: true,
  imports: [IconComponent],
  template: `
    <div class="backdrop" (click)="close.emit()" (keydown.escape)="close.emit()">
      <div class="modal" [class.wide]="wide()" role="dialog" aria-modal="true" [attr.aria-label]="title()" (click)="$event.stopPropagation()">
        <header>
          <h2>{{ title() }}</h2>
          <button class="btn ghost sm" type="button" (click)="close.emit()" aria-label="Kapat"><app-icon name="x" /></button>
        </header>
        <div class="body"><ng-content /></div>
        <footer><ng-content select="[footer]" /></footer>
      </div>
    </div>`
})
export class Modal {
  title = input.required<string>();
  wide = input(false);
  close = output<void>();
}

@Component({
  selector: 'app-empty',
  standalone: true,
  imports: [IconComponent],
  template: `<div class="empty"><app-icon [name]="icon()" [size]="40" /><p>{{ text() }}</p><ng-content /></div>`
})
export class Empty {
  icon = input('inbox');
  text = input('Kayıt bulunamadı');
}

export function fmtDate(v?: string | null): string {
  if (!v) return '-';
  const d = new Date(v);
  return isNaN(+d) ? v : d.toLocaleDateString('tr-TR');
}
export function fmtDateTime(v?: string | null): string {
  if (!v) return '-';
  const d = new Date(v);
  return isNaN(+d) ? v : d.toLocaleString('tr-TR', { dateStyle: 'short', timeStyle: 'short' });
}
export function fmtDuration(s?: number | null): string {
  if (s == null) return '-';
  const m = Math.floor(s / 60), r = Math.round(s % 60);
  return `${m}:${String(r).padStart(2, '0')}`;
}
export function download(blob: Blob, name: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = name; a.click();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

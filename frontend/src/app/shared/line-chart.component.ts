import { Component, computed, input } from '@angular/core';

export interface Point { label: string; value: number; }

// Dependency-free SVG line chart (longitudinal change of one feature).
@Component({
  selector: 'app-line-chart',
  standalone: true,
  template: `
    <svg [attr.viewBox]="'0 0 ' + w + ' ' + h" role="img" [attr.aria-label]="title()" style="width:100%;height:auto">
      @for (t of ticks(); track t.y) {
        <line [attr.x1]="pad" [attr.x2]="w - pad" [attr.y1]="t.y" [attr.y2]="t.y" stroke="var(--border)" />
        <text [attr.x]="pad - 8" [attr.y]="t.y + 4" text-anchor="end" fill="var(--muted)" font-size="11">{{ t.v }}</text>
      }
      @if (path()) { <path [attr.d]="path()" fill="none" stroke="var(--primary)" stroke-width="2.5" stroke-linejoin="round" /> }
      @for (p of pts(); track p.x) {
        <circle [attr.cx]="p.x" [attr.cy]="p.y" r="4.5" fill="var(--surface)" stroke="var(--primary)" stroke-width="2.5"><title>{{ p.label }}: {{ p.value }}</title></circle>
        <text [attr.x]="p.x" [attr.y]="h - 8" text-anchor="middle" fill="var(--muted)" font-size="11">{{ p.label }}</text>
      }
    </svg>`
})
export class LineChart {
  points = input.required<Point[]>();
  title = input('Grafik');
  readonly w = 640; readonly h = 240; readonly pad = 44;

  private range = computed(() => {
    const v = this.points().map(p => p.value);
    if (!v.length) return { min: 0, max: 1 };
    let min = Math.min(...v), max = Math.max(...v);
    if (min === max) { min -= 1; max += 1; }
    const m = (max - min) * 0.1;
    return { min: min - m, max: max + m };
  });
  private y = (v: number) => { const { min, max } = this.range(); return this.h - 28 - ((v - min) / (max - min)) * (this.h - 28 - 16); };
  pts = computed(() => {
    const p = this.points(); const n = p.length;
    return p.map((q, i) => ({ ...q, x: n === 1 ? this.w / 2 : this.pad + (i * (this.w - 2 * this.pad)) / (n - 1), y: this.y(q.value) }));
  });
  path = computed(() => this.pts().length > 1 ? this.pts().map((p, i) => `${i ? 'L' : 'M'}${p.x},${p.y}`).join(' ') : '');
  ticks = computed(() => {
    const { min, max } = this.range();
    return [0, 1, 2, 3].map(i => { const v = min + ((max - min) * i) / 3; return { v: +v.toFixed(2), y: this.y(v) }; });
  });
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Summary, STATUS_LABEL } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, StatusBadge, fmtDateTime, fmtDuration } from '../shared/ui';

@Component({
  selector: 'app-dashboard',
  imports: [IconComponent, RouterLink, StatusBadge, Empty],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>Genel Bakış</h1><p>Çalışmanın güncel durumu.</p></div>
        <a class="btn primary" routerLink="/recordings"><app-icon name="upload" /> Kayıt yükle</a>
      </div>

      @if (!s()) {
        <div class="grid cols-4"> @for (i of [1,2,3,4]; track i) { <div class="card"><div class="skeleton" style="height:70px"></div></div> } </div>
      } @else {
        <div class="grid cols-4">
          <div class="card stat"><div class="icon"><app-icon name="users" /></div><span class="num">{{ s()!.participants }}</span><span class="lbl">Katılımcı · {{ s()!.withConsent }} onamlı</span></div>
          <div class="card stat"><div class="icon"><app-icon name="folder" /></div><span class="num">{{ s()!.visits }}</span><span class="lbl">Ziyaret</span></div>
          <div class="card stat"><div class="icon"><app-icon name="mic" /></div><span class="num">{{ s()!.recordings }}</span><span class="lbl">Ses kaydı</span></div>
          <div class="card stat"><div class="icon"><app-icon name="tag" /></div><span class="num">{{ s()!.annotations }}</span><span class="lbl">Anotasyon · {{ s()!.measurements }} ölçüm</span></div>
        </div>

        <div class="grid cols-2" style="margin-top:16px">
          <section class="card">
            <header><h2>Analiz durumu</h2></header>
            @if (!s()!.recordings) { <app-empty icon="mic" text="Henüz kayıt yok" /> }
            @for (r of rows(); track r.status) {
              <div class="bar-row">
                <span style="width:100px">{{ label(r.status) }}</span>
                <div class="progress" style="flex:1"><i [style.width.%]="r.pct"></i></div>
                <b style="width:32px;text-align:right">{{ r.count }}</b>
              </div>
            }
          </section>

          <section class="card flush">
            <header style="padding:20px 20px 0"><h2>Son kayıtlar</h2><a routerLink="/recordings" class="btn ghost sm">Tümü</a></header>
            @if (!s()!.recent.length) { <app-empty icon="mic" text="Henüz kayıt yok" /> } @else {
              <div class="table-wrap"><table class="table"><tbody>
                @for (r of s()!.recent; track r.id) {
                  <tr class="clickable" [routerLink]="['/annotate', r.id]">
                    <td><span class="mono">{{ r.id.slice(0, 8) }}</span><br><span class="muted">{{ fmtDt(r.rowCreatedDate) }}</span></td>
                    <td>{{ dur(r.recordingDurationSeconds) }}</td>
                    <td class="right"><app-status [status]="r.analysisStatus" /></td>
                  </tr>
                }
              </tbody></table></div>
            }
          </section>
        </div>
      }
    </div>`,
  styles: ['.bar-row { display: flex; align-items: center; gap: 12px; padding: 8px 0; }']
})
export class DashboardPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  s = signal<Summary | null>(null);
  fmtDt = fmtDateTime; dur = fmtDuration;
  label = (st: number) => STATUS_LABEL[st];

  rows = () => {
    const x = this.s(); if (!x) return [];
    const max = Math.max(1, ...x.recordingsByStatus.map(r => r.count));
    return x.recordingsByStatus.map(r => ({ ...r, pct: (r.count / max) * 100 }));
  };

  ngOnInit() { this.api.summary().subscribe({ next: v => this.s.set(v), error: e => this.toast.err(e) }); }
}

import { Component, OnInit, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { ApiService } from '../core/api.service';
import { Participant } from '../core/models';
import { ToastService } from '../core/toast.service';
import { IconComponent } from '../shared/icon.component';
import { Empty, Modal, fmtDate } from '../shared/ui';

@Component({
  selector: 'app-participants',
  imports: [FormsModule, IconComponent, Modal, Empty],
  template: `
    <div class="page">
      <div class="page-head">
        <div><h1>Katılımcılar</h1><p>Takma adlı kodlarla yönetilir; gerçek kimlik bilgisi tutulmaz.</p></div>
        <button class="btn primary" (click)="open.set(true)"><app-icon name="plus" /> Yeni katılımcı</button>
      </div>

      <div class="card flush">
        <div style="padding:14px 16px;border-bottom:1px solid var(--border)">
          <label class="row" style="position:relative">
            <app-icon name="search" class="muted" />
            <input class="input" style="border:0;box-shadow:none;padding-left:4px" placeholder="Kod ile ara…" [ngModel]="q()" (ngModelChange)="q.set($event)" aria-label="Katılımcı ara" />
          </label>
        </div>
        @if (loading()) { <div style="padding:20px"><div class="skeleton" style="height:120px"></div></div> }
        @else if (!filtered().length) { <app-empty icon="users" [text]="q() ? 'Eşleşen katılımcı yok' : 'Henüz katılımcı eklenmedi'" /> }
        @else {
          <div class="table-wrap"><table class="table">
            <thead><tr><th>Kod</th><th>Cinsiyet</th><th>Doğum tarihi</th><th>Onam</th><th>Eklenme</th></tr></thead>
            <tbody>
              @for (p of filtered(); track p.id) {
                <tr class="clickable" (click)="go(p)">
                  <td><b>{{ p.participantCode }}</b></td>
                  <td>{{ p.sex || '-' }}</td>
                  <td>{{ d(p.dateOfBirth) }}</td>
                  <td>
                    @if (p.consentWithdrawnAt) { <span class="badge err">Geri çekildi</span> }
                    @else if (p.consentGivenAt) { <span class="badge ok">Var · {{ p.consentVersion }}</span> }
                    @else { <span class="badge warn">Yok</span> }
                  </td>
                  <td class="muted">{{ d(p.rowCreatedDate) }}</td>
                </tr>
              }
            </tbody>
          </table></div>
        }
      </div>
    </div>

    @if (open()) {
      <app-modal title="Yeni katılımcı" (close)="open.set(false)">
        <form id="pf" (ngSubmit)="create()" class="form-grid">
          <label class="field">Katılımcı kodu *<input class="input" name="c" [(ngModel)]="f.participantCode" required maxlength="64" /></label>
          <label class="field">Cinsiyet
            <select class="input" name="s" [(ngModel)]="f.sex"><option value="">Belirtilmedi</option><option>Kadın</option><option>Erkek</option><option>Diğer</option></select>
          </label>
          <label class="field">Doğum tarihi<input class="input" name="d" type="date" [(ngModel)]="f.dateOfBirth" /></label>
          <label class="field">Onam formu sürümü<input class="input" name="v" [(ngModel)]="f.consentVersion" placeholder="örn. v1 (boşsa onam yok)" /></label>
          <label class="field wide">Notlar<textarea class="input" name="n" [(ngModel)]="f.notes"></textarea></label>
          <div class="notice wide">Onam kaydı olmayan katılımcı için ses kaydı yüklenemez.</div>
        </form>
        <ng-container footer>
          <button class="btn" type="button" (click)="open.set(false)">Vazgeç</button>
          <button class="btn primary" type="submit" form="pf" [disabled]="!f.participantCode.trim() || busy()">Kaydet</button>
        </ng-container>
      </app-modal>
    }`
})
export class ParticipantsPage implements OnInit {
  private api = inject(ApiService);
  private toast = inject(ToastService);
  private router = inject(Router);
  all = signal<Participant[]>([]);
  q = signal('');
  loading = signal(true);
  open = signal(false);
  busy = signal(false);
  d = fmtDate;
  f: any = this.blank();

  filtered = () => this.all().filter(p => p.participantCode.toLowerCase().includes(this.q().trim().toLowerCase()));
  private blank() { return { participantCode: '', sex: '', dateOfBirth: '', consentVersion: '', notes: '' }; }

  ngOnInit() { this.load(); }
  load() {
    this.api.participants().subscribe({ next: v => { this.all.set(v); this.loading.set(false); }, error: e => { this.loading.set(false); this.toast.err(e); } });
  }
  go(p: Participant) { this.router.navigate(['/participants', p.id]); }

  create() {
    this.busy.set(true);
    const b = { ...this.f, sex: this.f.sex || null, dateOfBirth: this.f.dateOfBirth || null, consentVersion: this.f.consentVersion?.trim() || null, notes: this.f.notes || null };
    this.api.createParticipant(b).subscribe({
      next: r => { this.busy.set(false); this.open.set(false); this.f = this.blank(); this.toast.ok('Katılımcı eklendi'); this.router.navigate(['/participants', r.id]); },
      error: e => { this.busy.set(false); this.toast.err(e); }
    });
  }
}

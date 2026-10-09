import { inject } from '@angular/core';
import { CanActivateFn, Router, Routes } from '@angular/router';
import { AuthService } from './core/auth.service';

const authGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() ? true : inject(Router).createUrlTree(['/login']);
};
const guestGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isLoggedIn() ? inject(Router).createUrlTree(['/']) : true;
};

// UI-side convenience only: the API enforces the same rules on every call.
const adminGuard = (kind: 'system' | 'institution'): CanActivateFn => () => {
  const auth = inject(AuthService);
  return (kind === 'system' ? auth.isSystemAdmin() : auth.isInstitutionAdmin()) ? true : inject(Router).createUrlTree(['/']);
};

// The system admin runs institutions and Kaggle only; clinical data belongs to the institutions.
const clinicalGuard: CanActivateFn = () => {
  const auth = inject(AuthService);
  return auth.isSystemAdmin() ? inject(Router).createUrlTree(['/institutions']) : true;
};

export const routes: Routes = [
  { path: 'login', canActivate: [guestGuard], loadComponent: () => import('./pages/login').then(m => m.LoginPage) },
  {
    path: '', canActivate: [authGuard], loadComponent: () => import('./pages/shell').then(m => m.Shell),
    children: [
      { path: '', canActivate: [clinicalGuard], pathMatch: 'full', loadComponent: () => import('./pages/dashboard').then(m => m.DashboardPage), title: 'Genel Bakış · NeuroVox' },
      { path: 'participants', canActivate: [clinicalGuard], loadComponent: () => import('./pages/participants').then(m => m.ParticipantsPage), title: 'Katılımcılar · NeuroVox' },
      { path: 'participants/:id', canActivate: [clinicalGuard], loadComponent: () => import('./pages/participant-detail').then(m => m.ParticipantDetailPage), title: 'Katılımcı · NeuroVox' },
      { path: 'recordings', canActivate: [clinicalGuard], loadComponent: () => import('./pages/recordings').then(m => m.RecordingsPage), title: 'Kayıtlar · NeuroVox' },
      { path: 'annotate/:id', canActivate: [clinicalGuard], loadComponent: () => import('./pages/annotate').then(m => m.AnnotatePage), title: 'Anotasyon · NeuroVox' },
      { path: 'analysis', canActivate: [clinicalGuard], loadComponent: () => import('./pages/analysis').then(m => m.AnalysisPage), title: 'Analiz · NeuroVox' },
      { path: 'research', canActivate: [clinicalGuard], loadComponent: () => import('./pages/research').then(m => m.ResearchPage), title: 'Araştırma · NeuroVox' },
      { path: 'kaggle', canActivate: [adminGuard('system')], loadComponent: () => import('./pages/kaggle').then(m => m.KaggleAccountsPage), title: 'Kaggle · NeuroVox' },
      { path: 'institutions', canActivate: [adminGuard('system')], loadComponent: () => import('./pages/institutions').then(m => m.InstitutionsPage), title: 'Kurumlar · NeuroVox' },
      { path: 'users', canActivate: [adminGuard('institution')], loadComponent: () => import('./pages/users').then(m => m.UsersPage), title: 'Kullanıcılar · NeuroVox' }
    ]
  },
  { path: '**', redirectTo: '' }
];

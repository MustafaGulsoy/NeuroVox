import { HttpErrorResponse, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { inject } from '@angular/core';
import { catchError, from, switchMap, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ConfigService } from './config.service';

const PUBLIC = ['/api/Auth/Login', '/api/Auth/RefreshTokenLogin'];

export const apiInterceptor: HttpInterceptorFn = (req, next) => {
  const auth = inject(AuthService);
  const cfg = inject(ConfigService).config;
  if (!req.url.startsWith('/api')) return next(req);

  const withHeaders = (r: HttpRequest<unknown>, token: string | null) => r.clone({
    url: cfg.apiBase + r.url,
    setHeaders: { customerid: cfg.customerId, ...(token ? { Authorization: `Bearer ${token}` } : {}) }
  });
  const isPublic = PUBLIC.some(p => req.url.startsWith(p));

  return next(withHeaders(req, isPublic ? null : auth.accessToken)).pipe(
    catchError((err: HttpErrorResponse) => {
      if (err.status !== 401 || isPublic) return throwError(() => err);
      // 401 = expired token OR missing role permission (BaseAuth answers both with 401): refresh once and retry.
      return from(auth.refresh()).pipe(
        switchMap(ok => {
          if (!ok) { auth.logout(); return throwError(() => err); }
          return next(withHeaders(req, auth.accessToken)).pipe(
            catchError((e2: HttpErrorResponse) => throwError(() => e2.status === 401 ? Object.assign(e2, { forbidden: true }) : e2))
          );
        })
      );
    })
  );
};

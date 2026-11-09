import { HttpErrorResponse, HttpEvent, HttpHandlerFn, HttpInterceptorFn, HttpRequest } from '@angular/common/http';
import { Observable, catchError, throwError } from 'rxjs';
import { inject } from '@angular/core';
import { AuthService } from './auth.service';
import { AuthStorage, TOKEN_STORAGE_KEY } from './auth-storage';

/**
 * Attaches the Bearer access token to every API request and logs the user
 * out when the backend rejects the token (401): expired or revoked tokens
 * send the user back to the login screen on the next API call.
 */
export const authInterceptor: HttpInterceptorFn = (request, next: HttpHandlerFn): Observable<HttpEvent<unknown>> => {
  const storage = inject(AuthStorage);
  const auth = inject(AuthService);

  // The login call itself must never carry (or expect) an Authorization header.
  if (request.url.includes('/auth/login')) {
    return next(request).pipe(
      catchError((error: HttpErrorResponse) => {
        if (error.status === 401) {
          auth.outcome.set('invalid');
        }
        return throwError(() => error);
      }),
    );
  }

  const token = storage.accessToken;
  if (!token) {
    return next(request);
  }

  return next(requestWithToken(request, token)).pipe(
    catchError((error: unknown) => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        // Token rejected: clear the session; the app gate shows the login screen.
        storage.clearToken();
      }
      return throwError(() => error);
    }),
  );
};

function requestWithToken(request: HttpRequest<unknown>, token: string): HttpRequest<unknown> {
  return request.clone({ setHeaders: { Authorization: `Bearer ${token}` } });
}

/** Re-exported for components that need the storage key (logout cleanup). */
export { TOKEN_STORAGE_KEY };

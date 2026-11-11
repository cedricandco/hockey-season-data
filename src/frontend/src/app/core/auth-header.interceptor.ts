import { HttpInterceptorFn } from '@angular/common/http';

/**
 * Mirrors the SDK's `Authorization: Bearer` on a custom header so proxies,
 * managed hosts or edge rules that strip the standard header can still see
 * the token. Runs after the Auth0 interceptor reads the token from the
 * original request's header.
 */
export const authHeaderMirrorInterceptor: HttpInterceptorFn = (req, next) => {
  const bearer = req.headers.get('Authorization');
  if (!bearer || req.headers.has('X-Authorization')) return next(req);
  return next(req.clone({ headers: req.headers.set('X-Authorization', bearer) }));
};

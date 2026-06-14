import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { catchError, throwError } from 'rxjs';

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const token = localStorage.getItem('padel_token');
  if (!token) {
    return next(request);
  }

  return next(request.clone({
    setHeaders: {
      Authorization: `Bearer ${token}`
    }
  })).pipe(
    catchError(error => {
      if (error instanceof HttpErrorResponse && error.status === 401) {
        localStorage.removeItem('padel_token');
        window.dispatchEvent(new CustomEvent('padel:invalid-token'));
      }

      return throwError(() => error);
    })
  );
};

import { Injectable } from '@angular/core';
import { BehaviorSubject } from 'rxjs';

@Injectable({ providedIn: 'root' })
export class LoadingService {
  private pendingRequests = 0;
  private readonly loadingSubject = new BehaviorSubject(false);

  readonly loading$ = this.loadingSubject.asObservable();

  show() {
    this.pendingRequests += 1;
    this.loadingSubject.next(true);
  }

  hide() {
    this.pendingRequests = Math.max(0, this.pendingRequests - 1);
    this.loadingSubject.next(this.pendingRequests > 0);
  }
}

import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import { AccountListItem, PulseFilters, WeeklyPulse } from './pulse.models';

@Injectable({ providedIn: 'root' })
export class PulseApi {
  private readonly http = inject(HttpClient);
  private readonly base = '/api';

  accounts(): Observable<AccountListItem[]> {
    return this.http.get<AccountListItem[]>(`${this.base}/accounts`);
  }

  weeklyPulse(filters: PulseFilters): Observable<WeeklyPulse> {
    let params = new HttpParams()
      .set('baselineWeeks', filters.baselineWeeks)
      .set('eventType', filters.eventType);

    // Omitted rather than sent empty: the API resolves the default week from the data,
    // which is the only correct source given the dataset ends months behind wall time.
    if (filters.weekStart) params = params.set('weekStart', filters.weekStart);

    return this.http.get<WeeklyPulse>(
      `${this.base}/accounts/${filters.accountId}/weekly-pulse`,
      { params },
    );
  }
}

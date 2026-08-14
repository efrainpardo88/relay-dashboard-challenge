import { DecimalPipe } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { toObservable, toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, ParamMap, Router } from '@angular/router';
import { Observable, catchError, map, of, startWith, switchMap } from 'rxjs';
import { PulseApi } from './pulse.api';
import {
  BASELINE_WEEK_OPTIONS,
  DEFAULT_FILTERS,
  EVENT_TYPE_OPTIONS,
  MetricView,
  PulseFilters,
  PulseStatus,
  WeeklyPulse,
} from './pulse.models';

type ViewState =
  | { state: 'loading' }
  | { state: 'ready'; pulse: WeeklyPulse }
  | { state: 'error'; message: string };

/**
 * Reads the four filters out of the URL, validating as it goes. A hand-edited or stale
 * link falls back to a default rather than putting the view into an impossible state.
 */
export function readFilters(params: ParamMap): PulseFilters {
  const accountId = Number(params.get('accountId'));
  const baselineWeeks = Number(params.get('baselineWeeks'));
  const eventType = params.get('eventType') ?? '';
  const weekStart = params.get('weekStart');

  return {
    accountId: Number.isInteger(accountId) && accountId > 0 ? accountId : DEFAULT_FILTERS.accountId,
    baselineWeeks: (BASELINE_WEEK_OPTIONS as readonly number[]).includes(baselineWeeks)
      ? baselineWeeks
      : DEFAULT_FILTERS.baselineWeeks,
    eventType: EVENT_TYPE_OPTIONS.some((o) => o.value === eventType)
      ? eventType
      : DEFAULT_FILTERS.eventType,
    weekStart: weekStart && /^\d{4}-\d{2}-\d{2}$/.test(weekStart) ? weekStart : null,
  };
}

@Component({
  selector: 'app-pulse-page',
  standalone: true,
  imports: [DecimalPipe],
  templateUrl: './pulse-page.component.html',
  styleUrl: './pulse-page.component.css',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PulsePageComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly api = inject(PulseApi);

  readonly baselineOptions = BASELINE_WEEK_OPTIONS;
  readonly eventTypeOptions = EVENT_TYPE_OPTIONS;

  /**
   * The URL is the only place this state lives. Nothing is mirrored into a component
   * field or into localStorage, which is what makes a reload and a shared link produce
   * the same view rather than merely a similar one.
   */
  readonly filters = toSignal(this.route.queryParamMap.pipe(map(readFilters)), {
    initialValue: DEFAULT_FILTERS,
  });

  readonly accounts = toSignal(
    this.api.accounts().pipe(catchError(() => of([]))),
    { initialValue: [] },
  );

  private readonly view = toSignal(
    toObservable(this.filters).pipe(
      switchMap((filters) =>
        this.api.weeklyPulse(filters).pipe(
          map((pulse): ViewState => ({ state: 'ready', pulse })),
          startWith<ViewState>({ state: 'loading' }),
          catchError((err): Observable<ViewState> =>
            of({
              state: 'error',
              message:
                err?.status === 0
                  ? 'Cannot reach the API. Is it running on http://localhost:5080?'
                  : err?.error?.title ?? `Request failed (${err?.status ?? 'unknown'}).`,
            }),
          ),
        ),
      ),
    ),
    { initialValue: { state: 'loading' } as ViewState },
  );

  readonly loading = computed(() => this.view().state === 'loading');

  readonly error = computed(() => {
    const view = this.view();
    return view.state === 'error' ? view.message : null;
  });

  readonly pulse = computed(() => {
    const view = this.view();
    return view.state === 'ready' ? view.pulse : null;
  });

  /** True when the account exists but has never recorded anything — Quiet Harbor Spa. */
  readonly isEmptyAccount = computed(() => {
    const p = this.pulse();
    return !!p && !p.dataQuality.hasData;
  });

  /** Returns the navigation promise so callers — and tests — can await the URL settling. */
  patch(changes: Partial<PulseFilters>): Promise<boolean> {
    return this.router.navigate([], {
      relativeTo: this.route,
      queryParams: changes,
      queryParamsHandling: 'merge',
    });
  }

  /** Switching account clears the week: accounts do not necessarily share a latest week. */
  onAccountChange(value: string): void {
    void this.patch({ accountId: Number(value), weekStart: null });
  }

  shiftWeek(deltaWeeks: number): void {
    const current = this.pulse()?.week.start;
    if (!current) return;

    const date = new Date(`${current}T00:00:00Z`);
    date.setUTCDate(date.getUTCDate() + deltaWeeks * 7);
    void this.patch({ weekStart: date.toISOString().slice(0, 10) });
  }

  resetWeek(): void {
    void this.patch({ weekStart: null });
  }

  // --- presentation helpers ------------------------------------------------------------

  statusLabel(status: PulseStatus): string {
    switch (status) {
      case 'above': return 'Above normal';
      case 'below': return 'Below normal';
      case 'normal': return 'Normal';
      case 'lowVolume': return 'Too few to compare';
      case 'insufficientHistory': return 'Not enough history';
      case 'noBaseline': return 'No baseline yet';
    }
  }

  /**
   * Only the three comparable verdicts get a badge. Low volume, thin history and a zero
   * baseline are shown as plain muted text: the numbers are still there to read, but the
   * dashboard does not pretend to a judgement it cannot support. A location going 5 to 9
   * is "+64%" and is also noise.
   */
  hasBadge(status: PulseStatus): boolean {
    return status === 'above' || status === 'below' || status === 'normal';
  }

  deltaText(metric: MetricView): string {
    if (metric.deltaPct === null) return '—';
    const pct = metric.deltaPct * 100;
    const rounded = Math.abs(pct) < 0.5 ? 0 : Math.round(pct);
    return `${rounded > 0 ? '+' : ''}${rounded}%`;
  }

  deltaClass(metric: MetricView): string {
    if (metric.deltaPct === null) return 'delta-none';
    if (metric.deltaPct > 0.005) return 'delta-up';
    if (metric.deltaPct < -0.005) return 'delta-down';
    return 'delta-flat';
  }

  formatDate(iso: string): string {
    return new Date(`${iso}T00:00:00Z`).toLocaleDateString('en-GB', {
      day: 'numeric', month: 'short', timeZone: 'UTC',
    });
  }
}

import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Router, convertToParamMap, provideRouter } from '@angular/router';
import { RouterTestingHarness } from '@angular/router/testing';

import { PulsePageComponent, readFilters } from './pulse-page.component';
import { WeeklyPulse } from './pulse.models';

const PULSE: WeeklyPulse = {
  account: {
    id: 6, name: 'Metro Collision Centers', industry: 'Automotive Services',
    timezone: 'America/New_York', locationCount: 15, hasData: true,
    latestLocalDate: '2026-07-27',
  },
  week: { start: '2026-07-20', end: '2026-07-26' },
  baseline: { weeks: 12, start: '2026-04-27', end: '2026-07-19' },
  eventType: 'call_received',
  total: null,
  locations: [],
  dataQuality: {
    hasData: true, duplicateEventsExcluded: 0,
    earliestLocalDate: '2026-01-31', latestLocalDate: '2026-07-27',
  },
};

/**
 * The requirement is that filter state survives a page reload. It does because it lives
 * nowhere but the URL — so what is worth testing is that a component built fresh from a
 * URL (which is exactly what a reload produces) reconstructs the same state and asks the
 * API for the same thing, with nothing carried over in memory.
 */
describe('PulsePageComponent — URL-persisted filter state', () => {
  const FULL_URL = '/?accountId=6&baselineWeeks=12&eventType=call_received&weekStart=2026-07-20';

  let http: HttpTestingController;

  /** Answers whatever the component has asked for so far. */
  const settle = () =>
    http.match(() => true).forEach((r) =>
      r.flush(r.request.url.endsWith('/api/accounts') ? [] : PULSE));

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([{ path: '', component: PulsePageComponent }]),
        provideHttpClient(),
        provideHttpClientTesting(),
      ],
    });
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('rebuilds every filter from the URL alone, as a reload would', async () => {
    const harness = await RouterTestingHarness.create(FULL_URL);
    const component = harness.routeDebugElement!.componentInstance as PulsePageComponent;

    expect(component.filters()).toEqual({
      accountId: 6,
      baselineWeeks: 12,
      eventType: 'call_received',
      weekStart: '2026-07-20',
    });

    settle();
  });

  it('asks the API for exactly what the URL said, holding nothing in memory', async () => {
    await RouterTestingHarness.create(FULL_URL);

    http.expectOne('/api/accounts').flush([]);

    const request = http.expectOne((r) => r.url === '/api/accounts/6/weekly-pulse');
    expect(request.request.params.get('baselineWeeks')).toBe('12');
    expect(request.request.params.get('eventType')).toBe('call_received');
    expect(request.request.params.get('weekStart')).toBe('2026-07-20');
    request.flush(PULSE);
  });

  it('writes a changed filter back into the URL so the next load keeps it', async () => {
    const harness = await RouterTestingHarness.create('/?accountId=6');
    const component = harness.routeDebugElement!.componentInstance as PulsePageComponent;
    settle();

    await component.patch({ baselineWeeks: 12 });

    const url = TestBed.inject(Router).url;
    expect(url).toContain('baselineWeeks=12');
    expect(url).toContain('accountId=6');   // merged into the URL, not replacing it

    settle();
  });

  it('drops the week when the account changes, so one account cannot pin another', async () => {
    const harness = await RouterTestingHarness.create('/?accountId=6&weekStart=2026-07-20');
    const component = harness.routeDebugElement!.componentInstance as PulsePageComponent;
    settle();

    await component.patch({ accountId: 16, weekStart: null });

    expect(TestBed.inject(Router).url).not.toContain('weekStart');
    expect(component.filters().weekStart).toBeNull();

    settle();
  });
});

/**
 * A URL is user-editable and can be stale — a bookmark saved before an option changed, or
 * a hand-typed value. Falling back to a default beats rendering an impossible state or
 * sending the API something it will reject.
 */
describe('readFilters', () => {
  it('keeps valid values', () => {
    expect(readFilters(convertToParamMap({
      accountId: '6', baselineWeeks: '12', eventType: 'lead_created', weekStart: '2026-07-20',
    }))).toEqual({
      accountId: 6, baselineWeeks: 12, eventType: 'lead_created', weekStart: '2026-07-20',
    });
  });

  it('falls back when the URL carries nothing', () => {
    expect(readFilters(convertToParamMap({}))).toEqual({
      accountId: 1, baselineWeeks: 8, eventType: 'all', weekStart: null,
    });
  });

  it('rejects a baseline window the API would refuse', () => {
    expect(readFilters(convertToParamMap({ baselineWeeks: '7' })).baselineWeeks).toBe(8);
  });

  it('rejects an unknown event type', () => {
    expect(readFilters(convertToParamMap({ eventType: 'smoke_signal' })).eventType).toBe('all');
  });

  it('rejects a malformed week', () => {
    expect(readFilters(convertToParamMap({ weekStart: 'last-monday' })).weekStart).toBeNull();
  });

  it('rejects a non-numeric account id', () => {
    expect(readFilters(convertToParamMap({ accountId: 'six' })).accountId).toBe(1);
  });
});

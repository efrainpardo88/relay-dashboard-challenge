export type PulseStatus =
  | 'normal'
  | 'above'
  | 'below'
  | 'lowVolume'
  | 'insufficientHistory'
  | 'noBaseline';

export interface AccountListItem {
  id: number;
  name: string;
  industry: string;
  timezone: string;
  locationCount: number;
  hasData: boolean;
  latestLocalDate: string | null;
}

export interface MetricView {
  current: number;
  baselineMedian: number;
  typicalLow: number;
  typicalHigh: number;
  /** Null whenever there is no meaningful baseline to divide by. Never Infinity or NaN. */
  deltaPct: number | null;
  deviationScore: number | null;
  baselineWeeksUsed: number;
  status: PulseStatus;
}

export interface LocationPulse {
  location: string;
  metric: MetricView;
}

export interface WeeklyPulse {
  account: AccountListItem;
  week: { start: string; end: string };
  baseline: { weeks: number; start: string; end: string };
  eventType: string;
  /** Null when the account has no activity at all. */
  total: MetricView | null;
  locations: LocationPulse[];
  dataQuality: {
    hasData: boolean;
    duplicateEventsExcluded: number;
    earliestLocalDate: string | null;
    latestLocalDate: string | null;
  };
}

/** The four pieces of state that live in the URL. */
export interface PulseFilters {
  accountId: number;
  baselineWeeks: number;
  eventType: string;
  weekStart: string | null;
}

export const BASELINE_WEEK_OPTIONS = [4, 8, 12] as const;

export const EVENT_TYPE_OPTIONS = [
  { value: 'all', label: 'All activity' },
  { value: 'call_received', label: 'Calls' },
  { value: 'lead_created', label: 'Leads' },
  { value: 'appointment_set', label: 'Appointments' },
] as const;

export const DEFAULT_FILTERS: PulseFilters = {
  accountId: 1,
  baselineWeeks: 8,
  eventType: 'all',
  weekStart: null,
};

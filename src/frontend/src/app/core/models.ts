export interface ServerTime {
  utcNow: string;
  timeZoneId: string;
}

export interface HealthReport {
  environment: string;
  time: ServerTime;
}

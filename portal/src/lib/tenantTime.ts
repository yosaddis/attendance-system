const TENANT_OFFSET_MS = 3 * 60 * 60 * 1000; // GMT+3 — mirrors AttendanceAnalysisService.DefaultTenantOffset on the backend

export function todayIsoDateTenant(): string {
  return new Date(Date.now() + TENANT_OFFSET_MS).toISOString().slice(0, 10);
}

export function formatTenantTime(isoTimestamp: string): string {
  const shifted = new Date(new Date(isoTimestamp).getTime() + TENANT_OFFSET_MS);
  const hours = String(shifted.getUTCHours()).padStart(2, "0");
  const minutes = String(shifted.getUTCMinutes()).padStart(2, "0");
  return `${hours}:${minutes}`;
}

export type EmployeeResponse = {
  id: string;
  employeeCode: string;
  name: string;
  shiftId: string | null;
};

export type ShiftResponse = {
  id: string;
  name: string;
  startTime: string;
  endTime: string;
  graceMinutes: number;
  punchMode: "TwoPunch" | "FourPunch";
  breakStart: string | null;
  breakEnd: string | null;
  allowedBreakMinutes: number | null;
};

export type AttendanceRowResponse = {
  employeeId: string;
  employeeName: string;
  date: string;
  shiftId: string | null;
  firstIn: string | null;
  lastOut: string | null;
  workedHours: number | null;
  hasShift: boolean;
  isLate: boolean;
  lateMinutes: number | null;
  isMissingCheckout: boolean;
  hasDoublePunch: boolean;
};

export type LoginResult = {
  token: string;
  role: "Operator" | "TenantAdmin";
  tenantId: string | null;
};

export type AttendanceSummaryResponse = {
  totalEmployeesWithShift: number;
  presentCount: number;
  absentCount: number;
  lateCount: number;
};

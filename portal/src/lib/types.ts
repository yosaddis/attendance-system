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

export type DailyAttendanceResponse = {
  employeeId: string;
  employeeName: string;
  firstIn: string | null;
  lastOut: string | null;
};

export type LoginResult = {
  token: string;
  role: "Operator" | "TenantAdmin";
  tenantId: string | null;
};

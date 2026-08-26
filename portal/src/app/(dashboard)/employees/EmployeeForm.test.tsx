import { describe, it, expect } from "vitest";
import { render, screen } from "@testing-library/react";
import { EmployeeForm } from "./EmployeeForm";

describe("EmployeeForm", () => {
  it("renders a shift option for each provided shift", () => {
    render(
      <EmployeeForm
        shifts={[
          {
            id: "s1",
            name: "Day Shift",
            startTime: "09:00:00",
            endTime: "17:00:00",
            graceMinutes: 10,
            punchMode: "TwoPunch",
            breakStart: null,
            breakEnd: null,
            allowedBreakMinutes: null,
          },
        ]}
      />,
    );

    expect(screen.getByRole("option", { name: "Day Shift" })).toBeInTheDocument();
  });

  it("requires employee code and name fields", () => {
    render(<EmployeeForm shifts={[]} />);

    expect(screen.getByLabelText(/employee code/i)).toBeRequired();
    expect(screen.getByLabelText(/^name$/i)).toBeRequired();
  });

  it("pre-fills name and shift, renders the code read-only, and labels the submit button 'Save changes' in edit mode", () => {
    render(
      <EmployeeForm
        shifts={[
          {
            id: "s1",
            name: "Day Shift",
            startTime: "09:00:00",
            endTime: "17:00:00",
            graceMinutes: 10,
            punchMode: "TwoPunch",
            breakStart: null,
            breakEnd: null,
            allowedBreakMinutes: null,
          },
        ]}
        employee={{ id: "e1", employeeCode: "E001", name: "Jane Doe", shiftId: "s1" }}
      />,
    );

    const codeInput = screen.getByLabelText(/employee code/i) as HTMLInputElement;
    expect(codeInput).toHaveValue("E001");
    expect(codeInput).toHaveAttribute("readonly");
    expect(screen.getByLabelText(/^name$/i)).toHaveValue("Jane Doe");
    expect(screen.getByLabelText(/shift/i)).toHaveValue("s1");
    expect(screen.getByRole("button", { name: /save changes/i })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /cancel/i })).toHaveAttribute("href", "/employees");
  });

  it("has no read-only code field and no cancel link when creating (no employee prop)", () => {
    render(<EmployeeForm shifts={[]} />);

    expect(screen.getByLabelText(/employee code/i)).not.toHaveAttribute("readonly");
    expect(screen.getByRole("button", { name: /add employee/i })).toBeInTheDocument();
    expect(screen.queryByRole("link", { name: /cancel/i })).not.toBeInTheDocument();
  });
});

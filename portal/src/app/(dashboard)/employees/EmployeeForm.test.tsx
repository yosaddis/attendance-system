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
});

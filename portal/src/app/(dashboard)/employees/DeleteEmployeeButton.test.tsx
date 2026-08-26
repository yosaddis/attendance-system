import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("./actions", () => ({ deleteEmployee: vi.fn(() => () => {}) }));

import { DeleteEmployeeButton } from "./DeleteEmployeeButton";

describe("DeleteEmployeeButton", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
  });

  it("asks for confirmation naming the employee before deleting", () => {
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    render(<DeleteEmployeeButton id="e1" name="Jane Doe" />);

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));

    expect(confirmSpy).toHaveBeenCalledWith("Delete Jane Doe? This cannot be undone.");
  });
});

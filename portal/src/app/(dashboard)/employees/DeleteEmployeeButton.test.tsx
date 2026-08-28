import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";

vi.mock("./actions", () => ({ deleteEmployee: vi.fn() }));

import { deleteEmployee } from "./actions";
import { DeleteEmployeeButton } from "./DeleteEmployeeButton";

describe("DeleteEmployeeButton", () => {
  beforeEach(() => {
    vi.restoreAllMocks();
    vi.mocked(deleteEmployee).mockClear();
  });

  it("asks for confirmation naming the employee before deleting", () => {
    const confirmSpy = vi.spyOn(window, "confirm").mockReturnValue(false);
    render(<DeleteEmployeeButton id="e1" name="Jane Doe" />);

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));

    expect(confirmSpy).toHaveBeenCalledWith("Delete Jane Doe? This cannot be undone.");
  });

  it("does not submit the delete action when the confirmation is declined", () => {
    vi.spyOn(window, "confirm").mockReturnValue(false);
    render(<DeleteEmployeeButton id="e1" name="Jane Doe" />);

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));

    expect(deleteEmployee).not.toHaveBeenCalled();
  });

  it("submits the delete action when the confirmation is accepted", () => {
    vi.spyOn(window, "confirm").mockReturnValue(true);
    render(<DeleteEmployeeButton id="e1" name="Jane Doe" />);

    fireEvent.click(screen.getByRole("button", { name: /delete/i }));

    expect(deleteEmployee).toHaveBeenCalled();
  });
});

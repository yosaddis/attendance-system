"use client";

import { deleteEmployee } from "./actions";

export function DeleteEmployeeButton({ id, name }: { id: string; name: string }) {
  return (
    <form
      action={deleteEmployee.bind(null, id)}
      onSubmit={(e) => {
        if (!confirm(`Delete ${name}? This cannot be undone.`)) {
          e.preventDefault();
        }
      }}
    >
      <button type="submit" className="text-danger hover:underline text-sm">
        Delete
      </button>
    </form>
  );
}

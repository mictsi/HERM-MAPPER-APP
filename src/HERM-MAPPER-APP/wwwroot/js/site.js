// Please see documentation at https://learn.microsoft.com/aspnet/core/client-side/bundling-and-minification
// for details on configuring this project to bundle and minify static web assets.

document.addEventListener("DOMContentLoaded", () => {
  document.querySelectorAll("[data-owner-dropdown]").forEach((dropdown) => {
    const label = dropdown.querySelector("[data-owner-dropdown-label]");
    const checkboxes = dropdown.querySelectorAll("input[type='checkbox'][name='owners']");
    const clearButton = dropdown.querySelector("[data-owner-clear]");

    if (label === null || checkboxes.length === 0) {
      return;
    }

    const defaultLabel = label.getAttribute("data-default-label") ?? "Filter by owner";

    const syncLabel = () => {
      const checked = Array.from(checkboxes)
        .filter((checkbox) => checkbox instanceof HTMLInputElement && checkbox.checked)
        .map((checkbox) => checkbox.getAttribute("data-owner-label") ?? checkbox.getAttribute("value") ?? "");

      if (checked.length === 0) {
        label.textContent = defaultLabel;
      } else if (checked.length === 1) {
        label.textContent = checked[0];
      } else {
        label.textContent = `${checked.length} owners selected`;
      }
    };

    checkboxes.forEach((checkbox) => {
      checkbox.addEventListener("change", syncLabel);
    });

    if (clearButton instanceof HTMLButtonElement) {
      clearButton.addEventListener("click", () => {
        checkboxes.forEach((checkbox) => {
          if (checkbox instanceof HTMLInputElement) {
            checkbox.checked = false;
          }
        });

        syncLabel();
      });
    }

    syncLabel();
  });

  document.querySelectorAll("[data-bulk-selection-form]").forEach((form) => {
    const checkboxes = form.querySelectorAll("input[type='checkbox'][data-bulk-select-item]");
    const selectAll = form.querySelector("[data-bulk-select-all]");
    const submitButtons = form.querySelectorAll("[data-bulk-submit]");
    const summary = form.querySelector("[data-bulk-selection-summary]");

    if (checkboxes.length === 0) {
      return;
    }

    const syncSelection = () => {
      const checkedCount = Array.from(checkboxes)
        .filter((checkbox) => checkbox instanceof HTMLInputElement && checkbox.checked)
        .length;

      submitButtons.forEach((button) => {
        if (button instanceof HTMLButtonElement) {
          button.disabled = checkedCount === 0;
        }
      });

      if (selectAll instanceof HTMLInputElement) {
        selectAll.checked = checkedCount === checkboxes.length;
        selectAll.indeterminate = checkedCount > 0 && checkedCount < checkboxes.length;
      }

      if (summary !== null) {
        summary.textContent = checkedCount === 0
          ? "Select products to bulk update."
          : `${checkedCount} product(s) selected for bulk edit.`;
      }
    };

    checkboxes.forEach((checkbox) => {
      checkbox.addEventListener("change", syncSelection);
    });

    if (selectAll instanceof HTMLInputElement) {
      selectAll.addEventListener("change", () => {
        checkboxes.forEach((checkbox) => {
          if (checkbox instanceof HTMLInputElement) {
            checkbox.checked = selectAll.checked;
          }
        });

        syncSelection();
      });
    }

    syncSelection();
  });

  document.querySelectorAll("[data-bulk-edit-section]").forEach((section) => {
    const toggle = section.querySelector("[data-bulk-edit-toggle]");
    const controls = section.querySelectorAll("[data-bulk-edit-control]");

    if (!(toggle instanceof HTMLInputElement) || controls.length === 0) {
      return;
    }

    const syncSection = () => {
      section.classList.toggle("is-inactive", !toggle.checked);
      controls.forEach((control) => {
        if (control instanceof HTMLInputElement || control instanceof HTMLSelectElement || control instanceof HTMLTextAreaElement) {
          control.disabled = !toggle.checked;
        }
      });
    };

    toggle.addEventListener("change", syncSection);
    syncSection();
  });
});

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

  document.querySelectorAll("[data-service-editor]").forEach((editor) => {
    const rowsContainer = editor.querySelector("[data-service-rows]");
    const rowTemplate = editor.querySelector("[data-service-row-template]");
    const preview = editor.querySelector("[data-service-preview]");
    const addTailButton = editor.querySelector("[data-service-add-tail]");

    if (
      !(rowsContainer instanceof HTMLElement) ||
      !(rowTemplate instanceof HTMLTemplateElement) ||
      !(preview instanceof HTMLElement)
    ) {
      return;
    }

    const escapeHtml = (value) =>
      String(value)
        .replaceAll("&", "&amp;")
        .replaceAll("<", "&lt;")
        .replaceAll(">", "&gt;")
        .replaceAll('"', "&quot;")
        .replaceAll("'", "&#39;");

    const getRows = () =>
      Array.from(rowsContainer.querySelectorAll("[data-service-row]"))
        .filter((row) => row instanceof HTMLElement);

    const createRow = () => {
      const host = document.createElement("div");
      host.innerHTML = rowTemplate.innerHTML.trim();
      return host.firstElementChild;
    };

    const ensureMinimumRows = () => {
      while (getRows().length < 2) {
        const row = createRow();
        if (row instanceof HTMLElement) {
          rowsContainer.appendChild(row);
        } else {
          break;
        }
      }
    };

    const reindexRows = () => {
      const rows = getRows();
      rows.forEach((row, index) => {
        const order = row.querySelector("[data-service-order]");
        const select = row.querySelector("[data-service-product-select]");
        const label = row.querySelector("label");
        const moveUpButton = row.querySelector("[data-service-move-up]");
        const moveDownButton = row.querySelector("[data-service-move-down]");

        if (order instanceof HTMLElement) {
          order.textContent = String(index + 1);
        }

        if (select instanceof HTMLSelectElement) {
          const selectId = `ProductRows_${index}__ProductId`;
          select.id = selectId;
          select.name = `ProductRows[${index}].ProductId`;

          if (label instanceof HTMLLabelElement) {
            label.htmlFor = selectId;
          }
        }

        if (moveUpButton instanceof HTMLButtonElement) {
          moveUpButton.disabled = index === 0;
        }

        if (moveDownButton instanceof HTMLButtonElement) {
          moveDownButton.disabled = index === rows.length - 1;
        }
      });
    };

    const getSelectedProducts = () =>
      getRows()
        .map((row) => row.querySelector("[data-service-product-select]"))
        .filter((select) => select instanceof HTMLSelectElement && select.value !== "")
        .map((select) => ({
          id: select.value,
          name: select.options[select.selectedIndex]?.text ?? select.value
        }));

    const renderPreview = () => {
      const products = getSelectedProducts();

      if (products.length < 2) {
        preview.innerHTML = `
          <div class="empty-state compact">
            <h3>No complete flow yet</h3>
            <p>Choose at least two products to build the service flow.</p>
          </div>`;
        return;
      }

      preview.innerHTML = `
        <div class="service-chain" aria-label="Service product flow">
          ${products.map((product, index) => `
            <div class="service-chain-node">${escapeHtml(product.name)}</div>
            ${index < products.length - 1 ? '<div class="service-chain-arrow" aria-hidden="true">&rarr;</div>' : ""}
          `).join("")}
        </div>`;
    };

    rowsContainer.addEventListener("click", (event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      const button = target.closest("button");
      const row = target.closest("[data-service-row]");

      if (!(button instanceof HTMLButtonElement) || !(row instanceof HTMLElement)) {
        return;
      }

      if (button.hasAttribute("data-service-add-row")) {
        const newRow = createRow();
        if (newRow instanceof HTMLElement) {
          row.insertAdjacentElement("afterend", newRow);
        }
      } else if (button.hasAttribute("data-service-move-up")) {
        const previousRow = row.previousElementSibling;
        if (previousRow instanceof HTMLElement) {
          rowsContainer.insertBefore(row, previousRow);
        }
      } else if (button.hasAttribute("data-service-move-down")) {
        const nextRow = row.nextElementSibling;
        if (nextRow instanceof HTMLElement) {
          rowsContainer.insertBefore(nextRow, row);
        }
      } else if (button.hasAttribute("data-service-remove-row")) {
        row.remove();
        ensureMinimumRows();
      } else {
        return;
      }

      reindexRows();
      renderPreview();
    });

    rowsContainer.addEventListener("change", (event) => {
      const target = event.target;
      if (target instanceof HTMLSelectElement && target.hasAttribute("data-service-product-select")) {
        renderPreview();
      }
    });

    if (addTailButton instanceof HTMLButtonElement) {
      addTailButton.addEventListener("click", () => {
        const row = createRow();
        if (row instanceof HTMLElement) {
          rowsContainer.appendChild(row);
          reindexRows();
          renderPreview();
        }
      });
    }

    ensureMinimumRows();
    reindexRows();
    renderPreview();
  });
});

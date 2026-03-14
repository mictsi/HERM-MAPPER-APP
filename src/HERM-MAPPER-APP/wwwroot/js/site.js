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
    let draggedRow = null;

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
        .replaceAll("\"", "&quot;")
        .replaceAll("'", "&#39;");

    const getRows = () =>
      Array.from(rowsContainer.querySelectorAll("[data-service-row]"))
        .filter((row) => row instanceof HTMLElement);

    const createRow = () => {
      const host = document.createElement("div");
      host.innerHTML = rowTemplate.innerHTML.trim();
      const row = host.firstElementChild;
      return row instanceof HTMLElement ? row : null;
    };

    const clearDragState = () => {
      getRows().forEach((row) => {
        row.classList.remove("is-dragging", "drag-over-top", "drag-over-bottom");
      });
    };

    const ensureMinimumRows = () => {
      while (getRows().length < 2) {
        const row = createRow();
        if (row === null) {
          break;
        }

        rowsContainer.appendChild(row);
      }
    };

    const reindexRows = () => {
      const rows = getRows();
      rows.forEach((row, index) => {
        const order = row.querySelector("[data-service-order]");
        const select = row.querySelector("[data-service-product-select]");

        row.draggable = true;

        if (order instanceof HTMLElement) {
          order.textContent = String(index + 1);
        }

        if (select instanceof HTMLSelectElement) {
          select.id = `ProductRows_${index}__ProductId`;
          select.name = `ProductRows[${index}].ProductId`;
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
            ${index < products.length - 1 ? "<div class=\"service-chain-arrow\" aria-hidden=\"true\">&rarr;</div>" : ""}
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
        if (newRow !== null) {
          row.insertAdjacentElement("afterend", newRow);
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

    rowsContainer.addEventListener("dragstart", (event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      const row = target.closest("[data-service-row]");
      if (!(row instanceof HTMLElement)) {
        return;
      }

      draggedRow = row;
      clearDragState();
      row.classList.add("is-dragging");

      if (event.dataTransfer !== null) {
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", row.querySelector("[data-service-order]")?.textContent ?? "");
      }
    });

    rowsContainer.addEventListener("dragover", (event) => {
      if (!(draggedRow instanceof HTMLElement)) {
        return;
      }

      event.preventDefault();

      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      const row = target.closest("[data-service-row]");
      clearDragState();
      draggedRow.classList.add("is-dragging");

      if (!(row instanceof HTMLElement) || row === draggedRow) {
        rowsContainer.appendChild(draggedRow);
        reindexRows();
        return;
      }

      const bounds = row.getBoundingClientRect();
      const insertBefore = event.clientY < bounds.top + (bounds.height / 2);

      row.classList.add(insertBefore ? "drag-over-top" : "drag-over-bottom");

      if (insertBefore) {
        rowsContainer.insertBefore(draggedRow, row);
      } else {
        rowsContainer.insertBefore(draggedRow, row.nextElementSibling);
      }

      reindexRows();
    });

    rowsContainer.addEventListener("drop", (event) => {
      if (draggedRow instanceof HTMLElement) {
        event.preventDefault();
        reindexRows();
        renderPreview();
      }
    });

    rowsContainer.addEventListener("dragend", () => {
      clearDragState();
      draggedRow = null;
      reindexRows();
      renderPreview();
    });

    rowsContainer.addEventListener("dragleave", (event) => {
      const target = event.target;
      if (target instanceof HTMLElement && target === rowsContainer) {
        clearDragState();
      }
    });

    if (addTailButton instanceof HTMLButtonElement) {
      addTailButton.addEventListener("click", () => {
        const row = createRow();
        if (row !== null) {
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

  const escapeGraphHtml = (value) =>
    String(value)
      .replaceAll("&", "&amp;")
      .replaceAll("<", "&lt;")
      .replaceAll(">", "&gt;")
      .replaceAll("\"", "&quot;")
      .replaceAll("'", "&#39;");

  const graphBranchPalette = [
    { surface: "rgba(11, 110, 79, 0.10)", border: "rgba(11, 110, 79, 0.42)" },
    { surface: "rgba(43, 111, 119, 0.10)", border: "rgba(43, 111, 119, 0.42)" },
    { surface: "rgba(92, 124, 53, 0.10)", border: "rgba(92, 124, 53, 0.42)" },
    { surface: "rgba(139, 94, 52, 0.10)", border: "rgba(139, 94, 52, 0.42)" },
    { surface: "rgba(123, 63, 97, 0.10)", border: "rgba(123, 63, 97, 0.42)" },
    { surface: "rgba(62, 89, 157, 0.10)", border: "rgba(62, 89, 157, 0.42)" }
  ];

  const sortByFirstAppearance = (values, firstAppearance) =>
    Array.from(values ?? [])
      .sort((left, right) => (firstAppearance.get(left) ?? 0) - (firstAppearance.get(right) ?? 0));

  const averageNumbers = (values) =>
    values.length === 0
      ? Number.POSITIVE_INFINITY
      : values.reduce((sum, value) => sum + value, 0) / values.length;

  const normalizeGraphConnection = (connection) => {
    if (connection === null || typeof connection !== "object") {
      return null;
    }

    const read = (...names) => {
      for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(connection, name) &&
          connection[name] !== null &&
          connection[name] !== undefined) {
          return connection[name];
        }
      }

      return "";
    };

    return {
      sequence: Number(read("sequence", "Sequence", "sortOrder", "SortOrder")) || 0,
      fromId: String(read("fromProductId", "FromProductId", "fromId", "FromId")),
      toId: String(read("toProductId", "ToProductId", "toId", "ToId")),
      fromName: String(read("fromProductName", "FromProductName", "fromName", "FromName")),
      toName: String(read("toProductName", "ToProductName", "toName", "ToName"))
    };
  };

  const analyzeGraph = (connections) => {
    const firstAppearance = new Map();
    const adjacency = new Map();
    const incoming = new Map();
    const indegree = new Map();
    const labels = new Map();
    let appearanceIndex = 0;

    const ensureNode = (id, label) => {
      if (!firstAppearance.has(id)) {
        firstAppearance.set(id, appearanceIndex);
        appearanceIndex += 1;
      }

      if (!adjacency.has(id)) {
        adjacency.set(id, new Set());
      }

      if (!incoming.has(id)) {
        incoming.set(id, new Set());
      }

      if (!indegree.has(id)) {
        indegree.set(id, 0);
      }

      if (!labels.has(id)) {
        labels.set(id, label);
      }
    };

    connections.forEach((connection) => {
      ensureNode(connection.fromId, connection.fromName);
      ensureNode(connection.toId, connection.toName);

      const neighbors = adjacency.get(connection.fromId);
      const parents = incoming.get(connection.toId);
      if (neighbors instanceof Set && parents instanceof Set && !neighbors.has(connection.toId)) {
        neighbors.add(connection.toId);
        parents.add(connection.fromId);
        indegree.set(connection.toId, (indegree.get(connection.toId) ?? 0) + 1);
      }
    });

    const levels = new Map(Array.from(indegree.keys(), (id) => [id, 0]));
    const remainingIndegree = new Map(indegree);
    const ready = Array.from(remainingIndegree.entries())
      .filter(([, degree]) => degree === 0)
      .map(([id]) => id)
      .sort((left, right) => (firstAppearance.get(left) ?? 0) - (firstAppearance.get(right) ?? 0));

    const orderedIds = [];

    while (ready.length !== 0) {
      const currentId = ready.shift();
      if (currentId === undefined) {
        break;
      }

      orderedIds.push(currentId);

      sortByFirstAppearance(adjacency.get(currentId), firstAppearance)
        .forEach((nextId) => {
          levels.set(nextId, Math.max(levels.get(nextId) ?? 0, (levels.get(currentId) ?? 0) + 1));
          remainingIndegree.set(nextId, (remainingIndegree.get(nextId) ?? 0) - 1);
          if (remainingIndegree.get(nextId) === 0) {
            ready.push(nextId);
            ready.sort((left, right) => (firstAppearance.get(left) ?? 0) - (firstAppearance.get(right) ?? 0));
          }
        });
    }

    const supportsGraphLayout = orderedIds.length === remainingIndegree.size;
    const orderedNodeIds = supportsGraphLayout
      ? orderedIds
      : Array.from(firstAppearance.keys())
        .sort((left, right) => (firstAppearance.get(left) ?? 0) - (firstAppearance.get(right) ?? 0));
    const roots = orderedNodeIds.filter((id) => (incoming.get(id)?.size ?? 0) === 0);
    const rootSets = new Map();

    if (supportsGraphLayout) {
      orderedIds.forEach((id) => {
        const parentIds = sortByFirstAppearance(incoming.get(id), firstAppearance);
        if (parentIds.length === 0) {
          rootSets.set(id, new Set([id]));
          return;
        }

        const rootIds = new Set();
        parentIds.forEach((parentId) => {
          const parentRoots = rootSets.get(parentId);
          if (parentRoots instanceof Set && parentRoots.size !== 0) {
            parentRoots.forEach((rootId) => rootIds.add(rootId));
          } else {
            rootIds.add(parentId);
          }
        });

        if (rootIds.size === 0) {
          rootIds.add(id);
        }

        rootSets.set(id, rootIds);
      });
    }

    const primaryRootById = new Map();
    const sharedRootCountById = new Map();

    orderedNodeIds.forEach((id) => {
      const resolvedRoots = rootSets.has(id)
        ? sortByFirstAppearance(rootSets.get(id), firstAppearance)
        : ((incoming.get(id)?.size ?? 0) === 0
          ? [id]
          : [sortByFirstAppearance(incoming.get(id), firstAppearance)[0] ?? id]);

      primaryRootById.set(id, resolvedRoots[0] ?? id);
      sharedRootCountById.set(id, resolvedRoots.length);
    });

    return {
      supportsGraphLayout,
      adjacency,
      incoming,
      labels,
      firstAppearance,
      levels,
      roots,
      orderedNodeIds,
      primaryRootById,
      sharedRootCountById
    };
  };

  const buildGraphLayout = (analysis) => {
    if (!analysis.supportsGraphLayout) {
      return {
        supportsGraphLayout: false,
        nodes: analysis.orderedNodeIds.map((id) => ({ id, label: analysis.labels.get(id) ?? id }))
      };
    }

    const nodesByLevel = new Map();
    analysis.orderedNodeIds.forEach((id) => {
      const level = analysis.levels.get(id) ?? 0;
      if (!nodesByLevel.has(level)) {
        nodesByLevel.set(level, []);
      }

      nodesByLevel.get(level).push(id);
    });

    const horizontalOrderById = new Map();
    const rows = Array.from(nodesByLevel.entries())
      .sort((left, right) => left[0] - right[0])
      .map(([, ids]) => {
        const sortedIds = [...ids].sort((left, right) => {
          const leftRoot = analysis.primaryRootById.get(left) ?? left;
          const rightRoot = analysis.primaryRootById.get(right) ?? right;
          const rootDiff = (analysis.firstAppearance.get(leftRoot) ?? 0) - (analysis.firstAppearance.get(rightRoot) ?? 0);
          if (rootDiff !== 0) {
            return rootDiff;
          }

          const leftParents = sortByFirstAppearance(analysis.incoming.get(left), analysis.firstAppearance);
          const rightParents = sortByFirstAppearance(analysis.incoming.get(right), analysis.firstAppearance);
          const leftAnchor = leftParents.length === 0
            ? (analysis.firstAppearance.get(left) ?? 0)
            : averageNumbers(leftParents.map((parentId) => horizontalOrderById.get(parentId) ?? (analysis.firstAppearance.get(parentId) ?? 0)));
          const rightAnchor = rightParents.length === 0
            ? (analysis.firstAppearance.get(right) ?? 0)
            : averageNumbers(rightParents.map((parentId) => horizontalOrderById.get(parentId) ?? (analysis.firstAppearance.get(parentId) ?? 0)));
          if (leftAnchor !== rightAnchor) {
            return leftAnchor - rightAnchor;
          }

          return (analysis.firstAppearance.get(left) ?? 0) - (analysis.firstAppearance.get(right) ?? 0);
        });

        sortedIds.forEach((id, index) => {
          horizontalOrderById.set(id, index);
        });

        return sortedIds.map((id) => ({
          id,
          label: analysis.labels.get(id) ?? id
        }));
      });

    return {
      supportsGraphLayout: true,
      rows
    };
  };

  const buildGraphRoutes = (analysis, maxRoutes = 24) => {
    if (!analysis.supportsGraphLayout) {
      return {
        routes: [],
        truncated: false
      };
    }

    const routes = [];
    let truncated = false;
    const roots = analysis.roots.length !== 0 ? analysis.roots : analysis.orderedNodeIds;

    const visit = (currentId, path) => {
      if (routes.length >= maxRoutes) {
        truncated = true;
        return;
      }

      const nextIds = sortByFirstAppearance(analysis.adjacency.get(currentId), analysis.firstAppearance);
      const nextPath = [...path, currentId];

      if (nextIds.length === 0) {
        routes.push(nextPath.map((id) => analysis.labels.get(id) ?? id));
        return;
      }

      nextIds.forEach((nextId) => {
        if (!truncated) {
          visit(nextId, nextPath);
        }
      });
    };

    roots.forEach((rootId) => {
      if (!truncated) {
        visit(rootId, []);
      }
    });

    return {
      routes,
      truncated
    };
  };

  const renderGraphRoutes = (analysis) => {
    const { routes, truncated } = buildGraphRoutes(analysis);
    if (routes.length === 0) {
      return "";
    }

    return `
      <div class="service-graph-routes">
        <div class="service-graph-routes-heading">
          <h3>Distinct routes</h3>
          <p>Expanded routes make branches and merges easier to follow.</p>
        </div>
        <div class="service-graph-route-list">
          ${routes.map((route, index) => `
            <article class="service-graph-route-card">
              <div class="service-graph-route-index">Route ${index + 1}</div>
              <div class="service-graph-route-steps">
                ${route.map((step, stepIndex) => `
                  <span class="service-graph-route-step">${escapeGraphHtml(step)}</span>
                  ${stepIndex < route.length - 1 ? "<span class=\"service-graph-route-arrow\" aria-hidden=\"true\">&rarr;</span>" : ""}
                `).join("")}
              </div>
            </article>`).join("")}
        </div>
        ${truncated ? `<div class="service-graph-route-note">Showing the first ${routes.length} routes to keep the preview readable.</div>` : ""}
      </div>`;
  };

  const renderGraphRoutesPanel = (routesPanel, connections, analysisInput = null) => {
    if (!(routesPanel instanceof HTMLElement)) {
      return;
    }

    const emptyTitle = routesPanel.dataset.emptyTitle ?? "No routes yet";
    const emptyBody = routesPanel.dataset.emptyBody ?? "Add connected products to build distinct routes.";
    const unavailableTitle = routesPanel.dataset.unavailableTitle ?? "Routes unavailable";
    const unavailableBody = routesPanel.dataset.unavailableBody ?? "Distinct routes are only shown when the flow can be rendered without loops.";

    if (connections.length === 0) {
      routesPanel.innerHTML = `
        <div class="empty-state compact">
          <h3>${escapeGraphHtml(emptyTitle)}</h3>
          <p>${escapeGraphHtml(emptyBody)}</p>
        </div>`;
      return;
    }

    const analysis = analysisInput ?? analyzeGraph(connections);
    const markup = renderGraphRoutes(analysis);
    if (markup !== "") {
      routesPanel.innerHTML = markup;
      return;
    }

    routesPanel.innerHTML = `
      <div class="empty-state compact">
        <h3>${escapeGraphHtml(unavailableTitle)}</h3>
        <p>${escapeGraphHtml(unavailableBody)}</p>
      </div>`;
  };

  const drawGraphLines = (preview, connections) => {
    const canvas = preview.querySelector("[data-service-graph-canvas]");
    const svg = preview.querySelector(".service-graph-lines");

    if (!(canvas instanceof HTMLElement) || !(svg instanceof SVGSVGElement)) {
      return;
    }

    const canvasRect = canvas.getBoundingClientRect();
    const width = Math.max(canvas.scrollWidth, canvas.clientWidth);
    const height = Math.max(canvas.scrollHeight, canvas.clientHeight);

    svg.setAttribute("viewBox", `0 0 ${width} ${height}`);
    svg.setAttribute("width", String(width));
    svg.setAttribute("height", String(height));

    svg.innerHTML = connections
      .map((connection) => {
        const fromNode = canvas.querySelector(`[data-service-graph-node][data-node-id="${connection.fromId}"]`);
        const toNode = canvas.querySelector(`[data-service-graph-node][data-node-id="${connection.toId}"]`);

        if (!(fromNode instanceof HTMLElement) || !(toNode instanceof HTMLElement)) {
          return "";
        }

        const fromRect = fromNode.getBoundingClientRect();
        const toRect = toNode.getBoundingClientRect();
        const startX = fromRect.left - canvasRect.left + canvas.scrollLeft + (fromRect.width / 2);
        const startY = fromRect.bottom - canvasRect.top + canvas.scrollTop;
        const endX = toRect.left - canvasRect.left + canvas.scrollLeft + (toRect.width / 2);
        const endY = toRect.top - canvasRect.top + canvas.scrollTop;
        const controlOffset = Math.max(48, (endY - startY) * 0.35);

        return `<path class="service-graph-line" d="M ${startX} ${startY} C ${startX} ${startY + controlOffset}, ${endX} ${endY - controlOffset}, ${endX} ${endY}" />`;
      })
      .join("");
  };

  const renderGraphPreview = (preview, connections, analysisInput = null) => {
    if (!(preview instanceof HTMLElement)) {
      return;
    }

    const emptyTitle = preview.dataset.emptyTitle ?? "No connected products yet";
    const emptyBody = preview.dataset.emptyBody ?? "Add at least one complete connection to preview the service flow.";
    const loopTitle = preview.dataset.loopTitle ?? "Loop detected";
    const loopBody = preview.dataset.loopBody ?? "This flow includes a loop, so the preview is shown as connection pairs instead of a top-to-bottom graph.";

    if (connections.length === 0) {
      preview.innerHTML = `
        <div class="empty-state compact">
          <h3>${escapeGraphHtml(emptyTitle)}</h3>
          <p>${escapeGraphHtml(emptyBody)}</p>
        </div>`;
      preview._serviceGraphConnections = null;
      return;
    }

    const analysis = analysisInput ?? analyzeGraph(connections);
    const layout = buildGraphLayout(analysis);
    if (!layout.supportsGraphLayout) {
      preview.innerHTML = `
        <div class="service-graph-fallback">
          <div class="alert alert-info mb-3">${escapeGraphHtml(loopTitle)}. ${escapeGraphHtml(loopBody)}</div>
          <div class="service-graph-edge-list">
            ${connections.map((connection) => `
              <article class="service-graph-edge-card">
                <div class="service-graph-edge-label">${escapeGraphHtml(connection.fromName)}</div>
                <div class="service-graph-edge-arrow" aria-hidden="true">&rarr;</div>
                <div class="service-graph-edge-label">${escapeGraphHtml(connection.toName)}</div>
              </article>`).join("")}
          </div>
        </div>`;
      preview._serviceGraphConnections = null;
      return;
    }

    preview.innerHTML = `
      <div class="service-graph-canvas" data-service-graph-canvas>
        <svg class="service-graph-lines" aria-hidden="true"></svg>
        <div class="service-graph-rows">
          ${layout.rows.map((row, index) => `
            <div class="service-graph-row" data-service-graph-row="${index}">
              ${row.map((node) => `
                <div class="service-graph-node" data-service-graph-node data-node-id="${escapeGraphHtml(node.id)}">${escapeGraphHtml(node.label)}</div>
              `).join("")}
            </div>`).join("")}
        </div>
      </div>`;

    preview._serviceGraphConnections = connections;
    requestAnimationFrame(() => drawGraphLines(preview, connections));
  };

  document.querySelectorAll("[data-service-graph-preview]").forEach((preview) => {
    const graphHost = preview.closest("[data-service-graph-host]");
    const routesPanel = graphHost?.querySelector("[data-service-graph-routes]");
    const dataScript = preview.querySelector("[data-service-graph-data]");
    if (!(dataScript instanceof HTMLScriptElement)) {
      return;
    }

    try {
      const parsed = JSON.parse(dataScript.textContent ?? "[]");
      const connections = Array.isArray(parsed)
        ? parsed
          .map(normalizeGraphConnection)
          .filter((connection) => connection !== null)
          .filter((connection) => connection.fromId !== "" && connection.toId !== "")
        : [];
      const analysis = analyzeGraph(connections);

      renderGraphPreview(preview, connections, analysis);
      renderGraphRoutesPanel(routesPanel, connections, analysis);
    } catch (error) {
      console.error("Unable to render service graph preview", error);
    }
  });

  document.querySelectorAll("[data-service-connection-editor]").forEach((editor) => {
    const rowsContainer = editor.querySelector("[data-service-connection-rows]");
    const rowTemplate = editor.querySelector("[data-service-connection-row-template]");
    const addTailButton = editor.querySelector("[data-service-connection-add-tail]");
    const organizeButton = editor.querySelector("[data-service-connection-organize]");
    const host = editor.closest("form") ?? editor;
    const graphHost = host.querySelector("[data-service-graph-host]") ?? host;
    const preview = graphHost.querySelector("[data-service-graph-preview]");
    const routesPanel = graphHost.querySelector("[data-service-graph-routes]");

    if (
      !(rowsContainer instanceof HTMLElement) ||
      !(rowTemplate instanceof HTMLTemplateElement) ||
      !(preview instanceof HTMLElement)
    ) {
      return;
    }

    const getRows = () =>
      Array.from(rowsContainer.querySelectorAll("[data-service-connection-row]"))
        .filter((row) => row instanceof HTMLElement);

    const getRowStates = () =>
      getRows().map((row, index) => {
        const fromSelect = row.querySelector("[data-service-connection-from]");
        const toSelect = row.querySelector("[data-service-connection-to]");
        const summary = row.querySelector("[data-service-connection-summary]");
        const fromId = fromSelect instanceof HTMLSelectElement ? fromSelect.value : "";
        const toId = toSelect instanceof HTMLSelectElement ? toSelect.value : "";

        return {
          row,
          index,
          summary,
          fromSelect,
          toSelect,
          fromId,
          toId,
          fromName: fromSelect instanceof HTMLSelectElement && fromId !== ""
            ? (fromSelect.options[fromSelect.selectedIndex]?.text ?? fromId)
            : "",
          toName: toSelect instanceof HTMLSelectElement && toId !== ""
            ? (toSelect.options[toSelect.selectedIndex]?.text ?? toId)
            : ""
        };
      });

    const createRow = () => {
      const wrapper = document.createElement("div");
      wrapper.innerHTML = rowTemplate.innerHTML.trim();
      const row = wrapper.firstElementChild;
      return row instanceof HTMLElement ? row : null;
    };

    const seedRowFromPreviousTo = (newRow, previousRow) => {
      if (!(newRow instanceof HTMLElement) || !(previousRow instanceof HTMLElement)) {
        return;
      }

      const newFromSelect = newRow.querySelector("[data-service-connection-from]");
      const previousToSelect = previousRow.querySelector("[data-service-connection-to]");

      if (newFromSelect instanceof HTMLSelectElement &&
        previousToSelect instanceof HTMLSelectElement &&
        previousToSelect.value !== "") {
        newFromSelect.value = previousToSelect.value;
      }
    };

    const ensureMinimumRows = () => {
      while (getRows().length < 1) {
        const row = createRow();
        if (row === null) {
          break;
        }

        rowsContainer.appendChild(row);
      }
    };

    const reindexRows = () => {
      getRows().forEach((row, index) => {
        const order = row.querySelector("[data-service-connection-order]");
        const fromSelect = row.querySelector("[data-service-connection-from]");
        const toSelect = row.querySelector("[data-service-connection-to]");

        if (order instanceof HTMLElement) {
          order.textContent = String(index + 1);
        }

        if (fromSelect instanceof HTMLSelectElement) {
          fromSelect.id = `ConnectionRows_${index}__FromProductId`;
          fromSelect.name = `ConnectionRows[${index}].FromProductId`;
        }

        if (toSelect instanceof HTMLSelectElement) {
          toSelect.id = `ConnectionRows_${index}__ToProductId`;
          toSelect.name = `ConnectionRows[${index}].ToProductId`;
        }
      });
    };

    const collectConnections = () =>
      getRowStates()
        .filter((state) => state.fromId !== "" && state.toId !== "")
        .map((state) => ({
          fromId: state.fromId,
          toId: state.toId,
          fromName: state.fromName,
          toName: state.toName
        }));

    const clearBranchDividers = () => {
      rowsContainer.querySelectorAll("[data-service-connection-divider]").forEach((divider) => divider.remove());
    };

    const renderBadge = (kind, label) =>
      `<span class="service-connection-badge ${kind}">${escapeGraphHtml(label)}</span>`;

    const syncBranchPresentation = () => {
      clearBranchDividers();

      const rowStates = getRowStates();
      const completedStates = rowStates.filter((state) => state.fromId !== "" && state.toId !== "");
      const analysis = analyzeGraph(completedStates.map((state) => ({
        fromId: state.fromId,
        toId: state.toId,
        fromName: state.fromName,
        toName: state.toName
      })));
      const branchOrder = new Map(
        (analysis.roots.length !== 0 ? analysis.roots : analysis.orderedNodeIds)
          .map((rootId, index) => [rootId, index]));
      const metadataByRow = new Map();

      completedStates.forEach((state) => {
        const branchRootId = analysis.primaryRootById.get(state.fromId) ?? state.fromId;
        const branchLabel = analysis.labels.get(branchRootId) ?? state.fromName;
        const tone = graphBranchPalette[(branchOrder.get(branchRootId) ?? 0) % graphBranchPalette.length];
        const badges = [
          renderBadge("is-branch", `Branch from ${branchLabel}`)
        ];

        if ((analysis.adjacency.get(state.fromId)?.size ?? 0) > 1) {
          badges.push(renderBadge("is-split", `Splits at ${state.fromName}`));
        }

        if ((analysis.incoming.get(state.toId)?.size ?? 0) > 1) {
          badges.push(renderBadge("is-merge", `Merges into ${state.toName}`));
        }

        if ((analysis.sharedRootCountById.get(state.toId) ?? 1) > 1) {
          badges.push(renderBadge("is-merge", "Shared route"));
        }

        metadataByRow.set(state.row, {
          branchRootId,
          branchLabel,
          tone,
          badges
        });
      });

      let previousBranchRootId = null;
      rowStates.forEach((state) => {
        state.row.style.removeProperty("--service-connection-accent");
        state.row.style.removeProperty("--service-connection-accent-soft");

        if (state.summary instanceof HTMLElement) {
          state.summary.innerHTML = "";
        }

        const metadata = metadataByRow.get(state.row);
        if (metadata === undefined) {
          if (state.summary instanceof HTMLElement) {
            state.summary.innerHTML = renderBadge("is-draft", "Complete both products to place this connection in a branch.");
          }

          return;
        }

        state.row.style.setProperty("--service-connection-accent", metadata.tone.border);
        state.row.style.setProperty("--service-connection-accent-soft", metadata.tone.surface);

        if (metadata.branchRootId !== previousBranchRootId) {
          const divider = document.createElement("div");
          divider.className = "service-connection-branch-divider";
          divider.setAttribute("data-service-connection-divider", "");
          divider.innerHTML = `Branch from <strong>${escapeGraphHtml(metadata.branchLabel)}</strong>`;
          rowsContainer.insertBefore(divider, state.row);
          previousBranchRootId = metadata.branchRootId;
        }

        if (state.summary instanceof HTMLElement) {
          state.summary.innerHTML = metadata.badges.join("");
        }
      });
    };

    const organizeRowsByBranch = () => {
      const rowStates = getRowStates();
      const completedStates = rowStates.filter((state) => state.fromId !== "" && state.toId !== "");
      if (completedStates.length < 2) {
        return;
      }

      const analysis = analyzeGraph(completedStates.map((state) => ({
        fromId: state.fromId,
        toId: state.toId,
        fromName: state.fromName,
        toName: state.toName
      })));

      const sortCompletedStates = [...completedStates].sort((left, right) => {
        const leftRoot = analysis.primaryRootById.get(left.fromId) ?? left.fromId;
        const rightRoot = analysis.primaryRootById.get(right.fromId) ?? right.fromId;
        const rootDiff = (analysis.firstAppearance.get(leftRoot) ?? Number.MAX_SAFE_INTEGER) -
          (analysis.firstAppearance.get(rightRoot) ?? Number.MAX_SAFE_INTEGER);
        if (rootDiff !== 0) {
          return rootDiff;
        }

        const levelDiff = (analysis.levels.get(left.fromId) ?? Number.MAX_SAFE_INTEGER) -
          (analysis.levels.get(right.fromId) ?? Number.MAX_SAFE_INTEGER);
        if (levelDiff !== 0) {
          return levelDiff;
        }

        const fromDiff = (analysis.firstAppearance.get(left.fromId) ?? Number.MAX_SAFE_INTEGER) -
          (analysis.firstAppearance.get(right.fromId) ?? Number.MAX_SAFE_INTEGER);
        if (fromDiff !== 0) {
          return fromDiff;
        }

        const toDiff = (analysis.firstAppearance.get(left.toId) ?? Number.MAX_SAFE_INTEGER) -
          (analysis.firstAppearance.get(right.toId) ?? Number.MAX_SAFE_INTEGER);
        if (toDiff !== 0) {
          return toDiff;
        }

        return left.index - right.index;
      });

      const incompleteStates = rowStates.filter((state) => state.fromId === "" || state.toId === "");
      [...sortCompletedStates, ...incompleteStates].forEach((state) => {
        rowsContainer.appendChild(state.row);
      });
    };

    const syncPreview = () => {
      const connections = collectConnections();
      const analysis = analyzeGraph(connections);

      syncBranchPresentation();
      renderGraphPreview(preview, connections, analysis);
      renderGraphRoutesPanel(routesPanel, connections, analysis);
    };

    rowsContainer.addEventListener("click", (event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      const button = target.closest("button");
      const row = target.closest("[data-service-connection-row]");

      if (!(button instanceof HTMLButtonElement) || !(row instanceof HTMLElement)) {
        return;
      }

      if (button.hasAttribute("data-service-connection-add-row")) {
        const newRow = createRow();
        if (newRow !== null) {
          seedRowFromPreviousTo(newRow, row);
          row.insertAdjacentElement("afterend", newRow);
        }
      } else if (button.hasAttribute("data-service-connection-remove-row")) {
        row.remove();
        ensureMinimumRows();
      } else {
        return;
      }

      reindexRows();
      syncPreview();
    });

    rowsContainer.addEventListener("change", (event) => {
      const target = event.target;
      if (target instanceof HTMLSelectElement &&
        (target.hasAttribute("data-service-connection-from") || target.hasAttribute("data-service-connection-to"))) {
        syncPreview();
      }
    });

    if (addTailButton instanceof HTMLButtonElement) {
      addTailButton.addEventListener("click", () => {
        const row = createRow();
        const previousRow = getRows().at(-1);
        if (row !== null) {
          if (previousRow instanceof HTMLElement) {
            seedRowFromPreviousTo(row, previousRow);
          }

          rowsContainer.appendChild(row);
          reindexRows();
          syncPreview();
        }
      });
    }

    if (organizeButton instanceof HTMLButtonElement) {
      organizeButton.addEventListener("click", () => {
        organizeRowsByBranch();
        reindexRows();
        syncPreview();
      });
    }

    ensureMinimumRows();
    reindexRows();
    syncPreview();
  });

  window.addEventListener("resize", () => {
    document.querySelectorAll("[data-service-graph-preview]").forEach((preview) => {
      if (preview instanceof HTMLElement && Array.isArray(preview._serviceGraphConnections)) {
        drawGraphLines(preview, preview._serviceGraphConnections);
      }
    });
  });

  document.querySelectorAll("[data-password-strength-root]").forEach((root) => {
    const input = root.querySelector("[data-password-input]");
    const bar = root.querySelector("[data-password-strength-bar]");
    const label = root.querySelector("[data-password-strength-label]");

    if (!(input instanceof HTMLInputElement) || !(bar instanceof HTMLElement) || !(label instanceof HTMLElement)) {
      return;
    }

    const calculateScore = (value) => {
      let score = 0;

      if (value.length >= 12) {
        score += 35;
      }

      if (value.length >= 16) {
        score += 10;
      }

      if (/[a-z]/.test(value)) {
        score += 15;
      }

      if (/[A-Z]/.test(value)) {
        score += 15;
      }

      if (/\d/.test(value)) {
        score += 15;
      }

      if (/[^A-Za-z0-9]/.test(value)) {
        score += 10;
      }

      return Math.max(0, Math.min(100, score));
    };

    const syncStrength = () => {
      const score = calculateScore(input.value);
      let tone = "weak";
      let text = "Weak";

      if (score >= 80) {
        tone = "strong";
        text = "Strong";
      } else if (score >= 55) {
        tone = "medium";
        text = "Medium";
      }

      bar.style.width = `${score}%`;
      bar.setAttribute("data-strength-tone", tone);
      label.textContent = text;
    };

    input.addEventListener("input", syncStrength);
    syncStrength();
  });
});

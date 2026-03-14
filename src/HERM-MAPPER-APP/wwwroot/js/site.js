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

    return {
      supportsGraphLayout,
      adjacency,
      incoming,
      labels,
      firstAppearance,
      levels,
      roots,
      orderedNodeIds
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

          const leftChildren = sortByFirstAppearance(analysis.adjacency.get(left), analysis.firstAppearance);
          const rightChildren = sortByFirstAppearance(analysis.adjacency.get(right), analysis.firstAppearance);
          const leftChildAnchor = leftChildren.length === 0
            ? (analysis.firstAppearance.get(left) ?? 0)
            : averageNumbers(leftChildren.map((childId) => analysis.firstAppearance.get(childId) ?? (analysis.firstAppearance.get(left) ?? 0)));
          const rightChildAnchor = rightChildren.length === 0
            ? (analysis.firstAppearance.get(right) ?? 0)
            : averageNumbers(rightChildren.map((childId) => analysis.firstAppearance.get(childId) ?? (analysis.firstAppearance.get(right) ?? 0)));
          if (leftChildAnchor !== rightChildAnchor) {
            return leftChildAnchor - rightChildAnchor;
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

  const renderGraphConnectionBadge = (tone, label) =>
    `<span class="service-graph-connection-badge ${tone}">${escapeGraphHtml(label)}</span>`;

  const buildGraphConnectionGroups = (connections, analysis) => {
    const groupsBySourceId = new Map();

    connections.forEach((connection, index) => {
      let group = groupsBySourceId.get(connection.fromId);
      if (group === undefined) {
        group = {
          fromId: connection.fromId,
          fromName: connection.fromName,
          firstIndex: index,
          targetsById: new Map()
        };
        groupsBySourceId.set(connection.fromId, group);
      }

      let target = group.targetsById.get(connection.toId);
      if (target === undefined) {
        target = {
          toId: connection.toId,
          toName: connection.toName,
          firstIndex: index,
          count: 0
        };
        group.targetsById.set(connection.toId, target);
      }

      target.count += 1;
    });

    return Array.from(groupsBySourceId.values())
      .sort((left, right) => {
        const levelDiff = (analysis.levels.get(left.fromId) ?? 0) - (analysis.levels.get(right.fromId) ?? 0);
        if (levelDiff !== 0) {
          return levelDiff;
        }

        const appearanceDiff = (analysis.firstAppearance.get(left.fromId) ?? left.firstIndex) -
          (analysis.firstAppearance.get(right.fromId) ?? right.firstIndex);
        if (appearanceDiff !== 0) {
          return appearanceDiff;
        }

        return left.firstIndex - right.firstIndex;
      })
      .map((group) => ({
        fromId: group.fromId,
        fromName: group.fromName,
        incomingCount: analysis.incoming.get(group.fromId)?.size ?? 0,
        outgoingCount: analysis.adjacency.get(group.fromId)?.size ?? 0,
        targets: Array.from(group.targetsById.values())
          .sort((left, right) => {
            const levelDiff = (analysis.levels.get(left.toId) ?? 0) - (analysis.levels.get(right.toId) ?? 0);
            if (levelDiff !== 0) {
              return levelDiff;
            }

            const appearanceDiff = (analysis.firstAppearance.get(left.toId) ?? left.firstIndex) -
              (analysis.firstAppearance.get(right.toId) ?? right.firstIndex);
            if (appearanceDiff !== 0) {
              return appearanceDiff;
            }

            return left.firstIndex - right.firstIndex;
          })
          .map((target) => ({
            ...target,
            incomingCount: analysis.incoming.get(target.toId)?.size ?? 0,
            outgoingCount: analysis.adjacency.get(target.toId)?.size ?? 0
          }))
      }));
  };

  const renderGraphConnectionsPanel = (connectionsPanel, connections, analysisInput = null) => {
    if (!(connectionsPanel instanceof HTMLElement)) {
      return;
    }

    const emptyTitle = connectionsPanel.dataset.emptyTitle ?? "No connections yet";
    const emptyBody = connectionsPanel.dataset.emptyBody ?? "Add connected products to build the connection details.";

    if (connections.length === 0) {
      connectionsPanel.innerHTML = `
        <div class="empty-state compact">
          <h3>${escapeGraphHtml(emptyTitle)}</h3>
          <p>${escapeGraphHtml(emptyBody)}</p>
        </div>`;
      return;
    }

    const analysis = analysisInput ?? analyzeGraph(connections);
    const groups = buildGraphConnectionGroups(connections, analysis);

    connectionsPanel.innerHTML = `
      <div class="service-graph-connections">
        ${groups.map((group) => {
          const sourceBadges = [];
          if (group.incomingCount === 0) {
            sourceBadges.push(renderGraphConnectionBadge("is-entry", "Entry point"));
          } else if (group.incomingCount > 1) {
            sourceBadges.push(renderGraphConnectionBadge("is-merge", `${group.incomingCount} incoming connections`));
          }

          if (group.outgoingCount > 1) {
            sourceBadges.push(renderGraphConnectionBadge("is-branch", `${group.outgoingCount} outgoing connections`));
          }

          return `
            <article class="service-graph-connection-card">
              <div class="service-graph-connection-header">
                <div>
                  <div class="service-graph-connection-kicker">From</div>
                  <h3 class="service-graph-connection-title">${escapeGraphHtml(group.fromName)}</h3>
                </div>
                <div class="service-graph-connection-meta">
                  ${sourceBadges.join("")}
                </div>
              </div>
              <div class="service-graph-connection-target-list">
                ${group.targets.map((target) => {
                  const targetBadges = [];
                  if (target.count > 1) {
                    targetBadges.push(renderGraphConnectionBadge("is-neutral", `${target.count} saved connections`));
                  }

                  if (target.incomingCount > 1) {
                    targetBadges.push(renderGraphConnectionBadge("is-merge", `${target.incomingCount} incoming connections`));
                  }

                  if (target.outgoingCount > 1) {
                    targetBadges.push(renderGraphConnectionBadge("is-branch", `${target.outgoingCount} outgoing connections`));
                  } else if (target.outgoingCount === 0) {
                    targetBadges.push(renderGraphConnectionBadge("is-terminal", "Endpoint"));
                  }

                  return `
                    <div class="service-graph-connection-target">
                      <div class="service-graph-connection-arrow" aria-hidden="true">&darr;</div>
                      <div class="service-graph-connection-target-body">
                        <div class="service-graph-connection-kicker">To</div>
                        <div class="service-graph-connection-target-name">${escapeGraphHtml(target.toName)}</div>
                      </div>
                      <div class="service-graph-connection-meta">
                        ${targetBadges.join("")}
                      </div>
                    </div>`;
                }).join("")}
              </div>
            </article>`;
        }).join("")}
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
    const connectionsPanel = graphHost?.querySelector("[data-service-graph-connections]");
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
      renderGraphConnectionsPanel(connectionsPanel, connections, analysis);
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
    const connectionsPanel = graphHost.querySelector("[data-service-graph-connections]");

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
      const sourceOrder = new Map(
        Array.from(new Set(
          completedStates
            .map((state) => state.fromId)
            .sort((left, right) => {
              const levelDiff = (analysis.levels.get(left) ?? 0) - (analysis.levels.get(right) ?? 0);
              if (levelDiff !== 0) {
                return levelDiff;
              }

              return (analysis.firstAppearance.get(left) ?? 0) - (analysis.firstAppearance.get(right) ?? 0);
            })))
          .map((sourceId, index) => [sourceId, index]));
      const metadataByRow = new Map();

      completedStates.forEach((state) => {
        const tone = graphBranchPalette[(sourceOrder.get(state.fromId) ?? 0) % graphBranchPalette.length];
        const badges = [];

        const incomingToSource = analysis.incoming.get(state.fromId)?.size ?? 0;
        const outgoingFromSource = analysis.adjacency.get(state.fromId)?.size ?? 0;
        const incomingToTarget = analysis.incoming.get(state.toId)?.size ?? 0;
        const outgoingFromTarget = analysis.adjacency.get(state.toId)?.size ?? 0;

        if (incomingToSource === 0) {
          badges.push(renderBadge("is-entry", `Entry at ${state.fromName}`));
        } else if (incomingToSource > 1) {
          badges.push(renderBadge("is-merge", `${incomingToSource} inputs into ${state.fromName}`));
        }

        if (outgoingFromSource > 1) {
          badges.push(renderBadge("is-split", `${outgoingFromSource} outputs from ${state.fromName}`));
        }

        if (incomingToTarget > 1) {
          badges.push(renderBadge("is-merge", `${incomingToTarget} inputs into ${state.toName}`));
        }

        if (outgoingFromTarget === 0) {
          badges.push(renderBadge("is-terminal", `Ends at ${state.toName}`));
        }

        metadataByRow.set(state.row, {
          sourceId: state.fromId,
          sourceLabel: state.fromName,
          tone,
          badges
        });
      });

      let previousSourceId = null;
      rowStates.forEach((state) => {
        state.row.style.removeProperty("--service-connection-accent");
        state.row.style.removeProperty("--service-connection-accent-soft");

        if (state.summary instanceof HTMLElement) {
          state.summary.innerHTML = "";
        }

        const metadata = metadataByRow.get(state.row);
        if (metadata === undefined) {
          if (state.summary instanceof HTMLElement) {
            state.summary.innerHTML = renderBadge("is-draft", "Complete both products to place this connection.");
          }

          return;
        }

        state.row.style.setProperty("--service-connection-accent", metadata.tone.border);
        state.row.style.setProperty("--service-connection-accent-soft", metadata.tone.surface);

        if (metadata.sourceId !== previousSourceId) {
          const divider = document.createElement("div");
          divider.className = "service-connection-branch-divider";
          divider.setAttribute("data-service-connection-divider", "");
          divider.innerHTML = `Connections from <strong>${escapeGraphHtml(metadata.sourceLabel)}</strong>`;
          rowsContainer.insertBefore(divider, state.row);
          previousSourceId = metadata.sourceId;
        }

        if (state.summary instanceof HTMLElement) {
          state.summary.innerHTML = metadata.badges.join("");
        }
      });
    };

    const organizeRowsByConnection = () => {
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
      renderGraphConnectionsPanel(connectionsPanel, connections, analysis);
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
        organizeRowsByConnection();
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

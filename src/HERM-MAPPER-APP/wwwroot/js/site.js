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

  const getElementBox = (element, referenceElement, scrollHost = referenceElement) => {
    if (!(element instanceof Element) || !(referenceElement instanceof Element)) {
      return null;
    }

    const referenceRect = referenceElement.getBoundingClientRect();
    const elementRect = element.getBoundingClientRect();
    const scrollLeft = scrollHost instanceof HTMLElement ? scrollHost.scrollLeft : 0;
    const scrollTop = scrollHost instanceof HTMLElement ? scrollHost.scrollTop : 0;
    const left = elementRect.left - referenceRect.left + scrollLeft;
    const top = elementRect.top - referenceRect.top + scrollTop;

    return {
      left,
      top,
      right: left + elementRect.width,
      bottom: top + elementRect.height,
      width: elementRect.width,
      height: elementRect.height,
      centerX: left + (elementRect.width / 2),
      centerY: top + (elementRect.height / 2)
    };
  };

  const getOppositeAnchorSide = (side) => {
    switch (side) {
      case "left":
        return "right";
      case "right":
        return "left";
      case "top":
        return "bottom";
      default:
        return "top";
    }
  };

  const getBoxAnchor = (box, side, gap = 0) => {
    if (box === null) {
      return null;
    }

    switch (side) {
      case "left":
        return { x: box.left - gap, y: box.centerY, side };
      case "right":
        return { x: box.right + gap, y: box.centerY, side };
      case "top":
        return { x: box.centerX, y: box.top - gap, side };
      default:
        return { x: box.centerX, y: box.bottom + gap, side: "bottom" };
    }
  };

  // Prefer side anchors when nodes are laid out more horizontally than vertically.
  const getPreferredAnchorSides = (fromPoint, toPoint) => {
    const deltaX = toPoint.x - fromPoint.x;
    const deltaY = toPoint.y - fromPoint.y;

    if (Math.abs(deltaX) > Math.abs(deltaY)) {
      return deltaX >= 0
        ? { fromSide: "right", toSide: "left" }
        : { fromSide: "left", toSide: "right" };
    }

    return deltaY >= 0
      ? { fromSide: "bottom", toSide: "top" }
      : { fromSide: "top", toSide: "bottom" };
  };

  const getAnchorPairBetweenBoxes = (fromBox, toBox, startGap = 0, endGap = 0) => {
    if (fromBox === null || toBox === null) {
      return null;
    }

    const sides = getPreferredAnchorSides(
      { x: fromBox.centerX, y: fromBox.centerY },
      { x: toBox.centerX, y: toBox.centerY });

    return {
      start: getBoxAnchor(fromBox, sides.fromSide, startGap),
      end: getBoxAnchor(toBox, sides.toSide, endGap)
    };
  };

  const getPreviewAnchorPair = (fromBox, targetPoint, startGap = 0) => {
    if (fromBox === null || targetPoint === null) {
      return null;
    }

    const sides = getPreferredAnchorSides(
      { x: fromBox.centerX, y: fromBox.centerY },
      targetPoint);

    return {
      start: getBoxAnchor(fromBox, sides.fromSide, startGap),
      end: {
        x: targetPoint.x,
        y: targetPoint.y,
        side: getOppositeAnchorSide(sides.fromSide)
      }
    };
  };

  const buildConnectorPath = (start, end) => {
    const isHorizontal =
      start?.side === "left" ||
      start?.side === "right" ||
      end?.side === "left" ||
      end?.side === "right";

    if (isHorizontal) {
      const midpointX = start.x + ((end.x - start.x) / 2);
      return `M ${start.x} ${start.y} C ${midpointX} ${start.y}, ${midpointX} ${end.y}, ${end.x} ${end.y}`;
    }

    const midpointY = start.y + ((end.y - start.y) / 2);
    return `M ${start.x} ${start.y} C ${start.x} ${midpointY}, ${end.x} ${midpointY}, ${end.x} ${end.y}`;
  };

  const drawGraphLines = (preview, connections) => {
    const canvas = preview.querySelector("[data-service-graph-canvas]");
    const svg = preview.querySelector(".service-graph-lines");

    if (!(canvas instanceof HTMLElement) || !(svg instanceof SVGSVGElement)) {
      return;
    }

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

        const fromBox = getElementBox(fromNode, canvas, canvas);
        const toBox = getElementBox(toNode, canvas, canvas);
        const anchors = getAnchorPairBetweenBoxes(fromBox, toBox);
        if (anchors === null || anchors.start === null || anchors.end === null) {
          return "";
        }

        return `<path class="service-graph-line" d="${buildConnectorPath(anchors.start, anchors.end)}" />`;
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

  const normalizeCanvasNode = (node) => {
    if (node === null || typeof node !== "object") {
      return null;
    }

    const read = (...names) => {
      for (const name of names) {
        if (Object.prototype.hasOwnProperty.call(node, name) &&
          node[name] !== null &&
          node[name] !== undefined &&
          node[name] !== "") {
          return node[name];
        }
      }

      return "";
    };

    const productId = String(read("productId", "ProductId"));
    if (productId === "" || productId === "0") {
      return null;
    }

    const x = Number(read("x", "X"));
    const y = Number(read("y", "Y"));

    return {
      productId,
      x: Number.isFinite(x) ? x : null,
      y: Number.isFinite(y) ? y : null
    };
  };

  const parseServiceCanvasState = (value) => {
    if (typeof value !== "string" || value.trim() === "") {
      return {
        nodes: [],
        connections: []
      };
    }

    try {
      const parsed = JSON.parse(value);
      return {
        nodes: Array.isArray(parsed?.nodes)
          ? parsed.nodes
            .map(normalizeCanvasNode)
            .filter((node) => node !== null)
          : [],
        connections: Array.isArray(parsed?.connections)
          ? parsed.connections
            .map(normalizeGraphConnection)
            .filter((connection) => connection !== null)
            .filter((connection) => connection.fromId !== "" && connection.toId !== "")
          : []
      };
    } catch (error) {
      console.error("Unable to parse service canvas state", error);
      return null;
    }
  };

  document.querySelectorAll("[data-service-canvas-editor]").forEach((editor) => {
    const form = editor.closest("form");
    const stateInput = editor.querySelector("[data-service-canvas-state-input]");
    const board = editor.querySelector("[data-service-canvas-board]");
    const surface = editor.querySelector("[data-service-canvas-surface]");
    const nodesHost = editor.querySelector("[data-service-canvas-nodes]");
    const lines = editor.querySelector("[data-service-canvas-lines]");
    const emptyState = editor.querySelector("[data-service-canvas-empty]");
    const summary = editor.querySelector("[data-service-canvas-summary]");
    const connectorBanner = editor.querySelector("[data-service-canvas-connector-banner]");
    const selectionBanner = editor.querySelector("[data-service-canvas-selection-banner]");
    const selectionCopy = editor.querySelector("[data-service-canvas-selection-copy]");
    const selectionRemoveButton = editor.querySelector("[data-service-canvas-selection-remove]");
    const autoLayoutButton = editor.querySelector("[data-service-canvas-autolayout]");
    const clearButton = editor.querySelector("[data-service-canvas-clear]");
    const paletteSearch = editor.querySelector("[data-service-canvas-palette-search]");
    const paletteEmpty = editor.querySelector("[data-service-canvas-palette-empty]");
    const paletteItems = Array.from(editor.querySelectorAll("[data-service-canvas-product]"))
      .filter((item) => item instanceof HTMLButtonElement);

    if (
      !(form instanceof HTMLFormElement) ||
      !(stateInput instanceof HTMLInputElement) ||
      !(board instanceof HTMLElement) ||
      !(surface instanceof HTMLElement) ||
      !(nodesHost instanceof HTMLElement) ||
      !(lines instanceof SVGSVGElement) ||
      !(emptyState instanceof HTMLElement) ||
      !(summary instanceof HTMLElement) ||
      !(connectorBanner instanceof HTMLElement) ||
      !(selectionBanner instanceof HTMLElement) ||
      !(selectionCopy instanceof HTMLElement) ||
      !(selectionRemoveButton instanceof HTMLButtonElement)
    ) {
      return;
    }

    const productsById = new Map();
    paletteItems.forEach((item) => {
      const productId = item.dataset.productId ?? "";
      if (productId !== "") {
        productsById.set(productId, item.dataset.productLabel ?? item.textContent?.trim() ?? productId);
      }
    });

    const state = {
      nodes: new Map(),
      connections: [],
      connectorSourceId: null,
      selectedConnectionKey: null,
      pointer: null,
      dragging: null,
      ignoreNextClickProductId: null
    };

    let renderQueued = false;
    const canvasNodeWidth = 220;
    const canvasNodeHeight = 124;
    const canvasPadding = 32;
    const connectorStartGap = 3;
    const connectorEndGap = 5;

    const getLabel = (productId) => productsById.get(productId) ?? `Product ${productId}`;

    const buildConnectionRecord = (fromId, toId, sequence = 0) => ({
      sequence,
      fromId,
      toId,
      fromName: getLabel(fromId),
      toName: getLabel(toId)
    });

    const buildConnectionKey = (fromId, toId) => `${fromId}->${toId}`;

    const getRenderableConnections = () =>
      state.connections.map((connection, index) =>
        buildConnectionRecord(connection.fromId, connection.toId, index + 1));

    const serializeState = () => {
      stateInput.value = JSON.stringify({
        nodes: Array.from(state.nodes.values())
          .sort((left, right) => Number(left.productId) - Number(right.productId))
          .map((node) => ({
            productId: Number(node.productId),
            x: Math.round(node.x ?? 0),
            y: Math.round(node.y ?? 0)
          })),
        connections: state.connections.map((connection) => ({
          fromProductId: Number(connection.fromId),
          toProductId: Number(connection.toId)
        }))
      });
    };

    const updatePaletteUsage = () => {
      paletteItems.forEach((item) => {
        item.classList.toggle("is-used", state.nodes.has(item.dataset.productId ?? ""));
      });
    };

    const filterPalette = () => {
      const query = paletteSearch instanceof HTMLInputElement
        ? paletteSearch.value.trim().toLocaleLowerCase()
        : "";
      let visibleCount = 0;

      paletteItems.forEach((item) => {
        const label = (item.dataset.productLabel ?? item.textContent ?? "").toLocaleLowerCase();
        const isVisible = query === "" || label.includes(query);
        item.hidden = !isVisible;
        if (isVisible) {
          visibleCount += 1;
        }
      });

      if (paletteEmpty instanceof HTMLElement) {
        paletteEmpty.hidden = visibleCount !== 0;
      }
    };

    const updateSummary = () => {
      const nodeCount = state.nodes.size;
      const connectionCount = state.connections.length;
      summary.textContent = `${nodeCount} product${nodeCount === 1 ? "" : "s"} on canvas. ${connectionCount} connection${connectionCount === 1 ? "" : "s"}.`;
    };

    const updateConnectorBanner = () => {
      if (state.connectorSourceId === null) {
        connectorBanner.hidden = true;
        connectorBanner.textContent = "";
        return;
      }

      connectorBanner.hidden = false;
      connectorBanner.textContent = `Connecting from ${getLabel(state.connectorSourceId)}. Click a target node to add or remove the connection. Click a line to delete it. Press Esc or click empty space to cancel.`;
    };

    const updateSelectionBanner = () => {
      if (state.selectedConnectionKey === null) {
        selectionBanner.hidden = true;
        selectionCopy.textContent = "";
        return;
      }

      const selectedConnection = state.connections.find((connection) =>
        buildConnectionKey(connection.fromId, connection.toId) === state.selectedConnectionKey);
      if (selectedConnection === undefined) {
        state.selectedConnectionKey = null;
        selectionBanner.hidden = true;
        selectionCopy.textContent = "";
        return;
      }

      selectionBanner.hidden = false;
      selectionCopy.textContent = `Selected connection: ${getLabel(selectedConnection.fromId)} to ${getLabel(selectedConnection.toId)}.`;
    };

    const updateEmptyState = () => {
      emptyState.hidden = state.nodes.size !== 0;
    };

    const ensureNode = (productId) => {
      if (!state.nodes.has(productId)) {
        state.nodes.set(productId, {
          productId,
          label: getLabel(productId),
          x: null,
          y: null
        });
      }

      return state.nodes.get(productId);
    };

    const setNodePosition = (productId, x, y) => {
      const node = ensureNode(productId);
      if (node === undefined) {
        return;
      }

      node.x = Math.max(canvasPadding, Math.round(Number.isFinite(x) ? x : canvasPadding));
      node.y = Math.max(canvasPadding, Math.round(Number.isFinite(y) ? y : canvasPadding));
    };

    const getDefaultNodePosition = () => {
      const index = state.nodes.size;
      return {
        x: 56 + ((index % 3) * 250),
        y: 56 + (Math.floor(index / 3) * 168)
      };
    };

    const getViewportPlacementPoint = () => ({
      x: board.scrollLeft + (board.clientWidth / 2) - (canvasNodeWidth / 2),
      y: board.scrollTop + (board.clientHeight / 2) - (canvasNodeHeight / 2)
    });

    const revealNode = (productId) => {
      requestAnimationFrame(() => {
        const nodeElement = nodesHost.querySelector(`[data-service-canvas-node][data-node-id="${productId}"]`);
        if (nodeElement instanceof HTMLElement) {
          nodeElement.scrollIntoView({
            behavior: "smooth",
            block: "center",
            inline: "center"
          });
        }
      });
    };

    const placeNode = (productId, point = null) => {
      const alreadyExists = state.nodes.has(productId);
      ensureNode(productId);
      if (point !== null || !alreadyExists) {
        const nextPoint = point ?? (state.nodes.size <= 1 ? getViewportPlacementPoint() : getDefaultNodePosition());
        setNodePosition(productId, nextPoint.x, nextPoint.y);
      }

      scheduleRender();
      revealNode(productId);
    };

    const removeNode = (productId) => {
      state.nodes.delete(productId);
      state.connections = state.connections.filter((connection) =>
        connection.fromId !== productId && connection.toId !== productId);

      if (state.connectorSourceId === productId) {
        state.connectorSourceId = null;
      }

      if (state.selectedConnectionKey !== null) {
        const selectedConnectionStillExists = state.connections.some((connection) =>
          buildConnectionKey(connection.fromId, connection.toId) === state.selectedConnectionKey);
        if (!selectedConnectionStillExists) {
          state.selectedConnectionKey = null;
        }
      }

      scheduleRender();
    };

    const clearCanvas = () => {
      state.nodes.clear();
      state.connections = [];
      state.connectorSourceId = null;
      state.selectedConnectionKey = null;
      state.pointer = null;
      state.dragging = null;
      state.ignoreNextClickProductId = null;
      scheduleRender();
    };

    const toggleConnection = (fromId, toId) => {
      if (fromId === toId) {
        return;
      }

      const existingIndex = state.connections.findIndex((connection) =>
        connection.fromId === fromId && connection.toId === toId);
      if (existingIndex >= 0) {
        state.connections.splice(existingIndex, 1);
        if (state.selectedConnectionKey === buildConnectionKey(fromId, toId)) {
          state.selectedConnectionKey = null;
        }
        return;
      }

      state.connections.push({ fromId, toId });
    };

    const applyGridLayout = (orderedProductIds) => {
      const productIds = orderedProductIds.length !== 0
        ? orderedProductIds.filter((productId) => state.nodes.has(productId))
        : Array.from(state.nodes.keys());
      const columns = Math.max(1, Math.ceil(Math.sqrt(productIds.length)));

      productIds.forEach((productId, index) => {
        setNodePosition(
          productId,
          56 + ((index % columns) * 250),
          56 + (Math.floor(index / columns) * 170));
      });
    };

    const applyAutoLayout = () => {
      const connections = getRenderableConnections();
      const nodeIds = Array.from(state.nodes.keys());

      if (nodeIds.length === 0) {
        scheduleRender();
        return;
      }

      if (connections.length === 0) {
        applyGridLayout(nodeIds.sort((left, right) => getLabel(left).localeCompare(getLabel(right))));
        scheduleRender();
        return;
      }

      const analysis = analyzeGraph(connections);
      const layout = buildGraphLayout(analysis);
      if (!layout.supportsGraphLayout) {
        applyGridLayout(analysis.orderedNodeIds);
        scheduleRender();
        return;
      }

      layout.rows.forEach((row, rowIndex) => {
        row.forEach((node, columnIndex) => {
          setNodePosition(node.id, 72 + (columnIndex * 250), 72 + (rowIndex * 180));
        });
      });

      scheduleRender();
    };

    const renderNodeBadges = (productId) => {
      const incomingCount = state.connections.filter((connection) => connection.toId === productId).length;
      const outgoingCount = state.connections.filter((connection) => connection.fromId === productId).length;
      const badges = [];

      if (incomingCount === 0 && state.connections.length !== 0) {
        badges.push("<span class=\"service-canvas-node-badge is-entry\">Entry</span>");
      } else if (incomingCount > 1) {
        badges.push(`<span class="service-canvas-node-badge is-merge">${incomingCount} in</span>`);
      }

      if (outgoingCount > 1) {
        badges.push(`<span class="service-canvas-node-badge is-branch">${outgoingCount} out</span>`);
      } else if (outgoingCount === 0 && state.connections.length !== 0) {
        badges.push("<span class=\"service-canvas-node-badge is-terminal\">End</span>");
      }

      return badges.length === 0
        ? "<div class=\"service-canvas-node-caption\">Drag to move. Use Connect to add links.</div>"
        : `<div class="service-canvas-node-badges">${badges.join("")}</div>`;
    };

    const renderNodes = () => {
      const connections = getRenderableConnections();
      const analysis = analyzeGraph(connections);
      const orderedNodes = Array.from(state.nodes.values())
        .sort((left, right) => {
          const leftY = left.y ?? Number.MAX_SAFE_INTEGER;
          const rightY = right.y ?? Number.MAX_SAFE_INTEGER;
          if (leftY !== rightY) {
            return leftY - rightY;
          }

          const leftX = left.x ?? Number.MAX_SAFE_INTEGER;
          const rightX = right.x ?? Number.MAX_SAFE_INTEGER;
          if (leftX !== rightX) {
            return leftX - rightX;
          }

          return getLabel(left.productId).localeCompare(getLabel(right.productId));
        });

      nodesHost.innerHTML = orderedNodes
        .map((node) => {
          const toneIndex = analysis.firstAppearance.get(node.productId) ?? orderedNodes.findIndex((candidate) => candidate.productId === node.productId);
          const tone = graphBranchPalette[toneIndex % graphBranchPalette.length];

          return `
            <article class="service-canvas-node${state.connectorSourceId === node.productId ? " is-connector-source" : ""}"
                     data-service-canvas-node
                     data-node-id="${escapeGraphHtml(node.productId)}"
                     style="left:${Math.round(node.x ?? 0)}px; top:${Math.round(node.y ?? 0)}px; --service-canvas-node-border:${tone.border}; --service-canvas-node-surface:${tone.surface};">
              <div class="service-canvas-node-head">
                <div class="service-canvas-node-title">${escapeGraphHtml(node.label)}</div>
                <div class="service-canvas-node-actions">
                  <button type="button"
                          class="service-canvas-node-action${state.connectorSourceId === node.productId ? " is-active" : ""}"
                          data-service-canvas-connect
                          aria-label="Connect ${escapeGraphHtml(node.label)}"
                          aria-pressed="${state.connectorSourceId === node.productId ? "true" : "false"}">${state.connectorSourceId === node.productId ? "Cancel" : "Connect"}</button>
                  <button type="button"
                          class="service-canvas-node-action is-danger"
                          data-service-canvas-remove
                          aria-label="Remove ${escapeGraphHtml(node.label)}">Remove</button>
                </div>
              </div>
              ${renderNodeBadges(node.productId)}
            </article>`;
        })
        .join("");
    };

    const refreshSurfaceSize = () => {
      const minimumWidth = Math.max(board.clientWidth - 2, 880);
      const minimumHeight = 520;
      let width = minimumWidth;
      let height = minimumHeight;

      Array.from(nodesHost.querySelectorAll("[data-service-canvas-node]")).forEach((element) => {
        if (!(element instanceof HTMLElement)) {
          return;
        }

        const left = Number.parseFloat(element.style.left) || 0;
        const top = Number.parseFloat(element.style.top) || 0;
        width = Math.max(width, left + element.offsetWidth + 96);
        height = Math.max(height, top + element.offsetHeight + 96);
      });

      surface.style.width = `${Math.round(width)}px`;
      surface.style.height = `${Math.round(height)}px`;
    };

    const getNodeBox = (productId) => {
      const nodeElement = nodesHost.querySelector(`[data-service-canvas-node][data-node-id="${productId}"]`);
      if (!(nodeElement instanceof HTMLElement)) {
        return null;
      }

      return getElementBox(nodeElement, surface, board);
    };

    const drawConnections = () => {
      const width = Math.max(surface.scrollWidth, surface.clientWidth);
      const height = Math.max(surface.scrollHeight, surface.clientHeight);

      lines.setAttribute("viewBox", `0 0 ${width} ${height}`);
      lines.setAttribute("width", String(width));
      lines.setAttribute("height", String(height));

      const renderedConnections = state.connections
        .map((connection) => {
          const fromNode = state.nodes.get(connection.fromId);
          const toNode = state.nodes.get(connection.toId);
          if (fromNode === undefined || toNode === undefined) {
            return "";
          }

          const fromBox = getNodeBox(connection.fromId);
          const toBox = getNodeBox(connection.toId);
          const anchors = getAnchorPairBetweenBoxes(fromBox, toBox, connectorStartGap, connectorEndGap);
          if (anchors === null || anchors.start === null || anchors.end === null) {
            return "";
          }

          const connectionKey = buildConnectionKey(connection.fromId, connection.toId);
          const isSelected = state.selectedConnectionKey === connectionKey;
          const path = buildConnectorPath(anchors.start, anchors.end);

          return `
            <path class="service-canvas-line-hit"
                  data-service-canvas-line
                  data-from-id="${escapeGraphHtml(connection.fromId)}"
                  data-to-id="${escapeGraphHtml(connection.toId)}"
                  d="${path}">
              <title>Select ${escapeGraphHtml(getLabel(connection.fromId))} to ${escapeGraphHtml(getLabel(connection.toId))}</title>
            </path>
            <path class="service-canvas-line${isSelected ? " is-selected" : ""}"
                  d="${path}" />`;
        })
        .join("");

      let previewPath = "";
      if (state.connectorSourceId !== null && state.pointer !== null) {
        const sourceNode = state.nodes.get(state.connectorSourceId);
        if (sourceNode !== undefined) {
          const sourceBox = getNodeBox(state.connectorSourceId);
          const previewAnchors = getPreviewAnchorPair(sourceBox, state.pointer, connectorStartGap);
          if (previewAnchors !== null && previewAnchors.start !== null && previewAnchors.end !== null) {
            previewPath = `<path class="service-canvas-line is-preview" d="${buildConnectorPath(previewAnchors.start, previewAnchors.end)}" />`;
          }
        }
      }

      lines.innerHTML = `
        <defs>
          <marker id="service-canvas-arrow" markerWidth="11" markerHeight="11" refX="9" refY="5.5" orient="auto" markerUnits="userSpaceOnUse">
            <path d="M 0 0 L 11 5.5 L 0 11 z" class="service-canvas-line-arrow"></path>
          </marker>
        </defs>
        ${renderedConnections}
        ${previewPath}`;
    };

    const render = () => {
      renderQueued = false;
      renderNodes();
      updatePaletteUsage();
      updateSummary();
      updateConnectorBanner();
      updateSelectionBanner();
      surface.classList.toggle("is-connector-active", state.connectorSourceId !== null);
      serializeState();

      requestAnimationFrame(() => {
        refreshSurfaceSize();
        drawConnections();
        updateEmptyState();
      });
    };

    const scheduleRender = () => {
      if (renderQueued) {
        return;
      }

      renderQueued = true;
      requestAnimationFrame(render);
    };

    const cancelConnector = () => {
      state.connectorSourceId = null;
      state.pointer = null;
      scheduleRender();
    };

    const clearSelectedConnection = () => {
      if (state.selectedConnectionKey === null) {
        return;
      }

      state.selectedConnectionKey = null;
      scheduleRender();
    };

    const toSurfacePoint = (clientX, clientY) => {
      const rect = surface.getBoundingClientRect();
      return {
        x: clientX - rect.left + board.scrollLeft,
        y: clientY - rect.top + board.scrollTop
      };
    };

    const initialState = parseServiceCanvasState(stateInput.value);
    if (initialState !== null) {
      initialState.nodes.forEach((node) => {
        const stateNode = ensureNode(node.productId);
        if (stateNode !== undefined) {
          stateNode.x = node.x;
          stateNode.y = node.y;
        }
      });

      initialState.connections.forEach((connection) => {
        ensureNode(connection.fromId);
        ensureNode(connection.toId);

        if (!state.connections.some((candidate) =>
          candidate.fromId === connection.fromId && candidate.toId === connection.toId)) {
          state.connections.push({
            fromId: connection.fromId,
            toId: connection.toId
          });
        }
      });
    }

    if (Array.from(state.nodes.values()).some((node) => node.x === null || node.y === null)) {
      applyAutoLayout();
    } else {
      scheduleRender();
    }

    paletteItems.forEach((item) => {
      item.addEventListener("dragstart", (event) => {
        const productId = item.dataset.productId ?? "";
        if (productId === "") {
          return;
        }

        event.dataTransfer?.setData("text/service-product-id", productId);
        if (event.dataTransfer !== null) {
          event.dataTransfer.effectAllowed = "copy";
        }
      });

      item.addEventListener("click", () => {
        const productId = item.dataset.productId ?? "";
        if (productId !== "") {
          placeNode(productId);
        }
      });
    });

    if (paletteSearch instanceof HTMLInputElement) {
      paletteSearch.addEventListener("input", () => {
        filterPalette();
      });
    }

    filterPalette();

    surface.addEventListener("dragover", (event) => {
      if (event.dataTransfer?.types.includes("text/service-product-id")) {
        event.preventDefault();
        surface.classList.add("is-drag-target");
      }
    });

    surface.addEventListener("dragleave", () => {
      surface.classList.remove("is-drag-target");
    });

    surface.addEventListener("drop", (event) => {
      event.preventDefault();
      surface.classList.remove("is-drag-target");

      const productId = event.dataTransfer?.getData("text/service-product-id") ?? "";
      if (productId === "") {
        return;
      }

      placeNode(productId, toSurfacePoint(event.clientX, event.clientY));
    });

    nodesHost.addEventListener("pointerdown", (event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement) || target.closest("button")) {
        return;
      }

      const nodeElement = target.closest("[data-service-canvas-node]");
      if (!(nodeElement instanceof HTMLElement)) {
        return;
      }

      const productId = nodeElement.dataset.nodeId ?? "";
      const node = state.nodes.get(productId);
      if (node === undefined) {
        return;
      }

      const point = toSurfacePoint(event.clientX, event.clientY);
      state.dragging = {
        productId,
        offsetX: point.x - (node.x ?? 0),
        offsetY: point.y - (node.y ?? 0),
        originX: point.x,
        originY: point.y,
        moved: false
      };
      nodeElement.setPointerCapture?.(event.pointerId);
      event.preventDefault();
    });

    surface.addEventListener("pointermove", (event) => {
      const point = toSurfacePoint(event.clientX, event.clientY);

      if (state.connectorSourceId !== null) {
        state.pointer = point;
      }

      if (state.dragging !== null) {
        const movedX = Math.abs(point.x - state.dragging.originX);
        const movedY = Math.abs(point.y - state.dragging.originY);
        if (movedX > 4 || movedY > 4) {
          state.dragging.moved = true;
        }

        setNodePosition(
          state.dragging.productId,
          point.x - state.dragging.offsetX,
          point.y - state.dragging.offsetY);
      }

      if (state.connectorSourceId !== null || state.dragging !== null) {
        scheduleRender();
      }
    });

    const finishDrag = () => {
      if (state.dragging !== null) {
        state.ignoreNextClickProductId = state.dragging.moved
          ? state.dragging.productId
          : null;
        state.dragging = null;
        scheduleRender();
      }
    };

    surface.addEventListener("pointerup", finishDrag);
    window.addEventListener("pointerup", finishDrag);

    surface.addEventListener("pointerleave", () => {
      if (state.connectorSourceId !== null) {
        state.pointer = null;
        scheduleRender();
      }
    });

    nodesHost.addEventListener("click", (event) => {
      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      const nodeElement = target.closest("[data-service-canvas-node]");
      if (!(nodeElement instanceof HTMLElement)) {
        return;
      }

      const productId = nodeElement.dataset.nodeId ?? "";
      if (productId === "") {
        return;
      }

      if (state.ignoreNextClickProductId === productId) {
        state.ignoreNextClickProductId = null;
        return;
      }

      if (target.closest("[data-service-canvas-remove]")) {
        removeNode(productId);
        return;
      }

      if (target.closest("[data-service-canvas-connect]")) {
        state.connectorSourceId = state.connectorSourceId === productId ? null : productId;
        state.pointer = null;
        scheduleRender();
        return;
      }

      if (state.connectorSourceId !== null && state.connectorSourceId !== productId) {
        const sourceId = state.connectorSourceId;
        toggleConnection(sourceId, productId);
        state.selectedConnectionKey = null;
        state.connectorSourceId = productId;
        state.pointer = null;
        scheduleRender();
      }
    });

    surface.addEventListener("click", (event) => {
      if (event.target === surface || event.target === emptyState) {
        cancelConnector();
        clearSelectedConnection();
      }
    });

    surface.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        cancelConnector();
      }
    });

    lines.addEventListener("click", (event) => {
      const target = event.target;
      if (!(target instanceof SVGPathElement)) {
        return;
      }

      const fromId = target.dataset.fromId ?? "";
      const toId = target.dataset.toId ?? "";
      if (fromId === "" || toId === "") {
        return;
      }

      const connectionKey = buildConnectionKey(fromId, toId);
      const existingConnection = state.connections.some((connection) =>
        buildConnectionKey(connection.fromId, connection.toId) === connectionKey);
      if (!existingConnection) {
        return;
      }

      state.connectorSourceId = null;
      state.pointer = null;
      state.selectedConnectionKey = state.selectedConnectionKey === connectionKey
        ? null
        : connectionKey;
      scheduleRender();
    });

    selectionRemoveButton.addEventListener("click", () => {
      if (state.selectedConnectionKey === null) {
        return;
      }

      const existingIndex = state.connections.findIndex((connection) =>
        buildConnectionKey(connection.fromId, connection.toId) === state.selectedConnectionKey);
      if (existingIndex < 0) {
        state.selectedConnectionKey = null;
        scheduleRender();
        return;
      }

      state.connections.splice(existingIndex, 1);
      state.selectedConnectionKey = null;
      scheduleRender();
    });

    board.addEventListener("scroll", () => {
      if (state.connectorSourceId !== null) {
        scheduleRender();
      }
    });

    if (autoLayoutButton instanceof HTMLButtonElement) {
      autoLayoutButton.addEventListener("click", () => {
        applyAutoLayout();
      });
    }

    if (clearButton instanceof HTMLButtonElement) {
      clearButton.addEventListener("click", () => {
        clearCanvas();
      });
    }

    form.addEventListener("submit", () => {
      serializeState();
    });

    window.addEventListener("resize", () => {
      scheduleRender();
    });
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

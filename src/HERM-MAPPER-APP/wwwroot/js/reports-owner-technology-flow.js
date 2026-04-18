(function () {
  const echartsApi = window.echarts;

  function compareText(left, right) {
    return left.localeCompare(right, undefined, { sensitivity: "base" });
  }

  function escapeHtml(value) {
    return String(value)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/\"/g, "&quot;")
      .replace(/'/g, "&#39;");
  }

  function normalizeString(value) {
    if (value === null || value === undefined) {
      return "";
    }

    return String(value).trim();
  }

  function appendEllipsis(prefix, remainder, maxLineLength) {
    const base = prefix === "" ? remainder : `${prefix} ${remainder}`;
    const trimmed = base.trim();
    if (trimmed.length <= maxLineLength) {
      return trimmed;
    }

    return `${trimmed.slice(0, Math.max(1, maxLineLength - 1)).trimEnd()}…`;
  }

  function wrapLabelText(text, maxLineLength, maxLines) {
    const normalized = normalizeString(text);
    if (normalized === "") {
      return "";
    }

    const words = normalized.split(/\s+/).filter((word) => word !== "");
    if (words.length === 0) {
      return normalized;
    }

    const lines = [];
    let currentLine = "";
    let wordIndex = 0;

    for (; wordIndex < words.length; wordIndex += 1) {
      const word = words[wordIndex];
      const candidate = currentLine === "" ? word : `${currentLine} ${word}`;
      if (candidate.length <= maxLineLength || currentLine === "") {
        currentLine = candidate;
        continue;
      }

      lines.push(currentLine);
      currentLine = word;

      if (lines.length === maxLines - 1) {
        break;
      }
    }

    if (lines.length < maxLines && currentLine !== "") {
      lines.push(currentLine);
      wordIndex += 1;
    }

    if (wordIndex < words.length) {
      const remainder = words.slice(wordIndex).join(" ");
      if (lines.length === maxLines) {
        lines[maxLines - 1] = appendEllipsis(lines[maxLines - 1], remainder, maxLineLength);
      } else {
        lines.push(appendEllipsis("", remainder, maxLineLength));
      }
    }

    return lines.join("\n");
  }

  function splitDisplayLabel(label) {
    const compact = normalizeString(label);
    if (compact === "") {
      return "";
    }

    const codedLabel = compact.match(/^([A-Z]{1,4}\d{3,4})\s+(.+)$/);
    if (codedLabel) {
      const wrappedName = wrapLabelText(codedLabel[2], 16, 2);
      return `${codedLabel[1]}\n${wrappedName}`;
    }

    return wrapLabelText(compact, 16, 3);
  }

  function measureTree(node, depth = 1) {
    const children = Array.isArray(node?.children) ? node.children : [];
    if (children.length === 0) {
      return {
        nodeCount: 1,
        leafCount: 1,
        depth
      };
    }

    return children.reduce((metrics, child) => {
      const childMetrics = measureTree(child, depth + 1);
      return {
        nodeCount: metrics.nodeCount + childMetrics.nodeCount,
        leafCount: metrics.leafCount + childMetrics.leafCount,
        depth: Math.max(metrics.depth, childMetrics.depth)
      };
    }, {
      nodeCount: 1,
      leafCount: 0,
      depth
    });
  }

  function computeChartHeight(host, metrics, shell) {
    const byLeaves = 280 + (metrics.leafCount * 72);
    const byNodes = 300 + (metrics.nodeCount * 26);
    const byDepth = 360 + (metrics.depth * 34);
    const contentHeight = Math.max(byLeaves, byNodes, byDepth);
    const fullscreenHeight = document.fullscreenElement === shell
      ? Math.max(window.innerHeight - 176, 560)
      : 0;

    return Math.max(560, Math.min(1600, Math.max(contentHeight, fullscreenHeight)));
  }

  function getNodeVisual(nodeType, isRoot) {
    if (isRoot) {
      return {
        fill: "#123b40",
        border: "#0f2f33",
        line: "rgba(18, 59, 64, 0.72)",
        symbolSize: 18
      };
    }

    switch (normalizeString(nodeType).toLowerCase()) {
      case "domain":
        return {
          fill: "#6b75c8",
          border: "#4d58a6",
          line: "rgba(107, 117, 200, 0.56)",
          symbolSize: 14
        };
      case "capability":
        return {
          fill: "#8f77df",
          border: "#6f58bc",
          line: "rgba(143, 119, 223, 0.5)",
          symbolSize: 12
        };
      case "component":
        return {
          fill: "#b38df2",
          border: "#8e68d1",
          line: "rgba(179, 141, 242, 0.48)",
          symbolSize: 10
        };
      default:
        return {
          fill: "#8aa0b2",
          border: "#6a7f91",
          line: "rgba(106, 127, 145, 0.5)",
          symbolSize: 10
        };
    }
  }

  function parsePayload(scriptElement) {
    try {
      const parsed = JSON.parse(scriptElement?.textContent ?? "[]");
      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed
        .filter((item) => item !== null && typeof item === "object")
        .map((item) => ({
          mappingId: Number(item.mappingId ?? item.MappingId ?? 0),
          ownerName: String(item.ownerName ?? item.OwnerName ?? "").trim(),
          domainId: String(item.domainId ?? item.DomainId ?? ""),
          domainLabel: String(item.domainLabel ?? item.DomainLabel ?? "").trim(),
          capabilityId: String(item.capabilityId ?? item.CapabilityId ?? ""),
          capabilityLabel: String(item.capabilityLabel ?? item.CapabilityLabel ?? "").trim(),
          componentId: String(item.componentId ?? item.ComponentId ?? ""),
          componentLabel: String(item.componentLabel ?? item.ComponentLabel ?? "").trim(),
          productId: Number(item.productId ?? item.ProductId ?? 0),
          productName: String(item.productName ?? item.ProductName ?? "").trim()
        }))
        .filter((item) => item.ownerName !== "" && item.domainId !== "" && item.capabilityId !== "" && item.componentId !== "");
    } catch (error) {
      console.error("Unable to parse owner technology flow payload", error);
      return [];
    }
  }

  function distinctOptions(paths, idKey, labelKey) {
    const groups = new Map();

    paths.forEach((path) => {
      const value = String(path[idKey] ?? "");
      const label = String(path[labelKey] ?? "");
      if (value === "" || label === "") {
        return;
      }

      if (!groups.has(value)) {
        groups.set(value, label);
      }
    });

    return Array.from(groups.entries())
      .map(([value, label]) => ({ value, label }))
      .sort((left, right) => compareText(left.label, right.label));
  }

  function populateSelect(select, options, placeholder, selectedValue) {
    const fragment = document.createDocumentFragment();
    const placeholderOption = document.createElement("option");
    placeholderOption.value = "";
    placeholderOption.textContent = placeholder;
    fragment.appendChild(placeholderOption);

    options.forEach((option) => {
      const element = document.createElement("option");
      element.value = option.value;
      element.textContent = option.label;
      fragment.appendChild(element);
    });

    select.replaceChildren(fragment);
    if (selectedValue !== "" && options.some((option) => option.value === selectedValue)) {
      select.value = selectedValue;
    }
  }

  function countDistinct(paths, keyName) {
    return new Set(paths.map((path) => String(path[keyName] ?? "")).filter((value) => value !== "")).size;
  }

  function createNode(name, nodeType, mappingCount, productCount, children, isRoot = false) {
    const visual = getNodeVisual(nodeType, isRoot);
    return {
      name: splitDisplayLabel(name),
      fullLabel: name,
      nodeType,
      mappingCount,
      productCount,
      value: mappingCount,
      children,
      symbolSize: visual.symbolSize,
      itemStyle: {
        color: visual.fill,
        borderColor: visual.border
      },
      lineStyle: {
        color: visual.line
      },
      label: isRoot
        ? {
          width: 152,
          overflow: "break",
          align: "center",
          fontWeight: 700
        }
        : undefined
    };
  }

  function buildComponentNodes(paths) {
    const groups = new Map();

    paths.forEach((path) => {
      const key = `${path.componentId}|${path.componentLabel}`;
      if (!groups.has(key)) {
        groups.set(key, []);
      }
      groups.get(key).push(path);
    });

    return Array.from(groups.entries())
      .map(([key, groupPaths]) => {
        const componentLabel = key.split("|")[1] ?? "Component";
        return createNode(
          componentLabel,
          "component",
          groupPaths.length,
          countDistinct(groupPaths, "productId"),
          []);
      })
      .sort((left, right) => compareText(left.name, right.name));
  }

  function buildCapabilityNodes(paths) {
    const groups = new Map();

    paths.forEach((path) => {
      const key = `${path.capabilityId}|${path.capabilityLabel}`;
      if (!groups.has(key)) {
        groups.set(key, []);
      }
      groups.get(key).push(path);
    });

    return Array.from(groups.entries())
      .map(([key, groupPaths]) => {
        const capabilityLabel = key.split("|")[1] ?? "Capability";
        return createNode(
          capabilityLabel,
          "capability",
          groupPaths.length,
          countDistinct(groupPaths, "productId"),
          buildComponentNodes(groupPaths));
      })
      .sort((left, right) => compareText(left.name, right.name));
  }

  function buildDomainNodes(paths) {
    const groups = new Map();

    paths.forEach((path) => {
      const key = `${path.domainId}|${path.domainLabel}`;
      if (!groups.has(key)) {
        groups.set(key, []);
      }
      groups.get(key).push(path);
    });

    return Array.from(groups.entries())
      .map(([key, groupPaths]) => {
        const domainLabel = key.split("|")[1] ?? "Domain";
        return createNode(
          domainLabel,
          "domain",
          groupPaths.length,
          countDistinct(groupPaths, "productId"),
          buildCapabilityNodes(groupPaths));
      })
      .sort((left, right) => compareText(left.name, right.name));
  }

  function buildTreeData(paths, ownerName) {
    return createNode(
      ownerName,
      "owner",
      paths.length,
      countDistinct(paths, "productId"),
      buildDomainNodes(paths),
      true);
  }

  function buildChartOption(root) {
    return {
      animationDuration: 450,
      animationDurationUpdate: 550,
      tooltip: {
        trigger: "item",
        triggerOn: "mousemove",
        confine: true,
        backgroundColor: "rgba(17, 24, 39, 0.92)",
        borderWidth: 0,
        textStyle: {
          color: "#f8fafc",
          fontFamily: "inherit"
        },
        formatter(params) {
          const node = params.data ?? {};
          const lines = [`<strong>${escapeHtml(String(node.fullLabel ?? params.name ?? ""))}</strong>`];
          if (node.nodeType) {
            lines.push(escapeHtml(String(node.nodeType)));
          }

          lines.push(`${node.mappingCount ?? 0} mapping path(s)`);
          lines.push(`${node.productCount ?? 0} product(s)`);
          return lines.join("<br>");
        }
      },
      series: [
        {
          type: "tree",
          data: [root],
          orient: "LR",
          top: "4%",
          left: "5%",
          bottom: "4%",
          right: "20%",
          symbol: "circle",
          symbolSize: 11,
          roam: true,
          expandAndCollapse: false,
          initialTreeDepth: -1,
          animationEasingUpdate: "cubicOut",
          edgeShape: "curve",
          label: {
            position: "top",
            verticalAlign: "bottom",
            align: "center",
            distance: 12,
            color: "#12212d",
            fontSize: 11,
            fontWeight: 600,
            width: 118,
            overflow: "truncate",
            lineHeight: 14,
            formatter(params) {
              const node = params?.data ?? {};
              return typeof node.name === "string" && node.name !== ""
                ? node.name
                : String(node.fullLabel ?? "");
            }
          },
          leaves: {
            label: {
              position: "top",
              verticalAlign: "bottom",
              align: "center",
              width: 118,
              fontSize: 11,
              lineHeight: 14,
              overflow: "truncate"
            }
          },
          lineStyle: {
            color: "rgba(103, 122, 139, 0.72)",
            width: 1.8,
            curveness: 0.22
          },
          itemStyle: {
            borderWidth: 2,
            shadowBlur: 10,
            shadowColor: "rgba(15, 23, 42, 0.12)"
          },
          emphasis: {
            focus: "descendant",
            lineStyle: {
              width: 2.4
            }
          }
        }
      ]
    };
  }

  function initializeReport(container) {
    if (typeof echartsApi?.init !== "function") {
      return;
    }

    const ownerSelect = container.querySelector("[data-owner-technology-filter='owner']");
    const domainSelect = container.querySelector("[data-owner-technology-filter='domain']");
    const capabilitySelect = container.querySelector("[data-owner-technology-filter='capability']");
    const componentSelect = container.querySelector("[data-owner-technology-filter='component']");
    const resetButton = container.querySelector("[data-owner-technology-reset]");
    const summary = container.querySelector("[data-owner-technology-summary]");
    const host = container.querySelector("[data-owner-technology-chart]");
    const payloadScript = container.querySelector("[data-owner-technology-payload]");
    const shell = container.querySelector("[data-owner-technology-flow-shell]");
    const fullscreenButton = container.querySelector("[data-owner-technology-flow-fullscreen]");
    const fullscreenButtonLabel = container.querySelector("[data-owner-technology-flow-fullscreen-label]");

    if (!(ownerSelect instanceof HTMLSelectElement)
      || !(domainSelect instanceof HTMLSelectElement)
      || !(capabilitySelect instanceof HTMLSelectElement)
      || !(componentSelect instanceof HTMLSelectElement)
      || !(resetButton instanceof HTMLButtonElement)
      || !(summary instanceof HTMLElement)
      || !(host instanceof HTMLElement)
      || !(payloadScript instanceof HTMLScriptElement)
      || !(shell instanceof HTMLElement)) {
      return;
    }

    const paths = parsePayload(payloadScript);
    if (paths.length === 0) {
      host.textContent = "No owner technology mappings are available.";
      summary.textContent = "No owner mapping paths are available.";
      return;
    }

    const chart = echartsApi.init(host, null, { renderer: "canvas" });
    let lastHeight = 0;
    const canToggleFullscreen = typeof shell.requestFullscreen === "function"
      && document.fullscreenEnabled !== false;

    function resizeChart(metrics) {
      const nextHeight = computeChartHeight(host, metrics, shell);
      if (nextHeight !== lastHeight) {
        host.style.height = `${nextHeight}px`;
        lastHeight = nextHeight;
      }

      chart.resize();
    }

    function updateFullscreenButtonState() {
      if (!(fullscreenButton instanceof HTMLButtonElement) || !(fullscreenButtonLabel instanceof HTMLElement)) {
        return;
      }

      const isFullscreen = document.fullscreenElement === shell;
      const nextLabel = isFullscreen ? "Exit full screen" : "Full screen";
      fullscreenButtonLabel.textContent = nextLabel;
      fullscreenButton.setAttribute("aria-label", isFullscreen ? "Exit full screen" : "Enter full screen");
      fullscreenButton.setAttribute("title", isFullscreen ? "Exit full screen" : "Enter full screen");
      fullscreenButton.setAttribute("aria-pressed", isFullscreen ? "true" : "false");
    }

    function rebuildFilterOptions() {
      const ownerName = ownerSelect.value;
      const ownerPaths = paths.filter((path) => path.ownerName === ownerName);

      const domainValue = domainSelect.value;
      const domainOptions = distinctOptions(ownerPaths, "domainId", "domainLabel");
      populateSelect(domainSelect, domainOptions, "All domains", domainValue);

      const scopedToDomain = domainSelect.value === ""
        ? ownerPaths
        : ownerPaths.filter((path) => path.domainId === domainSelect.value);

      const capabilityValue = capabilitySelect.value;
      const capabilityOptions = distinctOptions(scopedToDomain, "capabilityId", "capabilityLabel");
      populateSelect(capabilitySelect, capabilityOptions, "All capabilities", capabilityValue);

      const scopedToCapability = capabilitySelect.value === ""
        ? scopedToDomain
        : scopedToDomain.filter((path) => path.capabilityId === capabilitySelect.value);

      const componentValue = componentSelect.value;
      const componentOptions = distinctOptions(scopedToCapability, "componentId", "componentLabel");
      populateSelect(componentSelect, componentOptions, "All components", componentValue);
    }

    function filterPaths() {
      return paths.filter((path) => {
        if (path.ownerName !== ownerSelect.value) {
          return false;
        }

        if (domainSelect.value !== "" && path.domainId !== domainSelect.value) {
          return false;
        }

        if (capabilitySelect.value !== "" && path.capabilityId !== capabilitySelect.value) {
          return false;
        }

        if (componentSelect.value !== "" && path.componentId !== componentSelect.value) {
          return false;
        }

        return true;
      });
    }

    function render() {
      rebuildFilterOptions();
      const filteredPaths = filterPaths();

      if (filteredPaths.length === 0) {
        chart.clear();
        summary.textContent = `No technology paths match the current filters for ${ownerSelect.value}.`;
        resizeChart({ nodeCount: 1, leafCount: 1, depth: 1 });
        return;
      }

      const root = buildTreeData(filteredPaths, ownerSelect.value);
      summary.textContent = `${countDistinct(filteredPaths, "domainId")} domain(s), ${countDistinct(filteredPaths, "capabilityId")} capability(s), ${countDistinct(filteredPaths, "componentId")} component(s), and ${countDistinct(filteredPaths, "productId")} product(s) for ${ownerSelect.value}.`;
      chart.setOption(buildChartOption(root), true);
      resizeChart(measureTree(root));
    }

    if (ownerSelect.value === "" && ownerSelect.options.length > 0) {
      ownerSelect.selectedIndex = 0;
    }

    ownerSelect.addEventListener("change", () => {
      domainSelect.value = "";
      capabilitySelect.value = "";
      componentSelect.value = "";
      render();
    });
    domainSelect.addEventListener("change", () => {
      capabilitySelect.value = "";
      componentSelect.value = "";
      render();
    });
    capabilitySelect.addEventListener("change", () => {
      componentSelect.value = "";
      render();
    });
    componentSelect.addEventListener("change", render);
    resetButton.addEventListener("click", () => {
      domainSelect.value = "";
      capabilitySelect.value = "";
      componentSelect.value = "";
      render();
    });

    render();
    updateFullscreenButtonState();

    if (typeof ResizeObserver === "function") {
      const resizeObserver = new ResizeObserver(() => {
        const filteredPaths = filterPaths();
        const metrics = filteredPaths.length === 0
          ? { nodeCount: 1, leafCount: 1, depth: 1 }
          : measureTree(buildTreeData(filteredPaths, ownerSelect.value));
        resizeChart(metrics);
      });
      resizeObserver.observe(host.parentElement ?? host);
    } else {
      window.addEventListener("resize", () => {
        const filteredPaths = filterPaths();
        const metrics = filteredPaths.length === 0
          ? { nodeCount: 1, leafCount: 1, depth: 1 }
          : measureTree(buildTreeData(filteredPaths, ownerSelect.value));
        resizeChart(metrics);
      });
    }

    document.addEventListener("fullscreenchange", () => {
      updateFullscreenButtonState();
      const filteredPaths = filterPaths();
      const metrics = filteredPaths.length === 0
        ? { nodeCount: 1, leafCount: 1, depth: 1 }
        : measureTree(buildTreeData(filteredPaths, ownerSelect.value));
      resizeChart(metrics);
    });

    if (fullscreenButton instanceof HTMLButtonElement) {
      if (!canToggleFullscreen) {
        fullscreenButton.hidden = true;
      } else {
        fullscreenButton.addEventListener("click", async () => {
          try {
            if (document.fullscreenElement === shell) {
              await document.exitFullscreen();
            } else {
              await shell.requestFullscreen();
            }
          } catch (error) {
            console.error("Unable to toggle owner technology flow full screen mode", error);
          }
        });
      }
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-owner-technology-flow-report]").forEach((container) => {
      if (container instanceof HTMLElement) {
        initializeReport(container);
      }
    });
  });
}());
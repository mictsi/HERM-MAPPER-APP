"use strict";

document.addEventListener("DOMContentLoaded", () => {
  const chartHosts = document.querySelectorAll("[data-application-radial-tree]");
  if (chartHosts.length === 0) {
    return;
  }

  const echartsApi = window.echarts;
  chartHosts.forEach((host) => {
    if (!(host instanceof HTMLElement)) {
      return;
    }

    const dataScript = host.parentElement?.querySelector("[data-application-radial-tree-data]");
    const emptyTitle = host.dataset.emptyTitle ?? "No dependency map yet";
    const emptyBody = host.dataset.emptyBody ?? "Add application mappings first, then complete the TRM product mappings to see the dependency graph.";
    const includeProducts = host.dataset.includeProducts === "true";

    const showEmptyState = (title, body) => {
      host.innerHTML = `
        <div class="empty-state compact">
          <h3>${escapeHtml(title)}</h3>
          <p>${escapeHtml(body)}</p>
        </div>`;
    };

    if (!(dataScript instanceof HTMLScriptElement)) {
      showEmptyState(emptyTitle, emptyBody);
      return;
    }

    if (typeof echartsApi?.init !== "function") {
      showEmptyState("Visualization unavailable", "The local chart library could not be loaded.");
      return;
    }

    let parsedTree;
    try {
      parsedTree = JSON.parse(dataScript.textContent ?? "null");
    } catch (error) {
      console.error("Unable to parse application hierarchy data", error);
      showEmptyState("Visualization unavailable", "The application hierarchy data could not be read.");
      return;
    }

    const treeRoot = buildRadialTreeNode(parsedTree, includeProducts, true);
    if (treeRoot === null || !Array.isArray(treeRoot.children) || treeRoot.children.length === 0) {
      showEmptyState(emptyTitle, emptyBody);
      return;
    }

    host.innerHTML = "";
    const chart = echartsApi.init(host, null, { renderer: "canvas" });
    const metrics = measureTree(treeRoot);
    let lastHeight = 0;
    const resizeChart = () => {
      const nextHeight = computeChartHeight(host, metrics);
      if (nextHeight !== lastHeight) {
        host.style.height = `${nextHeight}px`;
        lastHeight = nextHeight;
      }

      chart.resize();
    };

    const chartOption = {
      animationDuration: 450,
      animationDurationUpdate: 550,
      tooltip: {
        trigger: "item",
        confine: true,
        backgroundColor: "rgba(17, 24, 39, 0.92)",
        borderWidth: 0,
        textStyle: {
          color: "#f8fafc",
          fontFamily: "inherit"
        },
        formatter: (params) => {
          const node = params?.data ?? {};
          const lines = [`<strong>${escapeHtml(String(node.fullLabel ?? node.name ?? ""))}</strong>`];
          if (node.nodeType) {
            lines.push(escapeHtml(String(node.nodeType)));
          }

          return lines.join("<br>");
        }
      },
      series: [
        {
          type: "tree",
          data: [treeRoot],
          orient: "LR",
          top: "4%",
          left: "5%",
          bottom: "4%",
          right: "20%",
          symbol: "circle",
          symbolSize: 11,
          expandAndCollapse: false,
          initialTreeDepth: -1,
          roam: true,
          animationEasingUpdate: "cubicOut",
          edgeShape: "curve",
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
          },
          label: {
            position: "top",
            verticalAlign: "bottom",
            align: "center",
            distance: 12,
            fontSize: 11,
            fontWeight: 600,
            color: "#213547",
            width: 118,
            overflow: "truncate",
            lineHeight: 14,
            formatter: (params) => formatNodeLabel(params?.data)
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
          }
        }
      ]
    };

    chart.setOption(chartOption);
    resizeChart();

    const resizeObserver = typeof ResizeObserver === "function"
      ? new ResizeObserver(() => resizeChart())
      : null;

    resizeObserver?.observe(host.parentElement ?? host);

    window.addEventListener("resize", resizeChart);

    host._applicationRadialTreeChart = chart;
    host._applicationRadialTreeResize = resizeChart;
    host._applicationRadialTreeResizeObserver = resizeObserver;
  });
});

const buildRadialTreeNode = (node, includeProducts, isRoot = false) => {
  if (node === null || typeof node !== "object") {
    return null;
  }

  const cssType = normalizeString(readNodeProperty(node, "cssType", "CssType"));
  if (cssType === "product" && !includeProducts) {
    return null;
  }

  const fullLabel = normalizeString(readNodeProperty(node, "label", "Label"));
  if (fullLabel === "") {
    return null;
  }

  const childNodes = readNodeProperty(node, "children", "Children");
  const children = Array.isArray(childNodes)
    ? childNodes
      .map((child) => buildRadialTreeNode(child, includeProducts, false))
      .filter((child) => child !== null)
    : [];

  const visual = getNodeVisual(cssType, normalizeString(readNodeProperty(node, "nodeType", "NodeType")), isRoot);

  return {
    id: normalizeString(readNodeProperty(node, "key", "Key")) || fullLabel,
    name: splitDisplayLabel(fullLabel),
    fullLabel,
    nodeType: normalizeString(readNodeProperty(node, "nodeType", "NodeType")),
    cssType,
    collapsed: false,
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
      : undefined,
    children
  };
};

const readNodeProperty = (node, ...propertyNames) => {
  for (const propertyName of propertyNames) {
    if (Object.prototype.hasOwnProperty.call(node, propertyName)) {
      return node[propertyName];
    }
  }

  return null;
};

const normalizeString = (value) => {
  if (value === null || value === undefined) {
    return "";
  }

  return String(value).trim();
};

const splitDisplayLabel = (label) => {
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
};

const formatNodeLabel = (node) => {
  const value = typeof node?.name === "string" && node.name !== ""
    ? node.name
    : String(node?.fullLabel ?? "");

  return value;
};

const wrapLabelText = (text, maxLineLength, maxLines) => {
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
};

const appendEllipsis = (prefix, remainder, maxLineLength) => {
  const base = prefix === "" ? remainder : `${prefix} ${remainder}`;
  const trimmed = base.trim();
  if (trimmed.length <= maxLineLength) {
    return trimmed;
  }

  return `${trimmed.slice(0, Math.max(1, maxLineLength - 1)).trimEnd()}…`;
};

const measureTree = (node, depth = 1) => {
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
};

const computeChartHeight = (host, metrics) => {
  const byLeaves = 280 + (metrics.leafCount * 72);
  const byNodes = 300 + (metrics.nodeCount * 26);
  const byDepth = 360 + (metrics.depth * 34);
  return Math.max(560, Math.min(1600, Math.max(byLeaves, byNodes, byDepth)));
};

const getNodeVisual = (cssType, nodeType, isRoot) => {
  if (isRoot) {
    return {
      fill: "#123b40",
      border: "#0f2f33",
      line: "rgba(18, 59, 64, 0.72)",
      symbolSize: 18
    };
  }

  switch (cssType) {
    case "arm-domain":
      return {
        fill: "#1e7c78",
        border: "#145d59",
        line: "rgba(30, 124, 120, 0.6)",
        symbolSize: 14
      };
    case "brm-domain":
      return {
        fill: "#145f7c",
        border: "#0d4458",
        line: "rgba(20, 95, 124, 0.58)",
        symbolSize: 14
      };
    case "brm-capability":
      return {
        fill: "#2f83a3",
        border: "#1f6380",
        line: "rgba(47, 131, 163, 0.54)",
        symbolSize: 12
      };
    case "brm-component":
      return {
        fill: "#63a9c0",
        border: "#41839a",
        line: "rgba(99, 169, 192, 0.5)",
        symbolSize: 10
      };
    case "arm-capability":
      return {
        fill: "#37a8a6",
        border: "#21807d",
        line: "rgba(55, 168, 166, 0.56)",
        symbolSize: 12
      };
    case "arm-component":
      return {
        fill: "#74cbc4",
        border: "#3d9992",
        line: "rgba(83, 170, 165, 0.48)",
        symbolSize: 10
      };
    case "trm-domain":
      return {
        fill: "#6b75c8",
        border: "#4d58a6",
        line: "rgba(107, 117, 200, 0.56)",
        symbolSize: 14
      };
    case "trm-capability":
      return {
        fill: "#8f77df",
        border: "#6f58bc",
        line: "rgba(143, 119, 223, 0.5)",
        symbolSize: 12
      };
    case "trm-component":
      return {
        fill: "#b38df2",
        border: "#8e68d1",
        line: "rgba(179, 141, 242, 0.48)",
        symbolSize: 10
      };
    case "product":
      return {
        fill: "#e7a35f",
        border: "#c88031",
        line: "rgba(231, 163, 95, 0.5)",
        symbolSize: 10
      };
    default:
      return {
        fill: "#8aa0b2",
        border: "#6a7f91",
        line: "rgba(106, 127, 145, 0.5)",
        symbolSize: normalizeString(nodeType).toLowerCase() === "application" ? 18 : 10
      };
  }
};

const escapeHtml = (value) => String(value)
  .replace(/&/g, "&amp;")
  .replace(/</g, "&lt;")
  .replace(/>/g, "&gt;")
  .replace(/\"/g, "&quot;")
  .replace(/'/g, "&#39;");

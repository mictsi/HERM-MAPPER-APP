(function () {
  const svgNs = "http://www.w3.org/2000/svg";
  const colors = {
    owner: "#0b6e4f",
    domain: "#2b6f77",
    capability: "#5c7c35",
    component: "#8b5e34",
    product: "#7b3f61"
  };
  const filterConfig = [
    { name: "owner", valueKey: "ownerName", labelKey: "ownerName" },
    { name: "domain", valueKey: "domainId", labelKey: "domainLabel" },
    { name: "capability", valueKey: "capabilityId", labelKey: "capabilityLabel" },
    { name: "component", valueKey: "componentId", labelKey: "componentLabel" }
  ];

  function createSvgElement(name, attributes) {
    const element = document.createElementNS(svgNs, name);
    Object.entries(attributes || {}).forEach(([key, value]) => {
      element.setAttribute(key, String(value));
    });
    return element;
  }

  function normalizePaths(payload) {
    const rawPaths = payload?.paths || payload?.Paths || [];
    return Array.isArray(rawPaths)
      ? rawPaths.map((path) => ({
          mappingId: path.mappingId ?? path.MappingId,
          ownerName: path.ownerName ?? path.OwnerName,
          domainId: String(path.domainId ?? path.DomainId),
          domainLabel: path.domainLabel ?? path.DomainLabel,
          capabilityId: String(path.capabilityId ?? path.CapabilityId),
          capabilityLabel: path.capabilityLabel ?? path.CapabilityLabel,
          componentId: String(path.componentId ?? path.ComponentId),
          componentLabel: path.componentLabel ?? path.ComponentLabel,
          productId: String(path.productId ?? path.ProductId),
          productName: path.productName ?? path.ProductName
        }))
      : [];
  }

  function filterPaths(paths, filters) {
    return paths.filter((path) =>
      (!filters.owner || path.ownerName === filters.owner) &&
      (!filters.domain || path.domainId === filters.domain) &&
      (!filters.capability || path.capabilityId === filters.capability) &&
      (!filters.component || path.componentId === filters.component)
    );
  }

  function buildOptions(paths, config) {
    const values = new Map();
    paths.forEach((path) => {
      const optionValue = String(path[config.valueKey]);
      if (!values.has(optionValue)) {
        values.set(optionValue, path[config.labelKey]);
      }
    });

    return Array.from(values.entries())
      .map(([value, label]) => ({ value, label }))
      .sort((a, b) => a.label.localeCompare(b.label));
  }

  function populateSelect(select, options, selectedValue, placeholder) {
    const fragment = document.createDocumentFragment();
    const defaultOption = document.createElement("option");
    defaultOption.value = "";
    defaultOption.textContent = placeholder;
    fragment.appendChild(defaultOption);

    options.forEach((option) => {
      const element = document.createElement("option");
      element.value = option.value;
      element.textContent = option.label;
      if (option.value === selectedValue) {
        element.selected = true;
      }
      fragment.appendChild(element);
    });

    select.replaceChildren(fragment);
  }

  function rebuildFilterOptions(paths, elements, filters) {
    const ownerOptions = buildOptions(paths, filterConfig[0]);
    populateSelect(elements.owner, ownerOptions, filters.owner, "All owners");
    if (filters.owner && !ownerOptions.some((option) => option.value === filters.owner)) {
      filters.owner = "";
    }

    const ownerFilteredPaths = filterPaths(paths, {
      owner: filters.owner,
      domain: "",
      capability: "",
      component: ""
    });
    const domainOptions = buildOptions(ownerFilteredPaths, filterConfig[1]);
    populateSelect(elements.domain, domainOptions, filters.domain, "All domains");
    if (filters.domain && !domainOptions.some((option) => option.value === filters.domain)) {
      filters.domain = "";
    }

    const domainFilteredPaths = filterPaths(paths, {
      owner: filters.owner,
      domain: filters.domain,
      capability: "",
      component: ""
    });
    const capabilityOptions = buildOptions(domainFilteredPaths, filterConfig[2]);
    populateSelect(elements.capability, capabilityOptions, filters.capability, "All capabilities");
    if (filters.capability && !capabilityOptions.some((option) => option.value === filters.capability)) {
      filters.capability = "";
    }

    const capabilityFilteredPaths = filterPaths(paths, {
      owner: filters.owner,
      domain: filters.domain,
      capability: filters.capability,
      component: ""
    });
    const componentOptions = buildOptions(capabilityFilteredPaths, filterConfig[3]);
    populateSelect(elements.component, componentOptions, filters.component, "All components");
    if (filters.component && !componentOptions.some((option) => option.value === filters.component)) {
      filters.component = "";
    }

    elements.owner.value = filters.owner;
    elements.domain.value = filters.domain;
    elements.capability.value = filters.capability;
    elements.component.value = filters.component;
  }

  function buildSankeyData(paths) {
    const nodes = [];
    const links = [];

    function addNodes(type, depth, valueKey, labelKey) {
      const grouped = new Map();
      paths.forEach((path) => {
        const id = `${type}:${path[valueKey]}`;
        if (!grouped.has(id)) {
          grouped.set(id, {
            id,
            nodeType: type,
            label: path[labelKey],
            depth,
            value: 0
          });
        }
        grouped.get(id).value += 1;
      });

      nodes.push(...Array.from(grouped.values()).sort((a, b) => (b.value - a.value) || a.label.localeCompare(b.label)));
    }

    function addLinks(type, sourceKey, targetKey) {
      const grouped = new Map();
      paths.forEach((path) => {
        const sourceId = sourceKey(path);
        const targetId = targetKey(path);
        const linkKey = `${sourceId}=>${targetId}`;
        if (!grouped.has(linkKey)) {
          grouped.set(linkKey, {
            sourceId,
            targetId,
            linkType: type,
            value: 0
          });
        }
        grouped.get(linkKey).value += 1;
      });

      links.push(...Array.from(grouped.values()).sort((a, b) => (b.value - a.value) || a.sourceId.localeCompare(b.sourceId)));
    }

    addNodes("owner", 0, "ownerName", "ownerName");
    addNodes("domain", 1, "domainId", "domainLabel");
    addNodes("capability", 2, "capabilityId", "capabilityLabel");
    addNodes("component", 3, "componentId", "componentLabel");
    addNodes("product", 4, "productId", "productName");

    addLinks("owner-domain", (path) => `owner:${path.ownerName}`, (path) => `domain:${path.domainId}`);
    addLinks("domain-capability", (path) => `domain:${path.domainId}`, (path) => `capability:${path.capabilityId}`);
    addLinks("capability-component", (path) => `capability:${path.capabilityId}`, (path) => `component:${path.componentId}`);
    addLinks("component-product", (path) => `component:${path.componentId}`, (path) => `product:${path.productId}`);

    return { nodes, links };
  }

  function updateSummary(element, filters, filteredPaths) {
    const labels = [];
    if (filters.owner) {
      labels.push(`owner selected`);
    }
    if (filters.domain) {
      labels.push(`domain selected`);
    }
    if (filters.capability) {
      labels.push(`capability selected`);
    }
    if (filters.component) {
      labels.push(`component selected`);
    }

    const productCount = new Set(filteredPaths.map((path) => path.productId)).size;
    const suffix = labels.length === 0 ? "Showing the full flow." : `Filtered subtree with ${labels.join(", ")}.`;
    element.textContent = `${filteredPaths.length} mapping path(s), ${productCount} product(s). ${suffix}`;
  }

  function buildSankey(container, sankeyData, onNodeSelect) {
    if (!sankeyData || !Array.isArray(sankeyData.nodes) || !Array.isArray(sankeyData.links) || sankeyData.nodes.length === 0) {
      container.textContent = "No sankey data available.";
      return;
    }

    const nodeWidth = 18;
    const minNodeHeight = 12;
    const nodeGap = 14;
    const margin = { top: 32, right: 220, bottom: 24, left: 24 };
    const columnCount = 5;
    const containerWidth = Math.max(container.clientWidth || 0, 1360);
    const width = Math.max(1360, containerWidth);

    const nodes = sankeyData.nodes.map((node) => ({
      ...node,
      inLinks: [],
      outLinks: []
    }));
    const nodeById = new Map(nodes.map((node) => [node.id, node]));
    const links = sankeyData.links
      .map((link) => ({
        ...link,
        source: nodeById.get(link.sourceId),
        target: nodeById.get(link.targetId)
      }))
      .filter((link) => link.source && link.target && Number.isFinite(link.value));

    links.forEach((link) => {
      link.source.outLinks.push(link);
      link.target.inLinks.push(link);
    });

    const columns = Array.from({ length: columnCount }, (_, depth) =>
      nodes
        .filter((node) => node.depth === depth)
        .sort((a, b) => (b.value - a.value) || a.label.localeCompare(b.label))
    );

    const maxNodesInColumn = Math.max(...columns.map((column) => column.length), 1);
    const totalValue = Math.max(...columns.map((column) => column.reduce((sum, node) => sum + node.value, 0)), 1);

    function computeHeight() {
      let height = Math.max(760, maxNodesInColumn * 34);

      for (let iteration = 0; iteration < 5; iteration += 1) {
        const availableHeight = height - margin.top - margin.bottom;
        const rawScale = Math.max((availableHeight - Math.max(0, maxNodesInColumn - 1) * nodeGap) / totalValue, 1);
        const requiredHeights = columns.map((column) =>
          column.reduce((sum, node) => sum + Math.max(minNodeHeight, node.value * rawScale), 0) +
          Math.max(0, column.length - 1) * nodeGap
        );
        const required = Math.max(...requiredHeights, 0);

        if (required <= availableHeight) {
          return { height, scale: rawScale };
        }

        height = required + margin.top + margin.bottom + 20;
      }

      return {
        height: Math.max(height, 760),
        scale: 1
      };
    }

    const { height, scale } = computeHeight();
    const trackWidth = width - margin.left - margin.right - nodeWidth;
    const columnSpacing = columnCount === 1 ? 0 : trackWidth / (columnCount - 1);

    columns.forEach((column, depth) => {
      let y = margin.top;
      column.forEach((node) => {
        node.x = margin.left + depth * columnSpacing;
        node.height = Math.max(minNodeHeight, node.value * scale);
        node.y = y;
        y += node.height + nodeGap;
      });
    });

    nodes.forEach((node) => {
      node.outLinks.sort((a, b) => a.target.y - b.target.y || a.target.label.localeCompare(b.target.label));
      node.inLinks.sort((a, b) => a.source.y - b.source.y || a.source.label.localeCompare(b.source.label));

      let sourceOffset = 0;
      node.outLinks.forEach((link) => {
        link.thickness = Math.max(1.5, link.value * scale);
        link.sourceOffset = sourceOffset;
        sourceOffset += link.thickness;
      });

      let targetOffset = 0;
      node.inLinks.forEach((link) => {
        link.thickness = Math.max(1.5, link.value * scale);
        link.targetOffset = targetOffset;
        targetOffset += link.thickness;
      });
    });

    const svg = createSvgElement("svg", {
      viewBox: `0 0 ${width} ${height}`,
      class: "owner-sankey-svg",
      role: "img",
      "aria-label": "Owner hierarchy sankey"
    });

    const defs = createSvgElement("defs");
    const linksGroup = createSvgElement("g", { class: "owner-sankey-links" });
    const nodesGroup = createSvgElement("g", { class: "owner-sankey-nodes" });
    svg.appendChild(defs);

    links.forEach((link, index) => {
      const x0 = link.source.x + nodeWidth;
      const x1 = link.target.x;
      const y0 = link.source.y + link.sourceOffset + (link.thickness / 2);
      const y1 = link.target.y + link.targetOffset + (link.thickness / 2);
      const curvature = Math.max((x1 - x0) * 0.48, 30);
      const gradientId = `owner-sankey-gradient-${index}`;
      const gradient = createSvgElement("linearGradient", {
        id: gradientId,
        x1: x0,
        x2: x1,
        y1: y0,
        y2: y1,
        gradientUnits: "userSpaceOnUse"
      });

      gradient.appendChild(createSvgElement("stop", {
        offset: "0%",
        "stop-color": colors[link.source.nodeType] || colors.owner,
        "stop-opacity": "0.45"
      }));
      gradient.appendChild(createSvgElement("stop", {
        offset: "100%",
        "stop-color": colors[link.target.nodeType] || colors.product,
        "stop-opacity": "0.45"
      }));
      defs.appendChild(gradient);

      const path = createSvgElement("path", {
        d: `M ${x0} ${y0} C ${x0 + curvature} ${y0}, ${x1 - curvature} ${y1}, ${x1} ${y1}`,
        fill: "none",
        stroke: `url(#${gradientId})`,
        "stroke-width": link.thickness,
        "stroke-linecap": "round",
        class: "owner-sankey-link"
      });
      path.appendChild(document.createElementNS(svgNs, "title")).textContent =
        `${link.source.label} -> ${link.target.label}: ${link.value} mapping path(s)`;
      linksGroup.appendChild(path);
    });

    columns.forEach((column) => {
      column.forEach((node) => {
        const group = createSvgElement("g", {
          class: `owner-sankey-node owner-sankey-node-${node.nodeType}`
        });
        const rect = createSvgElement("rect", {
          x: node.x,
          y: node.y,
          width: nodeWidth,
          height: node.height,
          rx: 6,
          ry: 6,
          fill: colors[node.nodeType] || colors.owner,
          class: node.nodeType === "product" ? "owner-sankey-rect" : "owner-sankey-rect owner-sankey-rect-clickable"
        });

        if (node.nodeType !== "product") {
          rect.style.cursor = "pointer";
          rect.addEventListener("click", () => onNodeSelect(node));
        }

        rect.appendChild(document.createElementNS(svgNs, "title")).textContent =
          `${node.label}: ${node.value} mapping path(s)`;
        group.appendChild(rect);

        const label = createSvgElement("text", {
          x: node.depth === columnCount - 1 ? node.x - 10 : node.x + nodeWidth + 10,
          y: node.y + Math.max(node.height / 2, 12),
          "text-anchor": node.depth === columnCount - 1 ? "end" : "start",
          class: "owner-sankey-label"
        });
        label.textContent = node.label;
        group.appendChild(label);

        const value = createSvgElement("text", {
          x: node.depth === columnCount - 1 ? node.x - 10 : node.x + nodeWidth + 10,
          y: node.y + Math.max(node.height / 2, 12) + 14,
          "text-anchor": node.depth === columnCount - 1 ? "end" : "start",
          class: "owner-sankey-value"
        });
        value.textContent = `${node.value}`;
        group.appendChild(value);

        if (node.nodeType !== "product") {
          label.style.cursor = "pointer";
          value.style.cursor = "pointer";
          label.addEventListener("click", () => onNodeSelect(node));
          value.addEventListener("click", () => onNodeSelect(node));
        }

        nodesGroup.appendChild(group);
      });
    });

    svg.appendChild(linksGroup);
    svg.appendChild(nodesGroup);
    container.replaceChildren(svg);
  }

  document.addEventListener("DOMContentLoaded", () => {
    const container = document.querySelector("[data-owner-sankey]");
    const payloadElement = document.querySelector("[data-owner-sankey-payload]");
    const summaryElement = document.querySelector("[data-owner-filter-summary]");
    const resetButton = document.querySelector("[data-owner-filter-reset]");
    const selects = {
      owner: document.querySelector("[data-owner-filter='owner']"),
      domain: document.querySelector("[data-owner-filter='domain']"),
      capability: document.querySelector("[data-owner-filter='capability']"),
      component: document.querySelector("[data-owner-filter='component']")
    };

    if (!container || !payloadElement || !summaryElement || !resetButton || Object.values(selects).some((element) => !element)) {
      return;
    }

    let payload;
    try {
      payload = JSON.parse(payloadElement.textContent || "{}");
    } catch (error) {
      container.textContent = "The sankey visualization could not be rendered.";
      return;
    }

    const allPaths = normalizePaths(payload);
    if (allPaths.length === 0) {
      container.textContent = "No sankey data available.";
      summaryElement.textContent = "No complete mapping paths available.";
      return;
    }

    const filters = {
      owner: "",
      domain: "",
      capability: "",
      component: ""
    };

    function render() {
      rebuildFilterOptions(allPaths, selects, filters);
      const filteredPaths = filterPaths(allPaths, filters);
      updateSummary(summaryElement, filters, filteredPaths);
      buildSankey(container, buildSankeyData(filteredPaths), (node) => {
        if (node.nodeType === "owner") {
          filters.owner = node.id.slice("owner:".length);
          filters.domain = "";
          filters.capability = "";
          filters.component = "";
        } else if (node.nodeType === "domain") {
          filters.domain = node.id.slice("domain:".length);
          filters.capability = "";
          filters.component = "";
        } else if (node.nodeType === "capability") {
          filters.capability = node.id.slice("capability:".length);
          filters.component = "";
        } else if (node.nodeType === "component") {
          filters.component = node.id.slice("component:".length);
        }

        render();
      });
    }

    selects.owner.addEventListener("change", () => {
      filters.owner = selects.owner.value;
      filters.domain = "";
      filters.capability = "";
      filters.component = "";
      render();
    });

    selects.domain.addEventListener("change", () => {
      filters.domain = selects.domain.value;
      filters.capability = "";
      filters.component = "";
      render();
    });

    selects.capability.addEventListener("change", () => {
      filters.capability = selects.capability.value;
      filters.component = "";
      render();
    });

    selects.component.addEventListener("change", () => {
      filters.component = selects.component.value;
      render();
    });

    resetButton.addEventListener("click", () => {
      filters.owner = "";
      filters.domain = "";
      filters.capability = "";
      filters.component = "";
      render();
    });

    render();
  });
})();

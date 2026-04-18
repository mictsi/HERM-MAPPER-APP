(function () {
  const echartsApi = window.echarts;

  function computeGraphHeight(productCount) {
    const estimatedRows = Math.ceil(Math.max(productCount, 1) / 4);
    const byRows = 360 + (estimatedRows * 220);
    const byNodes = 360 + (Math.max(productCount, 1) * 52);
    return Math.max(560, Math.min(1920, Math.max(byRows, byNodes)));
  }

  function compareText(left, right) {
    return left.localeCompare(right, undefined, { sensitivity: "base" });
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
          domainLabel: String(item.domainLabel ?? item.DomainLabel ?? "").trim(),
          capabilityLabel: String(item.capabilityLabel ?? item.CapabilityLabel ?? "").trim(),
          componentLabel: String(item.componentLabel ?? item.ComponentLabel ?? "").trim(),
          productId: Number(item.productId ?? item.ProductId ?? 0),
          productName: String(item.productName ?? item.ProductName ?? "").trim()
        }))
        .filter((item) => item.ownerName !== "" && item.productName !== "" && Number.isFinite(item.productId) && item.productId > 0);
    } catch (error) {
      console.error("Unable to parse owner products payload", error);
      return [];
    }
  }

  function groupProducts(paths) {
    const groups = new Map();

    paths.forEach((path) => {
      const key = String(path.productId);
      let entry = groups.get(key);
      if (!entry) {
        entry = {
          productId: path.productId,
          productName: path.productName,
          mappings: new Set(),
          domains: new Set(),
          capabilities: new Set(),
          components: new Set()
        };
        groups.set(key, entry);
      }

      entry.mappings.add(path.mappingId);
      if (path.domainLabel !== "") {
        entry.domains.add(path.domainLabel);
      }
      if (path.capabilityLabel !== "") {
        entry.capabilities.add(path.capabilityLabel);
      }
      if (path.componentLabel !== "") {
        entry.components.add(path.componentLabel);
      }
    });

    return Array.from(groups.values()).sort((left, right) => compareText(left.productName, right.productName));
  }

  function buildChartOption(ownerName, productGroups) {
    const productCount = productGroups.length;
    const ownerNode = {
      id: `owner:${ownerName}`,
      name: ownerName,
      category: 0,
      symbolSize: Math.max(56, Math.min(80, 52 + productGroups.length * 2)),
      value: productGroups.length,
      mappingCount: productGroups.reduce((total, product) => total + product.mappings.size, 0),
      itemStyle: {
        color: "#1d5c81"
      },
      label: {
        color: "#f8fafc",
        fontWeight: 700
      }
    };

    const productNodes = productGroups.map((product) => ({
      id: `product:${product.productId}`,
      name: product.productName,
      category: 1,
      symbolSize: Math.max(28, Math.min(58, 24 + product.mappings.size * 8)),
      value: product.mappings.size,
      mappingCount: product.mappings.size,
      domainCount: product.domains.size,
      capabilityCount: product.capabilities.size,
      componentCount: product.components.size,
      itemStyle: {
        color: "#d97706"
      }
    }));

    const links = productGroups.map((product) => ({
      source: ownerNode.id,
      target: `product:${product.productId}`,
      value: product.mappings.size,
      lineStyle: {
        width: Math.max(2, Math.min(8, product.mappings.size + 1))
      }
    }));

    return {
      animationDuration: 550,
      animationEasingUpdate: "quarticOut",
      tooltip: {
        trigger: "item",
        backgroundColor: "rgba(17, 24, 39, 0.92)",
        borderWidth: 0,
        textStyle: {
          color: "#f8fafc"
        },
        formatter(params) {
          if (params.dataType === "edge") {
            return `${ownerName}<br/>${params.data?.value ?? 0} mapping path(s)`;
          }

          const node = params.data ?? {};
          if (node.category === 0) {
            return `${node.name}<br/>${node.value ?? 0} mapped product(s)<br/>${node.mappingCount ?? 0} mapping path(s)`;
          }

          return `${node.name}<br/>${node.mappingCount ?? 0} mapping path(s)<br/>${node.domainCount ?? 0} domain(s)<br/>${node.capabilityCount ?? 0} capability(s)<br/>${node.componentCount ?? 0} component(s)`;
        }
      },
      legend: {
        bottom: 0,
        textStyle: {
          color: "#486170"
        },
        data: ["Owner", "Product"]
      },
      series: [
        {
          type: "graph",
          layout: "force",
          roam: true,
          draggable: true,
          edgeSymbol: ["none", "arrow"],
          edgeSymbolSize: [0, 8],
          force: {
            repulsion: Math.max(320, 180 + (productCount * 18)),
            gravity: 0.05,
            edgeLength: [150, Math.max(220, 160 + (productCount * 8))]
          },
          label: {
            show: true,
            position: "right",
            color: "#12212d",
            fontSize: 12
          },
          labelLayout: {
            hideOverlap: true
          },
          lineStyle: {
            color: "source",
            curveness: 0.16,
            opacity: 0.46
          },
          emphasis: {
            focus: "adjacency"
          },
          categories: [
            { name: "Owner" },
            { name: "Product" }
          ],
          data: [ownerNode].concat(productNodes),
          links
        }
      ]
    };
  }

  function initializeReport(container) {
    if (typeof echartsApi?.init !== "function") {
      return;
    }

    const select = container.querySelector("[data-owner-products-filter]");
    const summary = container.querySelector("[data-owner-products-summary]");
    const host = container.querySelector("[data-owner-products-chart]");
    const payloadScript = container.querySelector("[data-owner-products-payload]");

    if (!(select instanceof HTMLSelectElement) || !(summary instanceof HTMLElement) || !(host instanceof HTMLElement) || !(payloadScript instanceof HTMLScriptElement)) {
      return;
    }

    const paths = parsePayload(payloadScript);
    if (paths.length === 0) {
      host.textContent = "No mapped products available for the selected owner.";
      summary.textContent = "No owner mapping paths are available.";
      return;
    }

    const chart = echartsApi.init(host, null, { renderer: "canvas" });
    let lastHeight = 0;

    function resizeChart(productCount) {
      const nextHeight = computeGraphHeight(productCount);
      if (nextHeight !== lastHeight) {
        host.style.height = `${nextHeight}px`;
        lastHeight = nextHeight;
      }

      chart.resize();
    }

    function render() {
      const ownerName = select.value;
      const filteredPaths = paths.filter((path) => path.ownerName === ownerName);
      const products = groupProducts(filteredPaths);

      if (products.length === 0) {
        chart.clear();
        summary.textContent = `No mapped products are connected to ${ownerName}.`;
        resizeChart(0);
        return;
      }

      summary.textContent = `${products.length} mapped product(s) connected to ${ownerName} across ${filteredPaths.length} complete mapping path(s).`;
      chart.setOption(buildChartOption(ownerName, products), true);
      resizeChart(products.length);
    }

    if (select.value === "" && select.options.length > 0) {
      select.selectedIndex = 0;
    }

    select.addEventListener("change", render);
    render();

    if (typeof ResizeObserver === "function") {
      const resizeObserver = new ResizeObserver(() => resizeChart(groupProducts(paths.filter((path) => path.ownerName === select.value)).length));
      resizeObserver.observe(host.parentElement ?? host);
    } else {
      window.addEventListener("resize", () => resizeChart(groupProducts(paths.filter((path) => path.ownerName === select.value)).length));
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-owner-products-report]").forEach((container) => {
      if (container instanceof HTMLElement) {
        initializeReport(container);
      }
    });
  });
}());
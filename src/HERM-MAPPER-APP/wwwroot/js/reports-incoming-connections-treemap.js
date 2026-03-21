(function () {
  const echartsApi = window.echarts;

  function parsePayload(scriptElement) {
    try {
      const parsed = JSON.parse(scriptElement?.textContent ?? "[]");
      if (!Array.isArray(parsed)) {
        return [];
      }

      return parsed
        .filter((item) => item !== null && typeof item === "object")
        .map((item) => ({
          productId: item.productId ?? item.ProductId,
          productName: String(item.productName ?? item.ProductName ?? ""),
          displayLabel: String(item.displayLabel ?? item.DisplayLabel ?? ""),
          vendor: String(item.vendor ?? item.Vendor ?? ""),
          version: String(item.version ?? item.Version ?? ""),
          incomingConnectionCount: Number(item.incomingConnectionCount ?? item.IncomingConnectionCount ?? 0),
          serviceCount: Number(item.serviceCount ?? item.ServiceCount ?? 0)
        }))
        .filter((item) => item.productName !== "" && Number.isFinite(item.incomingConnectionCount) && item.incomingConnectionCount > 0);
    } catch (error) {
      console.error("Unable to parse incoming connections treemap payload", error);
      return [];
    }
  }

  function buildTreeData(rows) {
    return rows.map((row) => ({
      id: `product:${row.productId}`,
      name: row.displayLabel || row.productName,
      value: row.incomingConnectionCount,
      productName: row.productName,
      vendor: row.vendor,
      version: row.version,
      serviceCount: row.serviceCount,
      incomingConnectionCount: row.incomingConnectionCount
    }));
  }

  function initializeTreemap(host) {
    if (typeof echartsApi?.init !== "function") {
      return;
    }

    const payloadScript = host.parentElement?.querySelector("[data-incoming-connections-treemap-payload]");
    if (!(payloadScript instanceof HTMLScriptElement)) {
      return;
    }

    const rows = parsePayload(payloadScript);
    if (rows.length === 0) {
      host.textContent = "No incoming service connections available.";
      return;
    }

    const chart = echartsApi.init(host, null, { renderer: "canvas" });

    chart.setOption({
      tooltip: {
        trigger: "item",
        backgroundColor: "rgba(17, 24, 39, 0.92)",
        borderWidth: 0,
        textStyle: {
          color: "#f8fafc"
        },
        formatter(params) {
          const node = params.data ?? {};
          const detail = [node.vendor, node.version]
            .filter((value) => typeof value === "string" && value.trim() !== "")
            .join(" ");
          const detailLine = detail === "" ? "" : `<br/>${detail}`;
          return `${node.productName ?? params.name}<br/>${node.incomingConnectionCount ?? params.value} incoming connection(s)<br/>${node.serviceCount ?? 0} service(s)${detailLine}`;
        }
      },
      series: [
        {
          type: "treemap",
          roam: false,
          nodeClick: false,
          breadcrumb: {
            show: false
          },
          color: [
            "#0f766e",
            "#0d9488",
            "#14b8a6",
            "#2563eb",
            "#7c3aed",
            "#f59e0b",
            "#f97316",
            "#dc2626"
          ],
          itemStyle: {
            borderColor: "#f8fafc",
            borderWidth: 3,
            gapWidth: 3
          },
          label: {
            show: true,
            color: "#f8fafc",
            fontSize: 13,
            fontWeight: 600,
            lineHeight: 18,
            overflow: "break",
            formatter(params) {
              const node = params.data ?? {};
              return `${node.productName ?? params.name}\n${node.incomingConnectionCount ?? params.value}`;
            }
          },
          upperLabel: {
            show: false
          },
          levels: [
            {
              itemStyle: {
                borderColor: "#ffffff",
                borderWidth: 4,
                gapWidth: 4
              }
            }
          ],
          data: buildTreeData(rows)
        }
      ]
    });

    if (typeof ResizeObserver === "function") {
      const resizeObserver = new ResizeObserver(() => chart.resize());
      resizeObserver.observe(host);
    } else {
      window.addEventListener("resize", () => chart.resize());
    }
  }

  document.addEventListener("DOMContentLoaded", () => {
    document.querySelectorAll("[data-incoming-connections-treemap]").forEach((host) => {
      if (host instanceof HTMLElement) {
        initializeTreemap(host);
      }
    });
  });
}());

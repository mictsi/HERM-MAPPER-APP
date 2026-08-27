// Filters the DRM common sub-class dropdown to the sub-classes that belong to the
// selected DRM entity. Loaded per page so an unrelated failure in site.js cannot
// stop the two selects from staying in sync.
(() => {
  const initialiseEditor = (editor) => {
    const optionsScript = editor.querySelector("[data-drm-common-subclass-options]");
    const entitySelect = editor.querySelector("[data-drm-entity-select]");
    const subClassSelect = editor.querySelector("[data-drm-common-subclass-select]");

    if (!(optionsScript instanceof HTMLScriptElement) ||
      !(entitySelect instanceof HTMLSelectElement) ||
      !(subClassSelect instanceof HTMLSelectElement)) {
      return;
    }

    if (editor.hasAttribute("data-drm-data-entity-editor-ready")) {
      return;
    }

    editor.setAttribute("data-drm-data-entity-editor-ready", "true");

    let parsedOptions;
    try {
      parsedOptions = JSON.parse(optionsScript.textContent ?? "[]");
    } catch (error) {
      console.error("Unable to parse DRM common sub-class options", error);
      return;
    }

    const normalizedOptions = Array.isArray(parsedOptions)
      ? parsedOptions
        .filter((option) => option !== null && typeof option === "object")
        .map((option) => ({
          id: String(option.id ?? option.Id ?? ""),
          parentEntityId: String(option.parentEntityId ?? option.ParentEntityId ?? ""),
          label: String(option.label ?? option.Label ?? "")
        }))
        .filter((option) => option.id !== "" && option.parentEntityId !== "" && option.label !== "")
      : [];

    if (normalizedOptions.length === 0) {
      console.warn("No DRM common sub-class options were supplied to the data-entity editor.");
    }

    const optionsByEntityId = new Map();
    normalizedOptions.forEach((option) => {
      if (!optionsByEntityId.has(option.parentEntityId)) {
        optionsByEntityId.set(option.parentEntityId, []);
      }

      optionsByEntityId.get(option.parentEntityId).push(option);
    });

    optionsByEntityId.forEach((items) => {
      items.sort((left, right) => left.label.localeCompare(right.label));
    });

    const syncCommonSubClasses = () => {
      const entityId = entitySelect.value;
      const previousSubClassId = subClassSelect.value;
      const matchingOptions = optionsByEntityId.get(entityId) ?? [];

      subClassSelect.innerHTML = "";

      const placeholderOption = document.createElement("option");
      placeholderOption.value = "";
      if (entityId === "") {
        placeholderOption.textContent = "Choose a DRM entity first";
      } else {
        placeholderOption.textContent = matchingOptions.length === 0
          ? "No common sub-classes for this entity"
          : "Use the entity itself";
      }

      subClassSelect.appendChild(placeholderOption);

      matchingOptions.forEach((option) => {
        const element = document.createElement("option");
        element.value = option.id;
        element.textContent = option.label;
        subClassSelect.appendChild(element);
      });

      if (matchingOptions.some((option) => option.id === previousSubClassId)) {
        subClassSelect.value = previousSubClassId;
      } else {
        subClassSelect.value = "";
      }

      subClassSelect.disabled = entityId === "" || matchingOptions.length === 0;
    };

    entitySelect.addEventListener("change", syncCommonSubClasses);
    syncCommonSubClasses();
  };

  const initialise = () => {
    document.querySelectorAll("[data-drm-data-entity-editor]").forEach((editor) => {
      try {
        initialiseEditor(editor);
      } catch (error) {
        console.error("Unable to initialise the DRM data-entity editor", error);
      }
    });
  };

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initialise);
  } else {
    initialise();
  }
})();

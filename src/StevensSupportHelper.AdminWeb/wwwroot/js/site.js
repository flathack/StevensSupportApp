document.addEventListener("DOMContentLoaded", function () {
    initializeSidebar();
    initializeClientWorkspace();
});

function initializeSidebar() {
    var toggle = document.getElementById("sidebarToggle");
    var sidebar = document.getElementById("appSidebar");

    if (!toggle || !sidebar) {
        return;
    }

    toggle.addEventListener("click", function () {
        sidebar.classList.toggle("open");
    });
}

function initializeClientWorkspace() {
    var workspace = document.querySelector(".client-workspace");
    if (!workspace) {
        return;
    }

    var tabsContainer = document.getElementById("clientWorkspaceTabs");
    var stateInput = document.getElementById("clientOpenTabsState");
    var activeInput = document.getElementById("clientActiveTabState");
    var hiddenStateInputs = document.querySelectorAll(".tab-state-input");
    var hiddenActiveInputs = document.querySelectorAll(".tab-active-input");
    var panels = Array.from(document.querySelectorAll("[data-workspace-panel]"));
    var launchers = Array.from(document.querySelectorAll("[data-client-tool]"));

    if (!tabsContainer || !stateInput || !activeInput || panels.length === 0) {
        return;
    }

    var panelMap = new Map();
    panels.forEach(function (panel) {
        panelMap.set(panel.dataset.workspacePanel, panel);
    });

    var openTabs = parseState(workspace.dataset.initialOpenTabs || stateInput.value || "overview");
    if (openTabs.indexOf("overview") === -1) {
        openTabs.unshift("overview");
    }

    var activeTab = workspace.dataset.initialActiveTab || activeInput.value || openTabs[0];
    if (openTabs.indexOf(activeTab) === -1) {
        activeTab = openTabs[0];
    }

    launchers.forEach(function (launcher) {
        launcher.addEventListener("click", function () {
            var key = launcher.dataset.clientTool;
            if (!panelMap.has(key)) {
                return;
            }

            if (openTabs.indexOf(key) === -1) {
                openTabs.push(key);
            }

            activeTab = key;
            renderWorkspace();
        });
    });

    function renderWorkspace() {
        tabsContainer.innerHTML = "";

        openTabs.forEach(function (tabKey) {
            var panel = panelMap.get(tabKey);
            if (!panel) {
                return;
            }

            var tab = document.createElement("div");
            tab.className = "workspace-tab" + (tabKey === activeTab ? " active" : "");

            var labelButton = document.createElement("button");
            labelButton.type = "button";
            labelButton.className = "workspace-tab-label";
            labelButton.textContent = panel.dataset.tabLabel || tabKey;
            labelButton.addEventListener("click", function () {
                activeTab = tabKey;
                renderWorkspace();
            });
            tab.appendChild(labelButton);

            if (tabKey !== "overview") {
                var closeButton = document.createElement("button");
                closeButton.type = "button";
                closeButton.className = "workspace-tab-close";
                closeButton.setAttribute("aria-label", "Tab schließen");
                closeButton.textContent = "×";
                closeButton.addEventListener("click", function () {
                    openTabs = openTabs.filter(function (entry) { return entry !== tabKey; });
                    if (activeTab === tabKey) {
                        activeTab = openTabs[openTabs.length - 1] || "overview";
                    }

                    renderWorkspace();
                });
                tab.appendChild(closeButton);
            }

            tabsContainer.appendChild(tab);
        });

        panels.forEach(function (panel) {
            var key = panel.dataset.workspacePanel;
            var isVisible = openTabs.indexOf(key) !== -1;
            var isActive = key === activeTab;
            panel.classList.toggle("active", isVisible && isActive);
        });

        var serializedTabs = openTabs.join(",");
        stateInput.value = serializedTabs;
        activeInput.value = activeTab;

        hiddenStateInputs.forEach(function (input) {
            input.value = serializedTabs;
        });

        hiddenActiveInputs.forEach(function (input) {
            input.value = activeTab;
        });
    }

    renderWorkspace();
}

function parseState(value) {
    return value
        .split(",")
        .map(function (entry) { return entry.trim(); })
        .filter(function (entry, index, array) { return entry.length > 0 && array.indexOf(entry) === index; });
}

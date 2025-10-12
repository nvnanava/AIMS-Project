function debounce(func, delay) {
    let timeoutId;
    return function (...args) {
        clearTimeout(timeoutId); // Clear any existing timeout
        timeoutId = setTimeout(() => {
            func.apply(this, args); // Execute the original function after the delay
        }, delay);
    };
}



document.addEventListener("DOMContentLoaded", function () {
    // --- Modal and DOM Elements ---
    const assignBtn = document.getElementById("assign-asset-button");
    const modal = new bootstrap.Modal(document.getElementById("assignAssetModal"));
    const userSelect = document.getElementById("userSelect");
    const assetSelect = document.getElementById("assetSelect");
    let userPreviousOptionLength = userSelect.options.length;
    let assetPreviousOptionLength = assetSelect.options.length;

    // --- Create Dynamic Search Box Above Dropdown ---
    const searchInput = document.createElement("input");
    searchInput.type = "text";
    searchInput.placeholder = "Search users...";
    searchInput.className = "form-control mb-2";
    userSelect.parentElement.insertBefore(searchInput, userSelect);


    const assetInput = document.createElement("input");
    assetInput.type = "text";
    assetInput.placeholder = "Search assets...";
    assetInput.className = "form-control mb-2";
    assetSelect.parentElement.insertBefore(assetInput, assetSelect);


    // --- Open Dropdown on Focus ---
    searchInput.addEventListener("focus", () => {
        populateUserDropdown();
        userPreviousOptionLength = userSelect.options.length;
        userSelect.size = Math.min(userSelect.options.length, 11);
        userSelect.style.display = "block";
    });

    // --- Select First Matching Option on Enter ---
    searchInput.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            const firstSelectable = Array.from(userSelect.options).find(opt => !opt.disabled);
            if (firstSelectable) {
                firstSelectable.selected = true;
                searchInput.value = "";
                userSelect.size = 1;
            }
        }
    });

    // --- Collapse Dropdown on Selection ---
    userSelect.addEventListener("change", function () {
        userSelect.size = 1;
        searchInput.value = "";
    });

    // --- Repopulate on Focus if Filtered Previously ---
    userSelect.addEventListener("focus", function () {
        if (searchInput.value === "" && userSelect.options.length < userPreviousOptionLength) {
            populateUserDropdown();
        }
    });

    // --- Close Dropdown if Clicking Outside ---
    document.addEventListener("click", (e) => {
        if (!searchInput.contains(e.target) && !userSelect.contains(e.target)) {
            userSelect.size = 1;
        }

        if (!assetInput.contains(e.target) && !assetSelect.contains(e.target)) {
            assetSelect.size = 1;
        }
    });

    function populateUserDropdown(searchTerm = "") {

        // fetch data
        fetch(`/api/user?searchString=${encodeURIComponent(searchTerm)}`)
            .then((response) => {
                return response.json();
            })
            .then((results) => {

                //clear out the old children
                userSelect.replaceChildren();

                results.forEach(user => {
                    const option = document.createElement("option");
                    option.value = user.fullName;
                    option.text = user.fullName;
                    option.dataset.user_id = user.userID;
                    userSelect.appendChild(option);
                });
            })
    }

    // --- Open Dropdown on Focus ---
    assetInput.addEventListener("focus", () => {
        populateAssetDropdown();
        assetPreviousOptionLength = assetSelect.options.length;
        assetSelect.size = Math.min(assetSelect.options.length, 11);
        assetSelect.style.display = "block";
    });

    // --- Select First Matching Option on Enter ---
    assetInput.addEventListener("keydown", function (e) {
        if (e.key === "Enter") {
            e.preventDefault();
            const firstSelectable = Array.from(assetSelect.options).find(opt => !opt.disabled);
            if (firstSelectable) {
                firstSelectable.selected = true;
                assetInput.value = "";
                assetSelect.size = 1;
            }
        }
    });

    // --- Collapse Dropdown on Selection ---
    assetSelect.addEventListener("change", function () {
        assetSelect.size = 1;
        assetInput.value = "";
    });

    // --- Repopulate on Focus if Filtered Previously ---
    assetSelect.addEventListener("focus", function () {
        if (assetInput.value === "" && assetSelect.options.length < assetPreviousOptionLength) {
            populateAssetDropdown();
        }
    });

    function populateAssetDropdown(searchTerm = "") {
        // Build a URL that works with both server shapes (q|searchString) and favors available assets
        const url =
            `/api/diag/assets?q=${encodeURIComponent(searchTerm)}` +
            `&searchString=${encodeURIComponent(searchTerm)}` +
            `&onlyAvailable=true&take=30`;

        fetch(url)
            .then(async (response) => {
                // Handle empty responses gracefully
                if (response.status === 204) return [];

                const contentType = response.headers.get('content-type') || '';
                const bodyText = await response.text();

                if (!response.ok) {
                    // Show real server error details in our toast
                    throw new Error(`HTTP ${response.status} ${response.statusText} — ${bodyText}`);
                }
                if (!contentType.includes('application/json')) {
                    throw new Error(`Expected JSON but got ${contentType}. Body: ${bodyText}`);
                }
                // It’s JSON, parse it ourselves because we already consumed text:
                return JSON.parse(bodyText);
            })
            .then((results) => {
                assetSelect.replaceChildren();
                results.forEach(asset => {
                    const option = document.createElement("option");
                    option.value = `(${asset.assetID}) ${asset.assetName}`;
                    option.text = `(${asset.assetID}) ${asset.assetName}`;
                    option.dataset.asset_id = asset.assetID;      // <-- numeric ID (HardwareID or SoftwareID)
                    option.dataset.asset_kind = asset.assetKind;  // <-- 1 = Hardware, 2 = Software
                    assetSelect.appendChild(option);
                });
            })
    }

    // --- Open Modal & PRepare Fields ---
    assignBtn.addEventListener("click", function () {
        populateUserDropdown();
        populateAssetDropdown();
        document.getElementById("commentBox").value = "";
        modal.show();
    });

    // --- Filter Users on Typing ---
    searchInput.addEventListener("input", function () {
        populateUserDropdown(this.value);
    });
    // --- Filter Users on Typing ---
    assetInput.addEventListener("input", function () {
        populateAssetDropdown(this.value);
    });


    // --- Submit Assignment Logic ---
    document.getElementById("assignAssetForm")?.addEventListener("submit", function (e) {
        e.preventDefault();

        const selectedUserOption = userSelect.options[userSelect.selectedIndex];
        const selectedAssetOption = assetSelect.options[assetSelect.selectedIndex];

        const selectedUserID = Number(selectedUserOption.dataset.user_id);
        const selectedAssetID = Number(selectedAssetOption.dataset.asset_id);
        const selectedAssetKind = Number(selectedAssetOption.dataset.asset_kind);
        const comment = (document.getElementById("commentBox").value || "").trim();

        const url = '/api/assign/create';

        // Build payload with the correct ID property
        const data = {
            userID: selectedUserID,
            assetKind: selectedAssetKind
        };
        if (selectedAssetKind === 1) data.hardwareID = selectedAssetID; // <-- was assetTag (wrong)
        if (selectedAssetKind === 2) data.softwareID = selectedAssetID;

        if (comment) data.comment = comment; // safe to include even if the API ignores it

        fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(data)
        })
            .then(response => {
                if (!response.ok) {
                    return response.text().then(errorBody => {
                        throw `Assignment Failed: ${errorBody}`;
                    });
                }
                return response.json();
            })
            .then(result => {
                // Toast success
                new bootstrap.Toast(document.getElementById("assignToast"), { delay: 3000 }).show();

                // Refresh the search table so the new assignment shows up
                if (typeof window.refreshSearchTable === 'function') {
                    window.refreshSearchTable();
                } else {
                    location.reload();
                }
            })
            .catch(error => {
                const toastElement = document.getElementById("errorToast");
                const errorToast = new bootstrap.Toast(toastElement, { delay: 3000 });
                toastElement.querySelector('.toast-body').innerHTML = error;
                errorToast.show();
            });

        modal.hide();
    });
    
    // --- Placeholder: User Agreement Upload Handler ---
    const fileInput = document.getElementById("userAgreementUpload");
    fileInput.addEventListener("change", function () {
        const selectedFile = fileInput.files[0];
        if (selectedFile) {
            // TODO: Upload file to SharePoint using Microsoft Graph API
            // TODO: Log file name + date + user in audit log table
        }
    });

    // --- Future Considerations ---
    // TODO: Consiter integrating with DocuSign or similar for dynamic agreement generation.
    // The form could autofill asset + user info and require a signature before assignment completes.
});

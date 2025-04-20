document.addEventListener("DOMContentLoaded", function () {
    // --- Modal and DOM Elements ---
    const assignBtn = document.getElementById("assign-asset-button");
    const modal = new bootstrap.Modal(document.getElementById("assignAssetModal"));
    const userSelect = document.getElementById("userSelect");
    const assetSelect = document.getElementById("assetSelect");
    let previousOptionLength = userSelect.options.length;

    // --- Create Dynamic Search Box Above Dropdown ---
    const searchInput = document.createElement("input");
    searchInput.type = "text";
    searchInput.placeholder = "Search users...";
    searchInput.className = "form-control mb-2";
    userSelect.parentElement.insertBefore(searchInput, userSelect);

    // --- Open Dropdown on Focus ---
    searchInput.addEventListener("focus", () => {
        populateUserDropdown();
        previousOptionLength = userSelect.options.length;
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
        if (userSelect.options.length < previousOptionLength) {
            populateUserDropdown();
        }
    });

    // --- Close Dropdown if Clicking Outside ---
    document.addEventListener("click", (e) => {
        if (!searchInput.contains(e.target) && !userSelect.contains(e.target)) {
            userSelect.size = 1;
        }
    });

    // --- User Dropdown Logic ---
    function populateUserDropdown(searchTerm = "") {
        userSelect.innerHTML = '<option disabled selected value="">Choose a user...</option>';
        users
            .filter(u => `${u.Name} (${u.ID})`.toLowerCase().includes(searchTerm.toLowerCase()))
            .slice(0, 20)
            .forEach(user => {
                const option = document.createElement("option");
                option.value = user.ID;
                option.textContent = `${user.Name} (${user.ID})`;
                userSelect.appendChild(option);
            });
    }

    // --- Asset Dropdown Logic (Filter for Available Only) ---
    function populateAssetDropdown() {
        assetSelect.innerHTML = '<option disabled selected value="">Choose an asset...</option>';
        tableData
            .filter(a => a.Status === "Available")
            .forEach(asset => {
                const option = document.createElement("option");
                option.value = asset["Tag #"];
                option.textContent = `${asset["Asset Name"]} - ${asset["Tag #"]}`;
                assetSelect.appendChild(option);
            });
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


    // --- Submit Assignment Logic ---
    document.getElementById("assignAssetForm")?.addEventListener("submit", function (e) {
        e.preventDefault();

        const selectedUserID = userSelect.value;
        const selectedAssetTag = assetSelect.value;

        const selectedUser = users.find(u => u.ID === selectedUserID);
        const selectedAsset = tableData.find(a => a["Tag #"] === selectedAssetTag);

        const comment = document.getElementById("commentBox").value.trim();

        if (selectedUser && selectedAsset) {

            // Update dummy dataset
            selectedAsset["Assigned To"] = `${selectedUser.Name} (${selectedUserID})`;
            selectedAsset["Status"] = "Assigned";

            // Update visible UI
            const rows = document.querySelectorAll("#table-body tr");
            rows.forEach(row => {
                const tagCell = row.querySelector("td:nth-child(3)");
                if (tagCell && tagCell.textContent.trim() === selectedAssetTag) {
                    const assignedToCell = row.querySelector("td:nth-child(4)");
                    const statusCell = row.querySelector("td:nth-child(5)");
                    const existingSpan = statusCell.querySelector("span");

                    if (assignedToCell) assignedToCell.textContent = `${selectedUser.Name} (${selectedUser.ID})`;
                    if (existingSpan) {
                        existingSpan.className = "status assigned";
                        existingSpan.textContent = "Assigned";
                    }
                }
            });
            // TODO: Store comment in audit log table (future database integration)
            // TODO: When backend is connected, send assignment + comment to database.
        }
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
